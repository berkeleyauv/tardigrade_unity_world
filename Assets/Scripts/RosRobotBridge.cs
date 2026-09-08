using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Nav;
using RosMessageTypes.Rosgraph;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.TardigradeInterfaces;
using Tardigrade.Simulation;

/// <summary>ROS transport/lifecycle composition root for the deterministic plant.</summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider), typeof(AuvPhysicsPlant))]
public sealed class RosRobotBridge : MonoBehaviour
{
    public double SimulationTime => simulationTime;
    public uint seed = 1;
    public string scenarioId = "gate_nominal";
    public float fixedRateHz = 100f;
    public float staleCommandTimeoutSeconds = 0.5f;
    [Range(0f, 1f)] public float voltageScale = 1f;
    public string thrusterCommandTopic = "/tardigrade/actuators/thruster_commands";
    public string groundTruthTopic = "/tardigrade/sim/ground_truth/odometry";
    public string imuTopic = "/tardigrade/sensors/imu/data";
    public string pressureTopic = "/tardigrade/sensors/pressure";
    public string visualOdometryTopic = "/tardigrade/sensors/visual_odometry";
    public string statusTopic = "/tardigrade/status";

    ROSConnection ros;
    SimulationConfig config;
    [Header("Authored simulation references")]
    [SerializeField] Rigidbody body;
    [SerializeField] BoxCollider bodyCollider;
    [SerializeField] AuvPhysicsPlant plant;
    [SerializeField] UnderwaterEnvironment underwaterEnvironment;
    [SerializeField] PoolScenario poolScenario;
    [SerializeField] GateScenario gateScenario;
    [SerializeField] StereoCameraPublisher stereoCameras;

    DeterministicNoise noise;
    Vector3 sceneOriginPosition;
    Quaternion sceneOriginRotation;
    double simulationTime;
    double nextTruthTime, nextStatusTime, nextImuTime, nextPressureTime, nextVioTime;
    bool havePreviousImuVelocity;
    Vector3 previousImuVelocity, accelerometerBias, gyroBias, vioPositionDrift;
    float pressureBias, vioYawDrift;
    double scenarioStartTime;

    sealed class Pending<T> where T : Message
    {
        public double deliveryTime;
        public T message;
    }

    readonly Queue<Pending<ImuMsg>> imuQueue = new Queue<Pending<ImuMsg>>();
    readonly Queue<Pending<FluidPressureMsg>> pressureQueue = new Queue<Pending<FluidPressureMsg>>();
    readonly Queue<Pending<OdometryMsg>> vioQueue = new Queue<Pending<OdometryMsg>>();

    public void SetAuthoredReferences(Rigidbody authoredBody, BoxCollider authoredCollider,
        AuvPhysicsPlant authoredPlant, UnderwaterEnvironment authoredEnvironment,
        PoolScenario authoredPool, GateScenario authoredGate,
        StereoCameraPublisher authoredCameras)
    {
        body = authoredBody;
        bodyCollider = authoredCollider;
        plant = authoredPlant;
        underwaterEnvironment = authoredEnvironment;
        poolScenario = authoredPool;
        gateScenario = authoredGate;
        stereoCameras = authoredCameras;
    }

    void Awake()
    {
        Time.fixedDeltaTime = 1f / Mathf.Max(fixedRateHz, 1f);
        Physics.defaultSolverIterations = 12;
        Physics.defaultSolverVelocityIterations = 4;
        sceneOriginPosition = transform.position;
        sceneOriginRotation = transform.rotation;
        config = SimulationConfig.Load();
        noise = new DeterministicNoise(seed);
        body = body != null ? body : GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        bodyCollider = bodyCollider != null ? bodyCollider : GetComponent<BoxCollider>();
        if (bodyCollider == null)
            bodyCollider = gameObject.AddComponent<BoxCollider>();
        float[] size = config.rigid_body.collision_size_m;
        bodyCollider.size = new Vector3(size[1], size[2], size[0]);
        plant = plant != null ? plant : GetComponent<AuvPhysicsPlant>();
        if (plant == null)
            plant = gameObject.AddComponent<AuvPhysicsPlant>();
        plant.Initialize(config);
        plant.CommandTimeoutSeconds = staleCommandTimeoutSeconds;
        plant.VoltageScale = voltageScale;
        underwaterEnvironment = underwaterEnvironment != null ? underwaterEnvironment :
            GetComponent<UnderwaterEnvironment>();
        if (underwaterEnvironment == null)
            underwaterEnvironment = gameObject.AddComponent<UnderwaterEnvironment>();
        gateScenario = gateScenario != null ? gateScenario :
            FindFirstObjectByType<GateScenario>();
        if (gateScenario == null)
        {
            Debug.LogWarning("No authored GateScenario found; creating compatibility geometry at runtime.");
            gateScenario = new GameObject("GateAcceptance").AddComponent<GateScenario>();
        }
        gateScenario.Build(sceneOriginPosition, sceneOriginRotation);
        poolScenario = poolScenario != null ? poolScenario :
            FindFirstObjectByType<PoolScenario>();
        if (poolScenario == null)
        {
            Debug.LogWarning("No authored PoolScenario found; creating compatibility geometry at runtime.");
            poolScenario = new GameObject("AcceptancePoolCollisions").AddComponent<PoolScenario>();
        }
        poolScenario.Build(config.water.surface_unity_y_m, sceneOriginPosition, sceneOriginRotation);
        ApplyScenario(scenarioId);
        if (scenarioId == "gate_nominal" || scenarioId == "gate_cross_current")
            transform.rotation = sceneOriginRotation * Quaternion.Euler(0f, -20f, 0f);
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<ThrusterCommandsMsg>(thrusterCommandTopic, OnThrusterCommands);
        ros.RegisterPublisher<ClockMsg>("/clock");
        ros.RegisterPublisher<OdometryMsg>(groundTruthTopic);
        ros.RegisterPublisher<ImuMsg>(imuTopic);
        ros.RegisterPublisher<FluidPressureMsg>(pressureTopic);
        ros.RegisterPublisher<OdometryMsg>(visualOdometryTopic);
        ros.RegisterPublisher<RobotStatusMsg>(statusTopic);
        ros.ImplementService<SetArmedRequest, SetArmedResponse>("/tardigrade/set_armed", OnSetArmed);
        ros.ImplementService<SetExternalControlRequest, SetExternalControlResponse>(
            "/tardigrade/set_external_control", OnSetExternalControl);
        ros.ImplementService<ResetSimulationRequest, ResetSimulationResponse>(
            "/tardigrade/sim/reset", OnReset);
        ResetSensorHistory();
        stereoCameras = stereoCameras != null ? stereoCameras : GetComponent<StereoCameraPublisher>();
        if (stereoCameras == null)
            stereoCameras = gameObject.AddComponent<StereoCameraPublisher>();
        stereoCameras.Initialize(config, ros, () => simulationTime);
        stereoCameras.ConfigureVisualProfile(scenarioId, seed);
        Debug.Log($"[Tardigrade SIL] config={config.generated.sha256}; seed={seed}; scenario={scenarioId}");
    }

    void FixedUpdate()
    {
        simulationTime += Time.fixedDeltaTime;
        plant.SimulationTime = simulationTime;
        UpdateTimeDependentScenario();
        ros.Publish("/clock", new ClockMsg(ToRosTime(simulationTime)));
        SampleAtRates();
        Flush(imuQueue, imuTopic);
        Flush(pressureQueue, pressureTopic);
        Flush(vioQueue, visualOdometryTopic);
    }

    void OnThrusterCommands(ThrusterCommandsMsg message)
    {
        if (!plant.AcceptCommands(message, out string reason))
            Debug.LogError("Rejected thruster command: " + reason);
    }

    SetArmedResponse OnSetArmed(SetArmedRequest request)
    {
        plant.Armed = request.armed;
        if (!request.armed) plant.Neutralize();
        return new SetArmedResponse(true, request.armed ? "Unity plant armed" : "Unity plant disarmed");
    }

    SetExternalControlResponse OnSetExternalControl(SetExternalControlRequest request)
    {
        plant.ExternalControlEnabled = request.enabled;
        if (!request.enabled) plant.Neutralize();
        return new SetExternalControlResponse(true, request.enabled ?
            "Unity external control enabled" : "Unity external control disabled");
    }

    ResetSimulationResponse OnReset(ResetSimulationRequest request)
    {
        try
        {
            scenarioId = string.IsNullOrWhiteSpace(request.scenario_id) ? "gate_nominal" : request.scenario_id;
            seed = request.seed;
            float[] position = {
                (float)request.initial_pose.position.x,
                (float)request.initial_pose.position.y,
                (float)request.initial_pose.position.z,
            };
            Vector3 unityPosition = sceneOriginPosition + sceneOriginRotation * SimulationConfig.RosPolarToUnity(position);
            Quaternion unityRotation = sceneOriginRotation * RosQuaternionToUnity(request.initial_pose.orientation);
            plant.ResetPlant(unityPosition, unityRotation);
            noise.Reset(seed);
            ApplyScenario(scenarioId);
            ResetSensorHistory();
            return new ResetSimulationResponse(true,
                $"reset scenario={scenarioId}; seed={seed}; config={config.generated.sha256}");
        }
        catch (Exception error)
        {
            return new ResetSimulationResponse(false, error.Message);
        }
    }

    void ApplyScenario(string id)
    {
        scenarioStartTime = simulationTime;
        config.water.current_body_initial_m_s = new[] { 0f, 0f, 0f };
        if (id == "gate_cross_current" || id == "gate_nominal")
            config.water.current_body_initial_m_s[1] = 0.1f;
        else if (id == "current_impulse")
            config.water.current_body_initial_m_s[1] = 0.1f;
        else if (id.StartsWith("failed_thruster_", StringComparison.Ordinal))
        {
            string suffix = id.Substring("failed_thruster_".Length);
            if (!int.TryParse(suffix, out int slot) || slot < 1 || slot > config.thrusters.Length)
                throw new ArgumentException("Invalid failed-thruster scenario: " + id);
            plant.SetThrusterScale(config.thrusters[slot - 1].name, 0f);
        }
        else if (id == "derated_thruster_1")
            plant.SetThrusterScale(config.thrusters[0].name, 0.5f);
        else if (id != "clean" && id != "sensor_stress" && id != "vio_reset" &&
                 id != "low_visibility" && id != "sensor_dropout" && id != "delayed_messages")
            throw new ArgumentException("Unknown scenario: " + id);
        underwaterEnvironment.Initialize(
            id,
            seed,
            config.water.surface_unity_y_m,
            poolScenario.transform.position,
            poolScenario.transform.rotation,
            poolScenario.insideSizeMeters);
    }

    void UpdateTimeDependentScenario()
    {
        if (scenarioId != "current_impulse")
            return;
        double elapsed = simulationTime - scenarioStartTime;
        config.water.current_body_initial_m_s[1] = elapsed >= 5.0 && elapsed < 7.0 ? 0.35f : 0.1f;
    }

    void ResetSensorHistory()
    {
        imuQueue.Clear(); pressureQueue.Clear(); vioQueue.Clear();
        nextTruthTime = nextStatusTime = nextImuTime = nextPressureTime = nextVioTime = simulationTime;
        havePreviousImuVelocity = false;
        accelerometerBias = gyroBias = vioPositionDrift = Vector3.zero;
        pressureBias = vioYawDrift = 0f;
    }

    void SampleAtRates()
    {
        if (simulationTime >= nextTruthTime)
        {
            ros.Publish(groundTruthTopic, BuildOdometry(simulationTime, false));
            nextTruthTime += 1.0 / 30.0;
        }
        if (simulationTime >= nextStatusTime)
        {
            PublishStatus();
            nextStatusTime += 0.1;
        }
        SensorConfig imu = config.Sensor("imu");
        if (simulationTime >= nextImuTime)
        {
            MaybeQueueImu(imu);
            nextImuTime += 1.0 / imu.rate_hz;
        }
        SensorConfig pressure = config.Sensor("pressure");
        if (simulationTime >= nextPressureTime)
        {
            MaybeQueuePressure(pressure);
            nextPressureTime += 1.0 / pressure.rate_hz;
        }
        SensorConfig vio = config.Sensor("visual_odometry");
        if (simulationTime >= nextVioTime)
        {
            MaybeQueueVio(vio);
            nextVioTime += 1.0 / vio.rate_hz;
        }
    }

    void MaybeQueueImu(SensorConfig sensor)
    {
        Vector3 mount = transform.TransformPoint(SimulationConfig.RosPolarToUnity(sensor.position_m));
        Vector3 pointVelocity = plant.Body.GetPointVelocity(mount);
        if (!havePreviousImuVelocity)
        {
            previousImuVelocity = pointVelocity;
            havePreviousImuVelocity = true;
            return;
        }
        Vector3 worldAcceleration = (pointVelocity - previousImuVelocity) * sensor.rate_hz;
        previousImuVelocity = pointVelocity;
        float accelNoise = sensor.noise_stddev[0], gyroNoise = sensor.noise_stddev[1];
        float scale = NoiseScale();
        accelerometerBias += RandomVector(sensor.bias_walk_stddev[0] / Mathf.Sqrt(sensor.rate_hz));
        gyroBias += RandomVector(sensor.bias_walk_stddev[1] / Mathf.Sqrt(sensor.rate_hz));
        Quaternion mountRotation = transform.rotation * RosRpyToUnity(sensor.rpy_rad);
        Vector3 specificLocal = Quaternion.Inverse(mountRotation) * (worldAcceleration - Physics.gravity);
        Vector3 angularLocal = Quaternion.Inverse(mountRotation) * plant.Body.angularVelocity;
        Vector3 accelerationRos = SimulationConfig.UnityPolarToRos(specificLocal) + accelerometerBias + RandomVector(accelNoise * scale);
        Vector3 angularRos = SimulationConfig.UnityAxialToRos(angularLocal) + gyroBias + RandomVector(gyroNoise * scale);
        Quaternion relativeSensor = Quaternion.Inverse(sceneOriginRotation) * mountRotation;
        var message = new ImuMsg();
        message.header = Header(simulationTime, sensor.frame_id);
        message.orientation = relativeSensor.To<FLU>();
        message.angular_velocity = new RosMessageTypes.Geometry.Vector3Msg(angularRos.x, angularRos.y, angularRos.z);
        message.linear_acceleration = new RosMessageTypes.Geometry.Vector3Msg(accelerationRos.x, accelerationRos.y, accelerationRos.z);
        message.orientation_covariance = Diagonal(0.0025 * scale * scale);
        message.angular_velocity_covariance = Diagonal(gyroNoise * gyroNoise * scale * scale);
        message.linear_acceleration_covariance = Diagonal(accelNoise * accelNoise * scale * scale);
        Enqueue(imuQueue, message, sensor);
    }

    void MaybeQueuePressure(SensorConfig sensor)
    {
        Vector3 mount = transform.TransformPoint(SimulationConfig.RosPolarToUnity(sensor.position_m));
        float scale = NoiseScale();
        float depth = Mathf.Max(0f, config.water.surface_unity_y_m - mount.y);
        pressureBias += (float)noise.Gaussian(sensor.bias_walk_stddev[0] / Mathf.Sqrt(sensor.rate_hz));
        double pressure = config.water.atmospheric_pressure_pa +
            config.water.density_kg_m3 * config.water.gravity_m_s2 * depth + pressureBias +
            noise.Gaussian(sensor.noise_stddev[0] * scale);
        var message = new FluidPressureMsg();
        message.header = Header(simulationTime, sensor.frame_id);
        message.fluid_pressure = Math.Round(pressure / 5.0) * 5.0;
        message.variance = sensor.noise_stddev[0] * sensor.noise_stddev[0] * scale * scale;
        Enqueue(pressureQueue, message, sensor);
    }

    void MaybeQueueVio(SensorConfig sensor)
    {
        float scale = NoiseScale();
        double scenarioElapsed = simulationTime - scenarioStartTime;
        if (scenarioId == "vio_reset" && scenarioElapsed >= 10.0 && scenarioElapsed < 12.0)
            return;
        if (noise.Uniform() < sensor.dropout_probability * scale)
            return;
        vioPositionDrift += RandomVector(sensor.bias_walk_stddev[0] / Mathf.Sqrt(sensor.rate_hz));
        vioYawDrift += (float)noise.Gaussian(sensor.bias_walk_stddev[1] / Mathf.Sqrt(sensor.rate_hz));
        OdometryMsg message = BuildOdometry(simulationTime, true);
        Vector3 error = vioPositionDrift + RandomVector(sensor.noise_stddev[0] * scale);
        message.pose.pose.position.x += error.x;
        message.pose.pose.position.y += error.y;
        message.pose.pose.position.z += error.z;
        Quaternion noisy = transform.rotation * Quaternion.Euler(0f, -vioYawDrift * Mathf.Rad2Deg, 0f);
        message.pose.pose.orientation = (Quaternion.Inverse(sceneOriginRotation) * noisy).To<FLU>();
        message.pose.covariance = PoseCovariance(
            sensor.noise_stddev[0] * sensor.noise_stddev[0] * scale * scale,
            sensor.noise_stddev[1] * sensor.noise_stddev[1] * scale * scale);
        Enqueue(vioQueue, message, sensor);
    }

    OdometryMsg BuildOdometry(double sampleTime, bool visual)
    {
        Vector3 localPosition = Quaternion.Inverse(sceneOriginRotation) * (transform.position - sceneOriginPosition);
        Vector3 positionRos = SimulationConfig.UnityPolarToRos(localPosition);
        Quaternion relativeRotation = Quaternion.Inverse(sceneOriginRotation) * transform.rotation;
        Vector3 linearRos = SimulationConfig.UnityPolarToRos(transform.InverseTransformDirection(plant.Body.linearVelocity));
        Vector3 angularRos = SimulationConfig.UnityAxialToRos(transform.InverseTransformDirection(plant.Body.angularVelocity));
        var message = new OdometryMsg();
        message.header = Header(sampleTime, visual ? "odom" : "map");
        message.child_frame_id = "base_link";
        message.pose.pose.position = new RosMessageTypes.Geometry.PointMsg(positionRos.x, positionRos.y, positionRos.z);
        message.pose.pose.orientation = relativeRotation.To<FLU>();
        message.twist.twist.linear = new RosMessageTypes.Geometry.Vector3Msg(linearRos.x, linearRos.y, linearRos.z);
        message.twist.twist.angular = new RosMessageTypes.Geometry.Vector3Msg(angularRos.x, angularRos.y, angularRos.z);
        return message;
    }

    void PublishStatus()
    {
        var message = new RobotStatusMsg();
        message.stamp = ToRosTime(simulationTime);
        message.control_connected = true;
        message.armed = plant.Armed;
        message.external_control_enabled = plant.ExternalControlEnabled;
        message.arming_state = (byte)(plant.Armed ? 1 : 0);
        message.detail = $"unity_sil; scenario={scenarioId}; seed={seed}; config={config.generated.sha256}";
        ros.Publish(statusTopic, message);
    }

    float NoiseScale() => scenarioId == "sensor_stress" ? 2.5f : scenarioId == "clean" ? 0f : 1f;

    void Enqueue<T>(Queue<Pending<T>> queue, T message, SensorConfig sensor) where T : Message
    {
        float latencyScale = scenarioId == "delayed_messages" ? 3f :
            scenarioId == "sensor_stress" ? 2f : scenarioId == "clean" ? 0f : 1f;
        float dropoutScale = scenarioId == "sensor_dropout" ? 12f :
            scenarioId == "sensor_stress" ? 5f : scenarioId == "clean" ? 0f : 1f;
        if (noise.Uniform() < sensor.dropout_probability * dropoutScale)
            return;
        double latency = Math.Max(0.0,
            (sensor.latency_s + noise.Gaussian(sensor.jitter_s)) * latencyScale);
        queue.Enqueue(new Pending<T> { deliveryTime = simulationTime + latency, message = message });
    }

    void Flush<T>(Queue<Pending<T>> queue, string topic) where T : Message
    {
        while (queue.Count > 0 && queue.Peek().deliveryTime <= simulationTime)
            ros.Publish(topic, queue.Dequeue().message);
    }

    Vector3 RandomVector(float deviation) => new Vector3(
        (float)noise.Gaussian(deviation), (float)noise.Gaussian(deviation), (float)noise.Gaussian(deviation));

    static HeaderMsg Header(double time, string frame) => new HeaderMsg(ToRosTime(time), frame);

    static TimeMsg ToRosTime(double seconds)
    {
        int whole = (int)Math.Floor(seconds);
        uint nanos = (uint)Math.Round((seconds - whole) * 1e9);
        if (nanos >= 1000000000) { ++whole; nanos -= 1000000000; }
        return new TimeMsg(whole, nanos);
    }

    static double[] Diagonal(double variance) => new[] {
        variance, 0.0, 0.0, 0.0, variance, 0.0, 0.0, 0.0, variance,
    };

    static double[] PoseCovariance(double position, double angle)
    {
        var covariance = new double[36];
        covariance[0] = covariance[7] = covariance[14] = position;
        covariance[21] = covariance[28] = covariance[35] = angle;
        return covariance;
    }

    static Quaternion RosQuaternionToUnity(RosMessageTypes.Geometry.QuaternionMsg message)
    {
        float norm = Mathf.Sqrt((float)(message.x * message.x + message.y * message.y + message.z * message.z + message.w * message.w));
        if (norm < 1e-6f) return Quaternion.identity;
        var value = new Unity.Robotics.ROSTCPConnector.ROSGeometry.Quaternion<FLU>(
            (float)message.x / norm, (float)message.y / norm,
            (float)message.z / norm, (float)message.w / norm);
        return (Quaternion)value;
    }

    static Quaternion RosRpyToUnity(float[] rpy)
    {
        float cr = Mathf.Cos(rpy[0] * .5f), sr = Mathf.Sin(rpy[0] * .5f);
        float cp = Mathf.Cos(rpy[1] * .5f), sp = Mathf.Sin(rpy[1] * .5f);
        float cy = Mathf.Cos(rpy[2] * .5f), sy = Mathf.Sin(rpy[2] * .5f);
        return RosQuaternionToUnity(new RosMessageTypes.Geometry.QuaternionMsg(
            sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy));
    }
}

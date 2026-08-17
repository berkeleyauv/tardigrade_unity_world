using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;

// NOTE: this namespace is generated from tardigrade_interfaces via
// Robotics > Generate ROS Messages. Unity derives it from the package name:
// "tardigrade_interfaces" -> RosMessageTypes.TardigradeInterfaces.
// If your generated folder uses a different name, fix this using directive and
// the type names (RobotStatusMsg, SetArmed*, SetExternalControl*) to match.
using RosMessageTypes.TardigradeInterfaces;

/// <summary>
/// Unity-side backend for Tardigrade. This is the C# port of
/// tardigrade_sim/fake_unity_backend.py: it subscribes to /tardigrade/cmd_vel,
/// integrates a kinematic pose, moves this GameObject, and publishes
/// /tardigrade/state/odometry and /tardigrade/status. It also answers the
/// /tardigrade/set_armed and /tardigrade/set_external_control services so the
/// existing missions (gate_mission, path_follower) drive it unchanged.
///
/// Physics is intentionally kinematic for now: cmd_vel is treated as body-frame
/// normalized velocity in [-1, 1] (max ~1 m/s and ~1 rad/s), exactly like the
/// fake backend. Swap in Rigidbody forces / buoyancy / drag later without
/// touching the ROS contract.
/// </summary>
public class RosRobotBridge : MonoBehaviour
{
    [Header("Topics")]
    public string cmdVelTopic = "/tardigrade/cmd_vel";
    public string odometryTopic = "/tardigrade/state/odometry";
    public string statusTopic = "/tardigrade/status";

    [Header("Frames")]
    public string odomFrameId = "odom";
    public string baseFrameId = "base_link";

    [Header("Kinematics (match fake_unity_backend)")]
    [Tooltip("Metres per second at full linear command (cmd = 1.0).")]
    public float linearSpeed = 1.0f;
    [Tooltip("Radians per second at full yaw command (cmd = 1.0).")]
    public float yawSpeed = 1.0f;
    [Tooltip("Ignore cmd_vel older than this many seconds.")]
    public float cmdTimeoutSec = 0.5f;

    [Header("Publish rate")]
    public float publishRateHz = 30.0f;

    ROSConnection ros;

    // Internal pose in the ROS odom frame: x forward, y left, z up, yaw about z.
    float x, y, z, yaw;

    bool armed = false;
    bool externalControlEnabled = false;

    TwistMsg latestCmd = new TwistMsg();
    float lastCmdTime = -1000f;
    float lastPublishTime = 0f;

    // Unity pose captured at startup so the robot integrates from wherever you
    // placed it in the scene, and we can map ROS pose -> Unity transform.
    Vector3 startUnityPosition;
    Quaternion startUnityRotation;

    void Start()
    {
        startUnityPosition = transform.position;
        startUnityRotation = transform.rotation;

        ros = ROSConnection.GetOrCreateInstance();

        ros.Subscribe<TwistMsg>(cmdVelTopic, OnCmdVel);
        ros.RegisterPublisher<OdometryMsg>(odometryTopic);
        ros.RegisterPublisher<RobotStatusMsg>(statusTopic);

        ros.ImplementService<SetArmedRequest, SetArmedResponse>(
            "/tardigrade/set_armed", HandleSetArmed);
        ros.ImplementService<SetExternalControlRequest, SetExternalControlResponse>(
            "/tardigrade/set_external_control", HandleSetExternalControl);

        Debug.Log($"[RosRobotBridge] up. sub {cmdVelTopic}; " +
                  $"pub {odometryTopic}, {statusTopic}");
    }

    void OnCmdVel(TwistMsg msg)
    {
        latestCmd = msg;
        lastCmdTime = Time.time;
    }

    SetArmedResponse HandleSetArmed(SetArmedRequest req)
    {
        armed = req.armed;
        return new SetArmedResponse
        {
            success = true,
            message = armed ? "Unity backend armed" : "Unity backend disarmed",
        };
    }

    SetExternalControlResponse HandleSetExternalControl(
        SetExternalControlRequest req)
    {
        externalControlEnabled = req.enabled;
        return new SetExternalControlResponse
        {
            success = true,
            message = externalControlEnabled
                ? "Unity backend external control enabled"
                : "Unity backend external control disabled",
        };
    }

    void FixedUpdate()
    {
        IntegrateMotion(Time.fixedDeltaTime);
        ApplyPoseToTransform();

        if (Time.time - lastPublishTime >= 1.0f / Mathf.Max(publishRateHz, 1f))
        {
            lastPublishTime = Time.time;
            PublishOdometry();
            PublishStatus();
        }
    }

    void IntegrateMotion(float dt)
    {
        bool stale = (Time.time - lastCmdTime) > cmdTimeoutSec;
        if (!armed || !externalControlEnabled || stale)
            return;

        float forward = Mathf.Clamp((float)latestCmd.linear.x, -1f, 1f) * linearSpeed;
        float left = Mathf.Clamp((float)latestCmd.linear.y, -1f, 1f) * linearSpeed;
        float up = Mathf.Clamp((float)latestCmd.linear.z, -1f, 1f) * linearSpeed;
        float yawRate = Mathf.Clamp((float)latestCmd.angular.z, -1f, 1f) * yawSpeed;

        float cos = Mathf.Cos(yaw);
        float sin = Mathf.Sin(yaw);

        x += (forward * cos - left * sin) * dt;
        y += (forward * sin + left * cos) * dt;
        z += up * dt;
        yaw = Mathf.Atan2(Mathf.Sin(yaw + yawRate * dt),
                          Mathf.Cos(yaw + yawRate * dt));
    }

    // Map the ROS odom pose (x fwd, y left, z up; right-handed) onto the Unity
    // transform (x right, y up, z forward; left-handed), relative to the pose
    // the robot started at. This is the FLU->Unity convention the
    // ROS-TCP-Connector's ROSGeometry uses, written out explicitly.
    void ApplyPoseToTransform()
    {
        Vector3 rosOffsetInUnity = new Vector3(-y, z, x);
        transform.position = startUnityPosition + startUnityRotation * rosOffsetInUnity;
        // Yaw about ROS +z (up) becomes yaw about Unity +y, opposite sign.
        transform.rotation = startUnityRotation *
                             Quaternion.Euler(0f, -yaw * Mathf.Rad2Deg, 0f);
    }

    void PublishOdometry()
    {
        var msg = new OdometryMsg();
        msg.header.frame_id = odomFrameId;
        msg.child_frame_id = baseFrameId;
        msg.pose.pose.position.x = x;
        msg.pose.pose.position.y = y;
        msg.pose.pose.position.z = z;
        // Quaternion for a pure yaw about z, matching fake_unity_backend.
        msg.pose.pose.orientation.z = Mathf.Sin(yaw * 0.5f);
        msg.pose.pose.orientation.w = Mathf.Cos(yaw * 0.5f);
        ros.Publish(odometryTopic, msg);
    }

    void PublishStatus()
    {
        var msg = new RobotStatusMsg();
        msg.control_connected = true;
        msg.armed = armed;
        msg.external_control_enabled = externalControlEnabled;
        msg.nav_state = 0;
        msg.arming_state = (byte)(armed ? 1 : 0);
        msg.detail = $"unity_bridge; x={x:F2}; y={y:F2}; yaw={yaw:F2}";
        ros.Publish(statusTopic, msg);
    }
}

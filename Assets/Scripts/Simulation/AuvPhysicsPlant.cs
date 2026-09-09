using System;
using System.Collections.Generic;
using UnityEngine;
using RosMessageTypes.TardigradeInterfaces;

namespace Tardigrade.Simulation
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class AuvPhysicsPlant : MonoBehaviour
    {
        public SimulationConfig Config { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool Armed { get; set; }
        public bool ExternalControlEnabled { get; set; }
        public double SimulationTime { get; set; }
        public float CommandTimeoutSeconds { get; set; } = 0.5f;
        public float VoltageScale { get; set; } = 1f;

        float[] requested;
        float[] delivered;
        float[] failureScale;
        double lastCommandTime = double.NegativeInfinity;
        readonly Queue<PendingCommand> commandQueue = new Queue<PendingCommand>();

        sealed class PendingCommand
        {
            public double deliveryTime;
            public float[] values;
        }

        public void Initialize(SimulationConfig config)
        {
            Config = config;
            Body = GetComponent<Rigidbody>();
            RigidBodyConfig body = config.rigid_body;
            float addedMass = (body.added_mass_kg[0] + body.added_mass_kg[1] + body.added_mass_kg[2]) / 3f;
            Body.mass = body.mass_kg + addedMass;
            Body.useGravity = false;
            Body.linearDamping = 0f;
            Body.angularDamping = 0f;
            Body.centerOfMass = SimulationConfig.RosPolarToUnity(body.center_of_mass_m);
            Body.inertiaTensor = new Vector3(
                body.inertia_kg_m2[1] + body.added_inertia_kg_m2[1],
                body.inertia_kg_m2[2] + body.added_inertia_kg_m2[2],
                body.inertia_kg_m2[0] + body.added_inertia_kg_m2[0]);
            requested = new float[config.thrusters.Length];
            delivered = new float[config.thrusters.Length];
            failureScale = new float[config.thrusters.Length];
            for (int index = 0; index < failureScale.Length; ++index)
                failureScale[index] = 1f;
        }

        public bool AcceptCommands(ThrusterCommandsMsg message, out string reason)
        {
            reason = "";
            if (message.names.Length != Config.thrusters.Length || message.setpoints.Length != message.names.Length)
            {
                reason = "names/setpoints length mismatch";
                return false;
            }
            float[] ordered = new float[Config.thrusters.Length];
            var seen = new HashSet<string>();
            for (int index = 0; index < message.names.Length; ++index)
            {
                string name = message.names[index];
                float value = message.setpoints[index];
                if (!seen.Add(name) || float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 1f)
                {
                    reason = "duplicate, non-finite, or out-of-range entry";
                    return false;
                }
                int canonical = Array.FindIndex(Config.thrusters, item => item.name == name);
                if (canonical < 0)
                {
                    reason = "unknown thruster " + name;
                    return false;
                }
                ordered[canonical] = message.setpoints[index];
            }
            double delay = 0.0;
            foreach (ThrusterConfig thruster in Config.thrusters)
                delay = Math.Max(delay, thruster.command_delay_s);
            commandQueue.Enqueue(new PendingCommand {
                deliveryTime = SimulationTime + delay,
                values = ordered,
            });
            lastCommandTime = SimulationTime;
            return true;
        }

        public void SetThrusterScale(string name, float scale)
        {
            int index = Array.FindIndex(Config.thrusters, item => item.name == name);
            if (index < 0)
                throw new ArgumentException("Unknown thruster", nameof(name));
            failureScale[index] = Mathf.Clamp01(scale);
        }

        public void ResetPlant(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Array.Clear(requested, 0, requested.Length);
            Array.Clear(delivered, 0, delivered.Length);
            for (int index = 0; index < failureScale.Length; ++index)
                failureScale[index] = 1f;
            commandQueue.Clear();
            lastCommandTime = double.NegativeInfinity;
            Armed = false;
            ExternalControlEnabled = false;
        }

        public void Neutralize()
        {
            Array.Clear(requested, 0, requested.Length);
            Array.Clear(delivered, 0, delivered.Length);
            commandQueue.Clear();
            lastCommandTime = double.NegativeInfinity;
        }

        void FixedUpdate()
        {
            if (Config == null)
                return;
            ApplyWeightAndBuoyancy();
            ApplyHydrodynamics();
            ApplyThrusters(Time.fixedDeltaTime);
        }

        void ApplyWeightAndBuoyancy()
        {
            Body.AddForce(Vector3.down * Config.rigid_body.mass_kg * Config.water.gravity_m_s2, ForceMode.Force);
            foreach (BuoyancyElementConfig element in Config.buoyancy_elements)
            {
                Vector3 point = transform.TransformPoint(SimulationConfig.RosPolarToUnity(element.position_m));
                float bottom = point.y - element.height_m * 0.5f;
                float fraction = Mathf.Clamp01((Config.water.surface_unity_y_m - bottom) / element.height_m);
                float force = Config.water.density_kg_m3 * Config.water.gravity_m_s2 * element.volume_m3 * fraction;
                Body.AddForceAtPosition(Vector3.up * force, point, ForceMode.Force);
            }
        }

        void ApplyHydrodynamics()
        {
            Vector3 currentLocal = SimulationConfig.RosPolarToUnity(Config.water.current_body_initial_m_s);
            Vector3 velocityLocal = transform.InverseTransformDirection(Body.linearVelocity) - currentLocal;
            Vector3 velocityRos = SimulationConfig.UnityPolarToRos(velocityLocal);
            Vector3 angularLocal = transform.InverseTransformDirection(Body.angularVelocity);
            Vector3 angularRos = SimulationConfig.UnityAxialToRos(angularLocal);
            float[] linear = Config.hydrodynamics.linear_damping;
            float[] quadratic = Config.hydrodynamics.quadratic_damping;
            Vector3 forceRos = Damping(velocityRos, linear, quadratic, 0);
            Vector3 torqueRos = Damping(angularRos, linear, quadratic, 3);
            Body.AddRelativeForce(SimulationConfig.RosPolarToUnity(new[] { forceRos.x, forceRos.y, forceRos.z }));
            Body.AddRelativeTorque(SimulationConfig.RosAxialToUnity(torqueRos));
        }

        static Vector3 Damping(Vector3 velocity, float[] linear, float[] quadratic, int offset)
        {
            return new Vector3(
                -linear[offset] * velocity.x - quadratic[offset] * velocity.x * Mathf.Abs(velocity.x),
                -linear[offset + 1] * velocity.y - quadratic[offset + 1] * velocity.y * Mathf.Abs(velocity.y),
                -linear[offset + 2] * velocity.z - quadratic[offset + 2] * velocity.z * Mathf.Abs(velocity.z));
        }

        void ApplyThrusters(float dt)
        {
            while (commandQueue.Count > 0 && commandQueue.Peek().deliveryTime <= SimulationTime)
                requested = commandQueue.Dequeue().values;
            bool neutral = !Armed || !ExternalControlEnabled || SimulationTime - lastCommandTime > CommandTimeoutSeconds;
            for (int index = 0; index < Config.thrusters.Length; ++index)
            {
                ThrusterConfig thruster = Config.thrusters[index];
                float target = neutral ? 0f : requested[index];
                if (neutral)
                    delivered[index] = 0f;
                else
                {
                    float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(thruster.time_constant_s, 1e-4f));
                    delivered[index] = Mathf.Lerp(delivered[index], target, alpha);
                }
                float force = CommandToForce(delivered[index], thruster) *
                    failureScale[index] * Mathf.Clamp01(VoltageScale);
                Vector3 localAxis = SimulationConfig.RosPolarToUnity(thruster.axis);
                Vector3 worldForce = transform.TransformDirection(localAxis) * force;
                Vector3 position = transform.TransformPoint(SimulationConfig.RosPolarToUnity(thruster.position_m));
                Body.AddForceAtPosition(worldForce, position, ForceMode.Force);
            }
        }

        public static float CommandToForce(float command, ThrusterConfig thruster)
        {
            float magnitude = Mathf.Abs(Mathf.Clamp(command, -1f, 1f));
            if (magnitude <= thruster.deadband)
                return 0f;
            float effective = (magnitude - thruster.deadband) / (1f - thruster.deadband);
            float limit = command >= 0f ? thruster.max_forward_n : thruster.max_reverse_n;
            return Mathf.Sign(command) * limit * effective * effective;
        }
    }
}

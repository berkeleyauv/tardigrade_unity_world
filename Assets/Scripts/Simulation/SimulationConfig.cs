using System;
using System.IO;
using UnityEngine;

namespace Tardigrade.Simulation
{
    [Serializable]
    public sealed class GeneratedMetadata
    {
        public string source;
        public string sha256;
    }

    [Serializable]
    public sealed class RigidBodyConfig
    {
        public float mass_kg;
        public float[] center_of_mass_m;
        public float[] center_of_buoyancy_m;
        public float[] inertia_kg_m2;
        public float[] added_mass_kg;
        public float[] added_inertia_kg_m2;
        public float displaced_volume_m3;
        public float[] collision_size_m;
    }

    [Serializable]
    public sealed class WaterConfig
    {
        public float density_kg_m3;
        public float gravity_m_s2;
        public float atmospheric_pressure_pa;
        public float surface_enu_z_m;
        public float surface_unity_y_m;
        public float[] current_body_initial_m_s;
    }

    [Serializable]
    public sealed class HydrodynamicsConfig
    {
        public float[] linear_damping;
        public float[] quadratic_damping;
    }

    [Serializable]
    public sealed class BuoyancyElementConfig
    {
        public float[] position_m;
        public float volume_m3;
        public float height_m;
    }

    [Serializable]
    public sealed class ThrusterConfig
    {
        public string name;
        public int slot;
        public float[] position_m;
        public float[] axis;
        public float max_forward_n;
        public float max_reverse_n;
        public float deadband;
        public float time_constant_s;
        public float command_delay_s;
    }

    [Serializable]
    public sealed class SensorConfig
    {
        public string name;
        public string frame_id;
        public float[] position_m;
        public float[] rpy_rad;
        public float rate_hz;
        public float latency_s;
        public float jitter_s;
        public float dropout_probability;
        public float[] noise_stddev;
        public float[] bias_walk_stddev;
        public float baseline_m;
        public int[] resolution;
        public float horizontal_fov_deg;
    }

    [Serializable]
    public sealed class SimulationConfig
    {
        public int schema_version;
        public string vehicle_id;
        public GeneratedMetadata generated;
        public RigidBodyConfig rigid_body;
        public WaterConfig water;
        public HydrodynamicsConfig hydrodynamics;
        public BuoyancyElementConfig[] buoyancy_elements;
        public ThrusterConfig[] thrusters;
        public SensorConfig[] sensors;

        public static SimulationConfig Load(string fileName = "tardigrade_vehicle.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Generated vehicle configuration is missing", path);
            SimulationConfig config = JsonUtility.FromJson<SimulationConfig>(File.ReadAllText(path));
            config.Validate();
            return config;
        }

        public SensorConfig Sensor(string name)
        {
            foreach (SensorConfig sensor in sensors)
                if (sensor.name == name)
                    return sensor;
            throw new InvalidDataException("Missing sensor: " + name);
        }

        public void Validate()
        {
            if (schema_version != 1 || rigid_body == null || water == null || hydrodynamics == null)
                throw new InvalidDataException("Unsupported or incomplete vehicle configuration");
            if (rigid_body.mass_kg <= 0f || rigid_body.displaced_volume_m3 <= 0f)
                throw new InvalidDataException("Mass and displacement must be positive");
            if (thrusters == null || thrusters.Length != 8)
                throw new InvalidDataException("Tardigrade requires eight configured thrusters");
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (ThrusterConfig thruster in thrusters)
            {
                if (!names.Add(thruster.name) || thruster.axis == null || thruster.axis.Length != 3)
                    throw new InvalidDataException("Thruster names and axes must be valid and unique");
                Vector3 axis = RosPolarToUnity(thruster.axis);
                if (Mathf.Abs(axis.magnitude - 1f) > 1e-4f)
                    throw new InvalidDataException(thruster.name + " axis is not normalized");
            }
        }

        // Unity RUF (x right, y up, z forward) <-> REP-103 FLU.
        public static Vector3 RosPolarToUnity(float[] flu) =>
            new Vector3(-flu[1], flu[2], flu[0]);

        public static Vector3 UnityPolarToRos(Vector3 ruf) =>
            new Vector3(ruf.z, -ruf.x, ruf.y);

        public static Vector3 RosAxialToUnity(Vector3 flu) =>
            new Vector3(flu.y, -flu.z, -flu.x);

        public static Vector3 UnityAxialToRos(Vector3 ruf) =>
            new Vector3(-ruf.z, ruf.x, -ruf.y);
    }
}

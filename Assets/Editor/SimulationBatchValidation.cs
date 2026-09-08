using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Tardigrade.Simulation;

public static class SimulationBatchValidation
{
    [MenuItem("Tardigrade/Validate Simulation Configuration")]
    public static void Run()
    {
        try
        {
            SimulationConfig config = SimulationConfig.Load();
            Assert(config.generated.sha256.Length == 64, "source hash is missing");
            Vector3[] vectors = {
                Vector3.right, Vector3.up, Vector3.forward,
                new Vector3(1.2f, -3.4f, 5.6f),
            };
            foreach (Vector3 unity in vectors)
            {
                Vector3 ros = SimulationConfig.UnityPolarToRos(unity);
                Vector3 roundTrip = SimulationConfig.RosPolarToUnity(
                    new[] { ros.x, ros.y, ros.z });
                Assert((roundTrip - unity).sqrMagnitude < 1e-10f,
                    "polar-vector coordinate round trip failed");
            }
            Vector3 yawUnity = SimulationConfig.RosAxialToUnity(new Vector3(0f, 0f, 1f));
            Assert(yawUnity == Vector3.down, "ROS positive yaw must map to Unity negative y");
            foreach (ThrusterConfig thruster in config.thrusters)
            {
                Assert(Mathf.Approximately(AuvPhysicsPlant.CommandToForce(0f, thruster), 0f),
                    thruster.name + " neutral is not zero");
                Assert(Mathf.Approximately(AuvPhysicsPlant.CommandToForce(1f, thruster), thruster.max_forward_n),
                    thruster.name + " forward curve endpoint failed");
                Assert(Mathf.Approximately(AuvPhysicsPlant.CommandToForce(-1f, thruster), -thruster.max_reverse_n),
                    thruster.name + " reverse curve endpoint failed");
            }
            Debug.Log("Tardigrade simulation validation passed: " + config.generated.sha256);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}

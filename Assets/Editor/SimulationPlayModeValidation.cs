using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Tardigrade.Simulation;

[InitializeOnLoad]
public static class SimulationPlayModeValidation
{
    const string RunningKey = "TardigradeSimulationPlayValidation";
    static int frames;
    static float initialY;

    static SimulationPlayModeValidation()
    {
        if (SessionState.GetBool(RunningKey, false))
            Register();
    }

    public static void Run()
    {
        SessionState.SetBool(RunningKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Register();
        EditorApplication.EnterPlaymode();
    }

    static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            frames = 0;
            RosRobotBridge bridge = UnityEngine.Object.FindFirstObjectByType<RosRobotBridge>();
            initialY = bridge == null ? 0f : bridge.transform.position.y;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode &&
                 SessionState.GetBool(RunningKey, false))
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Debug.Log("Tardigrade PlayMode validation passed");
            EditorApplication.Exit(0);
        }
    }

    static void Tick()
    {
        if (++frames < 120)
            return;
        EditorApplication.update -= Tick;
        try
        {
            RosRobotBridge bridge = UnityEngine.Object.FindFirstObjectByType<RosRobotBridge>();
            Assert(bridge != null, "RosRobotBridge missing in acceptance scene");
            AuvPhysicsPlant plant = bridge.GetComponent<AuvPhysicsPlant>();
            Assert(plant != null && plant.Body != null, "physical plant was not composed");
            Assert(Mathf.Abs(Time.fixedDeltaTime - 0.01f) < 1e-6f,
                "physics rate is not 100 Hz");
            Assert(!plant.Armed && !plant.ExternalControlEnabled,
                "simulator auto-armed or enabled external control");
            Assert(GameObject.Find("GateAcceptance") != null,
                "gate acceptance geometry was not created");
            Assert(GameObject.Find("AcceptancePoolCollisions") != null,
                "pool collision geometry was not created");
            Assert(GameObject.Find("TardigradeWaterSurface") != null,
                "visual water surface was not created");
            PoolScenario pool = UnityEngine.Object.FindFirstObjectByType<PoolScenario>();
            Assert(pool != null && pool.transform.Find("pool_visuals/tiled_floor") != null,
                "dimensioned pool visuals were not created");
            Vector3 vehicleInPool = pool.transform.InverseTransformPoint(bridge.transform.position);
            Assert(Mathf.Abs(vehicleInPool.x) <= pool.insideSizeMeters.x * .5f &&
                   Mathf.Abs(vehicleInPool.z) <= pool.insideSizeMeters.z * .5f,
                "vehicle starts outside the generated pool shell");
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsSortMode.None))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert(material == null || (material.shader != null && material.shader.isSupported),
                        renderer.name + " uses a missing or unsupported shader (magenta fallback)");
                }
            }
            Assert(bridge.transform.position.y > initialY,
                "slightly positive buoyancy did not raise the disarmed vehicle");
            Vector3 resetPosition = bridge.transform.position;
            plant.ResetPlant(resetPosition, bridge.transform.rotation);
            Assert(plant.Body.linearVelocity.sqrMagnitude < 1e-8f &&
                   plant.Body.angularVelocity.sqrMagnitude < 1e-8f,
                "reset did not clear rigid-body history");
            EditorApplication.ExitPlaymode();
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.Exit(1);
        }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Headless helper: lists every GameObject in the open scene, then attaches
/// RosRobotBridge to the first object whose name contains "tardigrade"
/// (case-insensitive) and saves the scene. Run via:
/// Unity.exe -batchmode -nographics -quit -projectPath &lt;proj&gt;
///   -executeMethod CliAttachBridge.ListSceneObjects
///   (or) -executeMethod CliAttachBridge.AttachToRobot
/// </summary>
public static class CliAttachBridge
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    public static void ListSceneObjects()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            LogHierarchy(root, 0);
        }
    }

    static void LogHierarchy(GameObject go, int depth)
    {
        Debug.Log(new string(' ', depth * 2) + "OBJ: '" + go.name + "'");
        foreach (Transform child in go.transform)
        {
            LogHierarchy(child.gameObject, depth + 1);
        }
    }

    public static void AttachToRobot()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject target = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            target = FindByNameContains(root, "tardigrade");
            if (target != null) break;
        }

        if (target == null)
        {
            Debug.LogError("CliAttachBridge: no GameObject with 'tardigrade' in its name was found.");
            return;
        }

        var existing = target.GetComponent<RosRobotBridge>();
        if (existing == null)
        {
            target.AddComponent<RosRobotBridge>();
            Debug.Log("CliAttachBridge: attached RosRobotBridge to '" + target.name + "'.");
        }
        else
        {
            Debug.Log("CliAttachBridge: '" + target.name + "' already has RosRobotBridge.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("CliAttachBridge: scene saved.");
    }

    static GameObject FindByNameContains(GameObject go, string needle)
    {
        if (go.name.ToLowerInvariant().Contains(needle))
            return go;
        foreach (Transform child in go.transform)
        {
            var found = FindByNameContains(child.gameObject, needle);
            if (found != null) return found;
        }
        return null;
    }
}

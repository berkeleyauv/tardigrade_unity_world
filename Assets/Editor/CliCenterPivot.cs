using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Fixes off-center rotation: the imported `tardigrade` FBX pivot sits wherever
/// the CAD export origin was, not at the robot's geometric center, so rotating
/// it in place swings the mesh in an arc. This wraps the robot in a parent
/// GameObject placed at the combined renderer-bounds center and moves
/// RosRobotBridge onto that parent, so yaw rotates about the visual center.
///
/// Run with the Unity Editor CLOSED:
/// Unity.exe -batchmode -nographics -quit -projectPath &lt;proj&gt;
///   -executeMethod CliCenterPivot.CenterRobotPivot
/// </summary>
public static class CliCenterPivot
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string RobotName = "tardigrade";
    const string PivotName = "TardigradeRoot";

    public static void CenterRobotPivot()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject robot = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == RobotName) { robot = root; break; }
        }
        if (robot == null)
        {
            Debug.LogError($"CliCenterPivot: no root GameObject named '{RobotName}'.");
            return;
        }

        if (robot.transform.parent != null &&
            robot.transform.parent.name == PivotName)
        {
            Debug.Log("CliCenterPivot: already wrapped in a centered pivot; nothing to do.");
            return;
        }

        // World-space center of all the robot's renderers.
        var renderers = robot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("CliCenterPivot: robot has no renderers to measure.");
            return;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        Vector3 center = bounds.center;

        // Create the pivot at the geometric center, keep the robot's world pose.
        var pivot = new GameObject(PivotName);
        pivot.transform.position = center;
        pivot.transform.rotation = robot.transform.rotation;
        robot.transform.SetParent(pivot.transform, worldPositionStays: true);

        // Move RosRobotBridge from the robot to the pivot so cmd_vel drives the
        // centered object. (Re-adds a fresh component; default fields are fine.)
        var oldBridge = robot.GetComponent<RosRobotBridge>();
        if (oldBridge != null) Object.DestroyImmediate(oldBridge);
        pivot.AddComponent<RosRobotBridge>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"CliCenterPivot: wrapped '{RobotName}' under '{PivotName}' at " +
                  $"{center}, moved RosRobotBridge to it, saved scene.");
    }
}

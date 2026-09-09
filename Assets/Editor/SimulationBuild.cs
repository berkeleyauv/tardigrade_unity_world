using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class SimulationBuild
{
    public static void BuildStandalone()
    {
        string targetName = CommandLineValue("-simBuildTarget", "linux");
        BuildTarget target = targetName.Equals("windows", StringComparison.OrdinalIgnoreCase) ?
            BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;
        string output = target == BuildTarget.StandaloneWindows64 ?
            "Builds/Windows/TardigradeSim.exe" : "Builds/Linux/TardigradeSim.x86_64";
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
            .Select(scene => scene.path).ToArray();
        BuildReport report = BuildPipeline.BuildPlayer(scenes, output, target,
            BuildOptions.StrictMode | BuildOptions.CleanBuildCache);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Standalone build failed: " + report.summary.result);
    }

    static string CommandLineValue(string key, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}

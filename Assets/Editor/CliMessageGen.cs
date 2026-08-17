using UnityEditor;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

/// <summary>
/// Headless equivalent of Robotics > Generate ROS Messages, for CLI/CI use:
/// Unity.exe -batchmode -nographics -quit -projectPath &lt;proj&gt;
///   -executeMethod CliMessageGen.GenerateTardigradeInterfaces
/// Writes into Assets/RosMessages, matching the menu tool's default location.
/// </summary>
public static class CliMessageGen
{
    const string RosPackagePath =
        @"C:\Users\elila\Desktop\Robosub Stuff\tardigrade_ws\src\tardigrade_interfaces";

    public static void GenerateTardigradeInterfaces()
    {
        string outPath = System.IO.Path.Combine(
            UnityEngine.Application.dataPath, "RosMessages");

        MessageAutoGen.GenerateDirectoryMessages(RosPackagePath, outPath, verbose: true);
        ServiceAutoGen.GenerateDirectoryServices(RosPackagePath, outPath, verbose: true);

        AssetDatabase.Refresh();
    }
}

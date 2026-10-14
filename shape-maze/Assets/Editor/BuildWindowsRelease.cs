using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWindowsRelease
{
    private const string ReleaseVersion = "1.0";
    private const string ProductName = "Shape Maze";

    [MenuItem("Build/Windows x64 Release (1.0)")]
    public static void Build()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.Combine(projectRoot, "Builds", "Windows");
        Directory.CreateDirectory(outputDirectory);

        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = ReleaseVersion;

        string[] scenes = System.Array.FindAll(
            System.Array.ConvertAll(EditorBuildSettings.scenes, scene => scene.path),
            scenePath => !string.IsNullOrEmpty(scenePath));

        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes are enabled in File > Build Settings.");
            return;
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDirectory, "ShapeMaze-1.0.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == BuildResult.Succeeded)
            Debug.Log($"Windows x64 1.0 build complete: {options.locationPathName}");
        else
            Debug.LogError($"Windows x64 build failed: {report.summary.result}");
    }
}

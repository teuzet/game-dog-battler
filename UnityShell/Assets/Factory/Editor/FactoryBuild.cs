using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>The demo build the factory makes at the end of every sprint:
/// Unity.exe -batchmode -quit -executeMethod FactoryBuild.Demo -buildTarget Win64,
/// output folder in FACTORY_BUILD_OUT.</summary>
public static class FactoryBuild
{
    const string MainScene = "Assets/Scenes/Main.unity";

    public static void Demo()
    {
        try
        {
            string outDir = Environment.GetEnvironmentVariable("FACTORY_BUILD_OUT") ?? "";
            if (outDir.Length == 0) outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "demo"));
            Directory.CreateDirectory(outDir);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { EnsureMainScene() };
            string name = string.IsNullOrWhiteSpace(PlayerSettings.productName) ? "Game" : PlayerSettings.productName;
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, name + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Build Failed: {report.summary.result}, {report.summary.totalErrors} errors");
                EditorApplication.Exit(1);
                return;
            }
            CopyDir(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game.Core", "Data")),
                    Path.Combine(outDir, name + "_Data", "StreamingAssets", "Data"));
            Directory.CreateDirectory(Path.Combine(outDir, "nick"));
            Debug.Log($"FactoryBuild.Demo: built {options.locationPathName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Build Failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    static string EnsureMainScene()
    {
        if (!File.Exists(MainScene))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainScene)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, MainScene);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
        return MainScene;
    }

    static void CopyDir(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*.json", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(to, file.Substring(from.Length).TrimStart('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }
}

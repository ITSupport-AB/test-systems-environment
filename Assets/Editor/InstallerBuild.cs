using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class InstallerBuild
{
    public static void Build()
    {
        var targetName = Environment.GetEnvironmentVariable("BUILD_TARGET") ?? "StandaloneWindows64";
        var target = ParseTarget(targetName);
        var output = Environment.GetEnvironmentVariable("BUILD_OUTPUT") ?? Path.Combine("Builds", targetName, "Freebuff");
        var format = (Environment.GetEnvironmentVariable("BUILD_FORMAT") ?? "player").ToLowerInvariant();
        var scenes = GetEnabledScenes();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes are configured in Build Settings.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        if (target == BuildTarget.Android)
        {
            EditorUserBuildSettings.buildAppBundle = format == "aab";
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });

        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            UnityEngine.Debug.LogError($"[InstallerBuild] FAILED: {summary.result} ({summary.totalErrors} errors)");
            EditorApplication.Exit(1);
            return;
        }

        UnityEngine.Debug.Log($"[InstallerBuild] SUCCESS: {targetName} -> {summary.outputPath} ({summary.totalSize} bytes)");
        EditorApplication.Exit(0);
    }

    private static string[] GetEnabledScenes()
    {
        var enabledScenes = new System.Collections.Generic.List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                enabledScenes.Add(scene.path);
            }
        }

        return enabledScenes.ToArray();
    }

    private static BuildTarget ParseTarget(string value)
    {
        switch (value)
        {
            case "Android":
                return BuildTarget.Android;
            case "StandaloneLinux64":
                return BuildTarget.StandaloneLinux64;
            case "WebGL":
                return BuildTarget.WebGL;
            case "StandaloneWindows64":
                return BuildTarget.StandaloneWindows64;
            default:
                throw new ArgumentException($"Unsupported BUILD_TARGET: {value}");
        }
    }
}
using UnityEditor;
using UnityEngine;

// Tools > Build Windows: builds every scene in Build Settings to Builds/Deliverable3.exe and runs it.
// Windowed, so two copies (host and client) fit on one screen for testing and for the video.
public static class BuildWindows
{
    [MenuItem("Tools/Build Windows")]
    public static void Build()
    {
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 960;
        PlayerSettings.defaultScreenHeight = 540;
        PlayerSettings.resizableWindow = true;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes),
            locationPathName = "Builds/Deliverable3.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.AutoRunPlayer,
        };
        BuildPipeline.BuildPlayer(options);
    }
}

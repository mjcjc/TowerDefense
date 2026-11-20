using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DemoSetup
{
    static DemoSetup()
    {
        EditorApplication.delayCall += PrepareDemo;
    }

    private static void PrepareDemo()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists("Assets/Scenes/Title.unity"))
        {
            Directory.CreateDirectory("Assets/Scenes");
            CreateScene("Assets/Scenes/Battle.unity");
            CreateScene("Assets/Scenes/Title.unity");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Title.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Battle.unity", true)
            };
            PlayerSettings.productName = "타워디펜스";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }

        if (SessionState.GetBool("DemoShown", false))
            return;

        SessionState.SetBool("DemoShown", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Title.unity");
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }

    private static void CreateScene(string path)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("BattleSceneController").AddComponent<BattleSceneController>();
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.13f, 0.19f);
        camera.orthographic = true;
        camera.transform.position = new Vector3(7, 5, -10);
        EditorSceneManager.SaveScene(scene, path);
    }

    [MenuItem("Tower Defense/Build Windows Demo")]
    public static void BuildDemo()
    {
        Directory.CreateDirectory("Builds/Windows");
        BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/Title.unity", "Assets/Scenes/Battle.unity" },
            "Builds/Windows/TowerDefense.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
    }
}


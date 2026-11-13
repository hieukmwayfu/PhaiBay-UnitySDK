using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds the demo scene (also usable from batchmode: -executeMethod PhaiBayDemoSceneBuilder.Build).</summary>
public static class PhaiBayDemoSceneBuilder
{
    const string ScenePath = "Assets/PhaiBayDemo/PhaiBayDemo.unity";

    [MenuItem("PhaiBay/Rebuild Demo Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.1f, 0.13f);
        }
        new GameObject("PhaiBayDemo").AddComponent<PhaiBayDemo>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("PhaiBay demo scene saved to " + ScenePath);
    }
}

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LightShaft)), CanEditMultipleObjects]
public class LightShaftEditor : Editor
{
    const string MaterialPath = "Assets/LightShafts/LightShaft.mat";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        if (GUILayout.Button("Generar haz"))
            foreach (var t in targets) ((LightShaft)t).Generate();

        if (GUILayout.Button("Regenerar todos los haces de la escena"))
            RegenerateAll();
    }

    [MenuItem("GameObject/Light/Haz de luz (ventana)", false, 10)]
    static void Create(MenuCommand command)
    {
        var go = new GameObject("Haz de luz");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetOrCreateMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var shaft = go.AddComponent<LightShaft>();
        var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindFirstObjectByType<Light>();
        if (sun != null && sun.type == LightType.Directional) shaft.sun = sun;

        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(go, "Crear haz de luz");
        Selection.activeGameObject = go;
        shaft.Generate();
    }

    [MenuItem("Tools/Haces de luz/Regenerar todos")]
    static void RegenerateAll()
    {
        foreach (var s in Object.FindObjectsByType<LightShaft>(FindObjectsSortMode.None))
            s.Generate();
    }

    internal static Material GetOrCreateMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Custom/LightShaft")) { name = "LightShaft" };
        AssetDatabase.CreateAsset(mat, MaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}

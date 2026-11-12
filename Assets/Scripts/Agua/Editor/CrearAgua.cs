using UnityEditor;
using UnityEngine;

/// <summary>
/// Menús para montar el agua con sangre:
///  - GameObject > Jindama > Agua con sangre: crea un plano de agua listo (material, AguaSangre y AguaReflejo).
///  - GameObject > Jindama > Convertir en agua con sangre: lo mismo sobre el objeto seleccionado (tu propia malla).
/// </summary>
static class CrearAgua
{
    const string RutaMaterial = "Assets/Shaders/AguaSangre.mat";

    static Material Material()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
        if (mat != null) return mat;
        var shader = Shader.Find("Jindama/AguaSangre");
        if (shader == null) { Debug.LogError("No encuentro el shader Jindama/AguaSangre."); return null; }
        mat = new Material(shader) { name = "AguaSangre" };
        AssetDatabase.CreateAsset(mat, RutaMaterial);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void Preparar(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null) { Debug.LogWarning(go.name + " no tiene Renderer."); return; }
        Undo.RecordObject(r, "Agua con sangre");
        r.sharedMaterial = Material();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        int capa = LayerMask.NameToLayer("Water");
        if (capa >= 0) { Undo.RecordObject(go, "Agua con sangre"); go.layer = capa; }
        if (go.GetComponent<AguaSangre>() == null) Undo.AddComponent<AguaSangre>(go);
        if (go.GetComponent<AguaReflejo>() == null) Undo.AddComponent<AguaReflejo>(go);
    }

    [MenuItem("GameObject/Jindama/Agua con sangre", false, 10)]
    static void Nueva()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Agua";
        Object.DestroyImmediate(go.GetComponent<Collider>()); // que no se pueda andar por encima
        var vista = SceneView.lastActiveSceneView;
        if (vista != null) go.transform.position = vista.pivot;
        go.transform.localScale = new Vector3(3f, 1f, 3f);
        Undo.RegisterCreatedObjectUndo(go, "Agua con sangre");
        Preparar(go);
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/Jindama/Convertir en agua con sangre", false, 11)]
    static void Convertir()
    {
        foreach (var go in Selection.gameObjects) Preparar(go);
    }

    [MenuItem("GameObject/Jindama/Convertir en agua con sangre", true)]
    static bool PuedeConvertir() => Selection.activeGameObject != null;
}

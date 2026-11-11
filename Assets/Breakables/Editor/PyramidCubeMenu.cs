using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

static class PyramidCubeMenu
{
    [MenuItem("GameObject/3D Object/Cubo de pirámides (destructible)", false, 10)]
    static void Create(MenuCommand command)
    {
        var go = new GameObject("Cubo de pirámides");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        if (command.context == null && SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        go.AddComponent<PyramidCube>(); // añade MeshFilter, MeshRenderer y MeshCollider

        var pipeline = GraphicsSettings.currentRenderPipeline;
        var mat = pipeline != null ? pipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        Undo.RegisterCreatedObjectUndo(go, "Crear cubo de pirámides");
        Selection.activeGameObject = go;
    }
}

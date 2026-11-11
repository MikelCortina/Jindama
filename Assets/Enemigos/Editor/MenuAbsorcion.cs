using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Tools > Absorción > Crear punto de absorción: añade a la escena un PuntoAbsorcion para colocarlo a mano.</summary>
static class MenuAbsorcion
{
    [MenuItem("Tools/Absorción/Crear punto de absorción")]
    static void Crear()
    {
        var existente = Object.FindAnyObjectByType<PuntoAbsorcion>();
        if (existente != null)
        {
            Selection.activeGameObject = existente.gameObject;
            EditorGUIUtility.PingObject(existente.gameObject);
            Debug.Log("Ya hay un punto de absorción en la escena: muévelo a donde quieras.", existente);
            return;
        }
        var go = new GameObject("Punto de absorción");
        go.transform.position = Absorcion.AgujeroAutomatico(); // donde se calcula solo, para moverlo desde ahí
        go.AddComponent<PuntoAbsorcion>();
        Undo.RegisterCreatedObjectUndo(go, "Crear punto de absorción");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }
}

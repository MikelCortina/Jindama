using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

static class PlayerSetupMenu
{
    [MenuItem("GameObject/Jugador en primera persona", false, 12)]
    static void Create()
    {
        var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam == null) { EditorUtility.DisplayDialog("Jugador", "No hay ninguna cámara en la escena.", "Vale"); return; }
        if (cam.GetComponentInParent<FirstPersonController>() != null)
        {
            Selection.activeGameObject = cam.GetComponentInParent<FirstPersonController>().gameObject;
            EditorUtility.DisplayDialog("Jugador", "La cámara ya tiene un jugador.", "Vale");
            return;
        }

        Undo.SetCurrentGroupName("Crear jugador en primera persona");
        int undoGroup = Undo.GetCurrentGroup();

        // Sin colliders en el mapa el jugador cae a través del suelo.
        var missing = MeshesWithoutCollider(cam.transform);
        if (missing.Count > 0 && EditorUtility.DisplayDialog("Jugador",
                $"{missing.Count} objetos del escenario no tienen collider y el jugador los atravesaría (suelo, paredes...).\n\n¿Añadirles un MeshCollider?",
                "Añadir colliders", "No"))
        {
            foreach (var r in missing)
                Undo.AddComponent<MeshCollider>(r.gameObject).sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
        }

        // Altura de los ojos: de la cámara al suelo que tenga debajo.
        Physics.SyncTransforms();
        Vector3 camPos = cam.transform.position;
        Vector3 ground;
        float eye;
        if (Physics.Raycast(camPos, Vector3.down, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore) && hit.distance > 0.05f)
        {
            ground = hit.point;
            eye = hit.distance;
        }
        else
        {
            eye = 1.6f;
            ground = camPos + Vector3.down * eye;
            Debug.LogWarning("Jugador: no hay suelo con collider debajo de la cámara; se usa una altura de 1.6.");
        }

        float height = eye * 1.12f;
        float k = height / 1.8f; // escala respecto a una persona de 1.8 m

        var player = new GameObject("Jugador");
        Undo.RegisterCreatedObjectUndo(player, "Jugador");
        player.transform.position = ground;
        player.transform.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);

        var cc = player.AddComponent<CharacterController>();
        cc.height = height;
        cc.radius = height * 0.22f;
        cc.center = new Vector3(0f, height * 0.5f + 0.01f * k, 0f);
        cc.stepOffset = height * 0.2f;
        cc.skinWidth = Mathf.Max(0.001f, cc.radius * 0.1f);
        cc.minMoveDistance = 0f;

        var fpc = player.AddComponent<FirstPersonController>();
        fpc.cam = cam;
        fpc.walkSpeed = 4f * k;
        fpc.dashDistance = 2f * k;
        fpc.jumpHeight = 1f * k;
        fpc.gravity = -20f * k;
        fpc.bobAmount = 0.03f * k;

        // La cámara pasa a ser los ojos del jugador (conserva su inclinación arriba/abajo).
        float pitch = cam.transform.eulerAngles.x;
        Undo.SetTransformParent(cam.transform, player.transform, "Cámara al jugador");
        Undo.RecordObject(cam.transform, "Cámara al jugador");
        cam.transform.localPosition = new Vector3(0f, eye, 0f);
        cam.transform.localRotation = Quaternion.Euler(pitch > 180f ? pitch - 360f : pitch, 0f, 0f);
        if (cam.nearClipPlane > 0.05f * k)
        {
            Undo.RecordObject(cam, "Plano cercano");
            cam.nearClipPlane = Mathf.Max(0.005f, 0.05f * k);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = player;
        Debug.Log($"Jugador creado: altura {height:0.###}, ojos a {eye:0.###}, velocidad {fpc.walkSpeed:0.##} (escala {k:0.###}).");
    }

    /// <summary>MeshRenderers del escenario sin collider (sin contar haces de luz, cubos rompibles ni lo que cuelga de la cámara).</summary>
    static List<MeshRenderer> MeshesWithoutCollider(Transform camTransform)
    {
        var list = new List<MeshRenderer>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.GetComponent<Collider>() != null || r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null) continue;
            if (r.GetComponent<LightShaft>() != null || r.GetComponent<PyramidCube>() != null) continue;
            if (r.transform.IsChildOf(camTransform)) continue;
            list.Add(r);
        }
        return list;
    }
}

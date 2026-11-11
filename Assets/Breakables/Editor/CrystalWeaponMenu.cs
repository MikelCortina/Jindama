using UnityEditor;
using UnityEngine;

static class CrystalWeaponMenu
{
    // Posición en la vista: derecha, abajo y delante de la cámara (unidades de mundo), y tamaño del arma.
    static readonly Vector3 ViewOffset = new Vector3(0.32f, -0.26f, 0.75f);
    const float ViewSize = 0.3f;
    const float Tilt = 12f; // grados: la punta un poco hacia arriba y hacia el centro de la pantalla

    [MenuItem("GameObject/Arma de cristal en la cámara", false, 11)]
    static void Create()
    {
        var cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam == null) { EditorUtility.DisplayDialog("Arma de cristal", "No hay ninguna cámara en la escena.", "Vale"); return; }

        var guids = AssetDatabase.FindAssets("ArmaCristal t:Model");
        if (guids.Length == 0) { EditorUtility.DisplayDialog("Arma de cristal", "No encuentro ArmaCristal.fbx en el proyecto.", "Vale"); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));

        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(go, "Arma de cristal");
        go.name = "Arma de cristal";
        var weapon = go.AddComponent<CrystalWeapon>();
        weapon.AutoSetup();
        weapon.cam = cam;

        // Orientar: el eje del arma (pivote -> cono central, el que se dispara) mirando hacia delante.
        var t = go.transform;
        Vector3 tipCenter = weapon.tip != null ? CrystalWeapon.MeshCenter(weapon.tip) : t.position + cam.transform.forward;
        Vector3 muzzle = (tipCenter - t.position).normalized;
        if (muzzle.sqrMagnitude > 0.5f) t.rotation = Quaternion.FromToRotation(muzzle, cam.transform.forward) * t.rotation;
        t.rotation = Quaternion.AngleAxis(-Tilt, cam.transform.right) * Quaternion.AngleAxis(-Tilt, cam.transform.up) * t.rotation;

        // Escalar a ViewSize.
        var b = Bounds(go);
        float extent = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (extent > 1e-5f) t.localScale *= ViewSize / extent;

        // Colgar de la cámara y llevar el centro del arma a su sitio en la vista.
        t.SetParent(cam.transform, true);
        b = Bounds(go);
        Vector3 wanted = cam.transform.TransformPoint(ViewOffset);
        t.position += wanted - b.center;

        // Que no la recorte el plano cercano de la cámara.
        if (cam.nearClipPlane > 0.1f)
        {
            Undo.RecordObject(cam, "Plano cercano");
            cam.nearClipPlane = 0.05f;
        }

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}

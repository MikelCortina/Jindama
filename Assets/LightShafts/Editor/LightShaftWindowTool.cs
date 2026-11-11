using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coloca haces de luz (LightShaft) en las ventanas del mapa, con la forma exacta de cada ventana.
///
/// Una "ventana" es una cara plana: el conjunto de triángulos conectados que comparten la misma normal.
/// Si su contorno tiene 4 esquinas (rectángulo, trapecio...) el haz usa esas 4 esquinas tal cual;
/// si no, se usa el rectángulo de área mínima que la contiene.
///
/// Dos modos:
///  - Clic: haces clic en la cara de cada ventana en la Scene view.
///  - Automático: todas las caras planas de los objetos/materiales elegidos que pasen los filtros.
/// </summary>
public class LightShaftWindowTool : EditorWindow
{
    const string GroupName = "Haces de luz";

    GameObject root;
    readonly List<Material> materials = new List<Material>();
    readonly HashSet<Material> selectedMaterials = new HashSet<Material>();

    float minArea = 0.01f;
    float maxArea = 5f;
    bool onlyExact = true;
    float angleTolerance = 3f;

    bool flipDirection;
    bool useSunIfEntering;
    float spread = 0f;
    float maxDistance = 30f;
    int resolution = 32;

    bool picking;
    WindowShape? hover;
    List<WindowShape> preview; // ventanas detectadas en modo automático (null = sin previsualizar)
    Vector2 scroll;

    struct WindowShape
    {
        public Vector3 center, normal, up;
        public Vector3[] corners;  // 4 esquinas en mundo, en orden alrededor del borde
        public bool exact;         // true = contorno real de 4 lados; false = rectángulo ajustado
        public float area;
    }

    [MenuItem("Tools/Haces de luz/Colocar en ventanas")]
    static void Open() => GetWindow<LightShaftWindowTool>("Haces en ventanas");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        if (root == null && Selection.activeGameObject != null) root = Selection.activeGameObject;
        RefreshMaterials();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        picking = false;
    }

    // ------------------------------------------------------------------ GUI

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();
        root = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Ventanas / Mapa", "Objeto que contiene las ventanas. Puede ser solo el objeto de los cristales (p.ej. Cylinder.006) o el mapa entero."),
            root, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck()) RefreshMaterials();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Haces nuevos", EditorStyles.boldLabel);
        spread = EditorGUILayout.FloatField("Apertura (grados)", spread);
        maxDistance = EditorGUILayout.FloatField("Distancia máxima", maxDistance);
        resolution = EditorGUILayout.IntSlider("Resolución (eje largo)", resolution, 4, 128);
        flipDirection = EditorGUILayout.Toggle(
            new GUIContent("Invertir dirección", "Si los haces salen hacia fuera del mapa en vez de hacia dentro."),
            flipDirection);
        useSunIfEntering = EditorGUILayout.Toggle(
            new GUIContent("Usar dirección del sol", "Si la Directional Light entra por la ventana, el haz sigue al sol. Si no, sale perpendicular a la ventana."),
            useSunIfEntering);

        // --- Modo clic
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1. Clic en ventanas", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Activa el modo y haz clic en el cristal de cada ventana en la Scene view. " +
                                "Se resalta el contorno que se va a usar. Alt+arrastrar sigue orbitando, Esc sale.", MessageType.None);
        var prevColor = GUI.backgroundColor;
        if (picking) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
        if (GUILayout.Button(picking ? "Modo clic ACTIVO (pulsa para salir)" : "Activar modo clic", GUILayout.Height(28)))
        {
            picking = !picking;
            hover = null;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = prevColor;

        // --- Modo automático
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. Automático", EditorStyles.boldLabel);
        if (root == null)
        {
            EditorGUILayout.HelpBox("Arrastra arriba el objeto de las ventanas (o el mapa).", MessageType.Info);
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Materiales (vacío = todos)", EditorStyles.miniBoldLabel);
            foreach (var m in materials)
            {
                bool on = selectedMaterials.Contains(m);
                bool now = EditorGUILayout.ToggleLeft(m.name, on);
                if (now && !on) selectedMaterials.Add(m);
                else if (!now && on) selectedMaterials.Remove(m);
            }

            minArea = EditorGUILayout.FloatField(new GUIContent("Área mínima", "Ignora caras más pequeñas."), minArea);
            maxArea = EditorGUILayout.FloatField(new GUIContent("Área máxima", "Ignora caras más grandes (paredes, suelo)."), maxArea);
            onlyExact = EditorGUILayout.Toggle(new GUIContent("Solo contornos limpios", "Descarta caras con agujeros o contornos raros (se aproximarían con un rectángulo)."), onlyExact);
            angleTolerance = EditorGUILayout.Slider(new GUIContent("Tolerancia de ángulo", "Grados de diferencia permitidos entre triángulos de una misma cara."), angleTolerance, 0.1f, 15f);
            if (EditorGUI.EndChangeCheck() && preview != null) UpdatePreview();

            if (GUILayout.Button(preview == null ? "Previsualizar ventanas detectadas" : $"Ocultar previsualización ({preview.Count} ventanas)"))
            {
                if (preview == null) UpdatePreview(); else preview = null;
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Crear haces en las ventanas detectadas", GUILayout.Height(28)))
                CreateAll();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Borrar todos los haces creados"))
        {
            var g = GameObject.Find(GroupName);
            if (g != null) Undo.DestroyObjectImmediate(g);
        }

        EditorGUILayout.EndScrollView();
    }

    void UpdatePreview()
    {
        preview = DetectAll();
        SceneView.RepaintAll();
    }

    void RefreshMaterials()
    {
        preview = null;
        materials.Clear();
        selectedMaterials.Clear();
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<LightShaft>() != null) continue;
            foreach (var m in r.sharedMaterials)
                if (m != null && !materials.Contains(m)) materials.Add(m);
        }
    }

    // ------------------------------------------------------------------ Scene view

    void OnSceneGUI(SceneView view)
    {
        if (preview != null && !picking)
        {
            foreach (var w in preview)
                DrawShape(w, new Color(1f, 0.9f, 0.4f, 0.2f), new Color(1f, 0.8f, 0.2f, 1f));
        }

        if (!picking) return;

        var e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (e.type == EventType.MouseMove)
        {
            hover = PickWindow(HandleUtility.GUIPointToWorldRay(e.mousePosition));
            view.Repaint();
        }

        if (hover.HasValue)
            DrawShape(hover.Value, new Color(1f, 0.95f, 0.5f, 0.35f), hover.Value.exact ? Color.yellow : new Color(1f, 0.5f, 0.2f));

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            var w = PickWindow(HandleUtility.GUIPointToWorldRay(e.mousePosition));
            if (w.HasValue)
            {
                if (ShaftExistsAt(w.Value.center))
                    Debug.Log("Ya hay un haz en esa ventana.");
                else
                    Selection.activeGameObject = CreateShaft(w.Value);
            }
            e.Use();
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            picking = false;
            hover = null;
            e.Use();
            Repaint();
        }
    }

    static void DrawShape(WindowShape w, Color fill, Color outline)
    {
        int n = w.corners.Length;
        var c = new Vector3[n + 1]; // cerrado, para la línea del contorno
        var off = w.normal * 0.003f;
        for (int i = 0; i <= n; i++) c[i] = w.corners[i % n] + off;
        Handles.color = fill;
        Handles.DrawAAConvexPolygon(w.corners.Select(p => p + off).ToArray());
        Handles.color = outline;
        Handles.DrawAAPolyLine(3f, c);
        float len = 0f;
        for (int i = 0; i < n; i++) len = Mathf.Max(len, (c[i] - c[i + 1]).magnitude);
        Handles.DrawLine(w.center, w.center + w.normal * len * 0.5f);
    }

    // ------------------------------------------------------------------ Detección

    IEnumerable<MeshFilter> MapMeshes()
    {
        var filters = root != null
            ? root.GetComponentsInChildren<MeshFilter>()
            : Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        foreach (var mf in filters)
            if (mf.sharedMesh != null && mf.GetComponent<LightShaft>() == null)
                yield return mf;
    }

    float MinNormalDot => Mathf.Cos(angleTolerance * Mathf.Deg2Rad);

    /// <summary>Cara plana bajo el rayo. El haz se orienta hacia el lado desde el que miras (dentro de la sala).</summary>
    WindowShape? PickWindow(Ray ray)
    {
        float best = float.MaxValue;
        Vector3[] bestVerts = null;
        int[] bestTris = null;
        int bestTri = -1;

        var filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null || mf.GetComponent<LightShaft>() != null) continue;
            var mesh = mf.sharedMesh;
            if (!mesh.bounds.IntersectRay(new Ray(mf.transform.InverseTransformPoint(ray.origin),
                                                  mf.transform.InverseTransformDirection(ray.direction))))
                continue;

            var verts = WorldVerts(mf);
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length / 3; t++)
            {
                if (RayTriangle(ray, verts[tris[t * 3]], verts[tris[t * 3 + 1]], verts[tris[t * 3 + 2]], out float d) && d < best)
                {
                    best = d;
                    bestVerts = verts;
                    bestTris = tris;
                    bestTri = t;
                }
            }
        }

        if (bestTri < 0) return null;
        var island = FloodFlatFace(bestVerts, bestTris, bestTri, BuildEdgeMap(bestVerts, bestTris, null), MinNormalDot);
        var w = BuildShape(bestVerts, bestTris, island);

        // Hacia la cámara = hacia dentro de la sala.
        bool towardViewer = Vector3.Dot(w.normal, ray.direction) < 0f;
        if (towardViewer == flipDirection) w.normal = -w.normal;
        return w;
    }

    List<WindowShape> DetectAll()
    {
        var result = new List<WindowShape>();
        if (root == null) return result;

        // Centro del mapa para decidir qué lado de cada ventana es "dentro".
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        bool hasBounds = false;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.GetComponent<LightShaft>() != null) continue;
            if (!r.transform.IsChildOf(root.transform.root)) continue;
            if (hasBounds) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; hasBounds = true; }
        }
        Vector3 inside = bounds.center;

        foreach (var mf in MapMeshes())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;
            var verts = WorldVerts(mf);
            var allTris = mesh.triangles;
            var mats = mr != null ? mr.sharedMaterials : new Material[0];

            var allowed = new HashSet<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (selectedMaterials.Count > 0 && (sub >= mats.Length || !selectedMaterials.Contains(mats[sub])))
                    continue;
                var desc = mesh.GetSubMesh(sub);
                for (int t = desc.indexStart / 3; t < (desc.indexStart + desc.indexCount) / 3; t++)
                    allowed.Add(t);
            }

            var edgeMap = BuildEdgeMap(verts, allTris, allowed);
            var visited = new HashSet<int>();
            foreach (int t in allowed)
            {
                if (visited.Contains(t)) continue;
                var island = FloodFlatFace(verts, allTris, t, edgeMap, MinNormalDot);
                visited.UnionWith(island);

                var w = BuildShape(verts, allTris, island);
                if (w.area < minArea || w.area > maxArea) continue;
                if (onlyExact && !w.exact) continue;

                bool facesInside = Vector3.Dot(w.normal, inside - w.center) > 0f;
                if (facesInside == flipDirection) w.normal = -w.normal;
                result.Add(w);
            }
        }
        return result;
    }

    static Vector3[] WorldVerts(MeshFilter mf)
    {
        var v = mf.sharedMesh.vertices;
        var m = mf.transform.localToWorldMatrix;
        for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
        return v;
    }

    static Vector3Int Quantize(Vector3 p) =>
        new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));

    static (Vector3Int, Vector3Int) EdgeKey(Vector3Int qa, Vector3Int qb)
    {
        bool swap = qa.x > qb.x || (qa.x == qb.x && (qa.y > qb.y || (qa.y == qb.y && qa.z > qb.z)));
        return swap ? (qb, qa) : (qa, qb);
    }

    static (Vector3Int, Vector3Int) EdgeKey(Vector3 a, Vector3 b) => EdgeKey(Quantize(a), Quantize(b));

    /// <summary>Arista (vértices soldados por posición) -> triángulos que la comparten.</summary>
    static Dictionary<(Vector3Int, Vector3Int), List<int>> BuildEdgeMap(Vector3[] v, int[] tris, HashSet<int> allowed)
    {
        var edgeMap = new Dictionary<(Vector3Int, Vector3Int), List<int>>();
        for (int t = 0; t < tris.Length / 3; t++)
        {
            if (allowed != null && !allowed.Contains(t)) continue;
            for (int e = 0; e < 3; e++)
            {
                var key = EdgeKey(v[tris[t * 3 + e]], v[tris[t * 3 + (e + 1) % 3]]);
                if (!edgeMap.TryGetValue(key, out var list)) edgeMap[key] = list = new List<int>(2);
                list.Add(t);
            }
        }
        return edgeMap;
    }

    /// <summary>Triángulos conectados por aristas con la misma normal (una cara plana).</summary>
    static List<int> FloodFlatFace(Vector3[] v, int[] tris, int start,
                                   Dictionary<(Vector3Int, Vector3Int), List<int>> edgeMap, float minDot)
    {
        Vector3 n0 = TriNormal(v, tris, start);
        var island = new List<int> { start };
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            for (int e = 0; e < 3; e++)
            {
                var key = EdgeKey(v[tris[t * 3 + e]], v[tris[t * 3 + (e + 1) % 3]]);
                foreach (int nb in edgeMap[key])
                {
                    if (seen.Contains(nb)) continue;
                    if (Vector3.Dot(TriNormal(v, tris, nb), n0) < minDot) continue;
                    seen.Add(nb);
                    island.Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }
        return island;
    }

    static Vector3 TriNormal(Vector3[] v, int[] tris, int t)
    {
        Vector3 a = v[tris[t * 3]], b = v[tris[t * 3 + 1]], c = v[tris[t * 3 + 2]];
        return Vector3.Cross(b - a, c - a).normalized;
    }

    /// <summary>
    /// Contorno exterior de la cara, sin puntos intermedios alineados.
    /// Devuelve null si la cara tiene agujeros o varios contornos.
    /// </summary>
    static List<Vector3> BoundaryLoop(Vector3[] v, int[] tris, List<int> island)
    {
        var count = new Dictionary<(Vector3Int, Vector3Int), int>();
        var pos = new Dictionary<Vector3Int, Vector3>();
        var directed = new List<(Vector3Int a, Vector3Int b)>();
        foreach (int t in island)
        for (int e = 0; e < 3; e++)
        {
            Vector3 pa = v[tris[t * 3 + e]], pb = v[tris[t * 3 + (e + 1) % 3]];
            var qa = Quantize(pa);
            var qb = Quantize(pb);
            pos[qa] = pa;
            pos[qb] = pb;
            var key = EdgeKey(qa, qb);
            count.TryGetValue(key, out int n);
            count[key] = n + 1;
            directed.Add((qa, qb));
        }

        var next = new Dictionary<Vector3Int, Vector3Int>();
        foreach (var (a, b) in directed)
            if (count[EdgeKey(a, b)] == 1)
            {
                if (next.ContainsKey(a)) return null; // contorno no simple
                next[a] = b;
            }
        if (next.Count < 3) return null;

        var loop = new List<Vector3>();
        var startKey = default(Vector3Int);
        foreach (var k in next.Keys) { startKey = k; break; }
        var cur = startKey;
        do
        {
            loop.Add(pos[cur]);
            if (!next.TryGetValue(cur, out cur) || loop.Count > next.Count) return null;
        } while (cur != startKey);
        if (loop.Count != next.Count) return null; // hay más de un contorno (agujeros)

        // Quitar vértices alineados (aristas partidas por la triangulación).
        for (int i = loop.Count - 1; i >= 0 && loop.Count > 3; i--)
        {
            Vector3 prev = loop[(i - 1 + loop.Count) % loop.Count];
            Vector3 nxt = loop[(i + 1) % loop.Count];
            Vector3 d1 = (loop[i] - prev).normalized, d2 = (nxt - loop[i]).normalized;
            if (Vector3.Cross(d1, d2).magnitude < 0.01f) loop.RemoveAt(i);
        }
        return loop;
    }

    static WindowShape BuildShape(Vector3[] v, int[] tris, List<int> island)
    {
        Vector3 normal = Vector3.zero;
        float area = 0f;
        var points = new List<Vector3>();
        var used = new HashSet<int>();
        foreach (int t in island)
        {
            Vector3 a = v[tris[t * 3]], b = v[tris[t * 3 + 1]], c = v[tris[t * 3 + 2]];
            var cr = Vector3.Cross(b - a, c - a);
            normal += cr;
            area += cr.magnitude * 0.5f;
            for (int k = 0; k < 3; k++)
                if (used.Add(tris[t * 3 + k])) points.Add(v[tris[t * 3 + k]]);
        }
        normal.Normalize();

        // Rectángulo de área mínima: da el centro y los ejes del haz (y la forma si no es un quad).
        Vector3 origin = points[0];
        float bestArea = float.MaxValue;
        Vector3 bestX = Vector3.zero, bestY = Vector3.zero;
        float bx0 = 0, bx1 = 0, by0 = 0, by1 = 0;
        foreach (int t in island)
        for (int e = 0; e < 3; e++)
        {
            Vector3 d = v[tris[t * 3 + (e + 1) % 3]] - v[tris[t * 3 + e]];
            d -= normal * Vector3.Dot(d, normal);
            if (d.sqrMagnitude < 1e-10f) continue;
            d.Normalize();
            Vector3 o = Vector3.Cross(normal, d);

            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in points)
            {
                var r = p - origin;
                float x = Vector3.Dot(r, d), y = Vector3.Dot(r, o);
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
            float a = (x1 - x0) * (y1 - y0);
            if (a < bestArea)
            {
                bestArea = a;
                bestX = d; bestY = o;
                bx0 = x0; bx1 = x1; by0 = y0; by1 = y1;
            }
        }

        var shape = new WindowShape
        {
            center = origin + bestX * (bx0 + bx1) * 0.5f + bestY * (by0 + by1) * 0.5f,
            normal = normal,
            up = (bx1 - bx0) > (by1 - by0) ? bestX : bestY, // eje largo = "arriba" del haz
            area = area,
        };

        var loop = BoundaryLoop(v, tris, island);
        if (loop != null && loop.Count >= 3)
        {
            shape.corners = loop.ToArray();
            shape.exact = true;
            // Centro = media de las esquinas (queda dentro de la ventana en trapecios, círculos...).
            Vector3 sum = Vector3.zero;
            foreach (var p in loop) sum += p;
            shape.center = sum / loop.Count;
        }
        else
        {
            shape.corners = new[]
            {
                origin + bestX * bx0 + bestY * by0,
                origin + bestX * bx1 + bestY * by0,
                origin + bestX * bx1 + bestY * by1,
                origin + bestX * bx0 + bestY * by1,
            };
        }
        return shape;
    }

    static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float dist)
    {
        dist = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(ray.direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        float inv = 1f / det;
        Vector3 s = ray.origin - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float w = Vector3.Dot(ray.direction, q) * inv;
        if (w < 0f || u + w > 1f) return false;
        dist = Vector3.Dot(e2, q) * inv;
        return dist > 0f;
    }

    // ------------------------------------------------------------------ Creación

    void CreateAll()
    {
        var windows = DetectAll();
        int created = 0;
        Undo.SetCurrentGroupName("Crear haces en ventanas");
        foreach (var w in windows)
        {
            if (ShaftExistsAt(w.center)) continue;
            CreateShaft(w);
            created++;
        }
        Debug.Log($"Haces de luz: {created} creados ({windows.Count} ventanas detectadas).");
        if (preview != null) UpdatePreview();
    }

    static bool ShaftExistsAt(Vector3 center)
    {
        foreach (var s in Object.FindObjectsByType<LightShaft>(FindObjectsSortMode.None))
            if ((s.transform.position - center).sqrMagnitude < 0.02f * 0.02f)
                return true;
        return false;
    }

    GameObject CreateShaft(WindowShape w)
    {
        var group = GameObject.Find(GroupName);
        if (group == null)
        {
            group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "Crear grupo de haces");
        }

        var go = new GameObject("Haz ventana " + (group.transform.childCount + 1));
        Undo.RegisterCreatedObjectUndo(go, "Crear haz de luz");
        go.transform.SetParent(group.transform, false);
        go.transform.SetPositionAndRotation(w.center, Quaternion.LookRotation(w.normal, w.up));

        // Esquinas en el plano XY local del haz.
        int n = w.corners.Length;
        var local = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var p = go.transform.InverseTransformPoint(w.corners[i]);
            local[i] = new Vector2(p.x, p.y);
        }
        // Orden antihorario visto desde delante, para que las paredes del haz queden bien.
        float signedArea = 0f;
        for (int i = 0; i < n; i++) signedArea += local[i].x * local[(i + 1) % n].y - local[(i + 1) % n].x * local[i].y;
        if (signedArea < 0f) System.Array.Reverse(local);

        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = LightShaftEditor.GetOrCreateMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var shaft = go.AddComponent<LightShaft>();
        shaft.corners = local;
        if (n == 4)
        {
            float lenS = Mathf.Max((local[1] - local[0]).magnitude, (local[2] - local[3]).magnitude);
            float lenT = Mathf.Max((local[3] - local[0]).magnitude, (local[2] - local[1]).magnitude);
            shaft.windowSize = new Vector2(lenS, lenT);
            float longest = Mathf.Max(lenS, lenT, 1e-4f);
            shaft.resolutionX = Mathf.Clamp(Mathf.RoundToInt(resolution * lenS / longest), 4, 128);
            shaft.resolutionY = Mathf.Clamp(Mathf.RoundToInt(resolution * lenT / longest), 4, 128);
        }
        else
        {
            // Polígono: X = puntos por el contorno, Y = anillos hacia el centro.
            var min = local[0];
            var max = local[0];
            foreach (var p in local) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            shaft.windowSize = max - min;
            shaft.resolutionX = Mathf.Clamp(Mathf.Max(resolution, n * 2), 4, 128);
            shaft.resolutionY = Mathf.Clamp(resolution / 3, 4, 128);
        }
        shaft.spread = new Vector2(spread, spread);
        shaft.maxDistance = maxDistance;

        var sun = RenderSettings.sun;
        if (useSunIfEntering && sun != null && Vector3.Dot(sun.transform.forward, w.normal) > 0.1f)
            shaft.sun = sun;

        shaft.Generate();
        return go;
    }
}

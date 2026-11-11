using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genera una malla de "haz de luz" que sale de una ventana rectangular y se corta
/// contra los obstáculos (piedras, suelo, paredes) que encuentra por el camino.
///
/// La ventana es el rectángulo del plano XY local del objeto (tamaño = <see cref="windowSize"/>).
/// La luz viaja por el +Z local, o en la dirección de <see cref="sun"/> si está asignado.
/// Se lanza una rejilla de rayos desde la ventana; cada rayo se para donde choca,
/// así que detrás de una piedra queda un "hueco" de sombra dentro del haz.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LightShaft : MonoBehaviour
{
    [Header("Ventana")]
    [Tooltip("Ancho (X) y alto (Y) de la ventana en unidades locales.")]
    public Vector2 windowSize = new Vector2(1f, 2f);

    [Tooltip("Forma exacta de la ventana: 4 esquinas en el plano XY local, en orden (vacío = rectángulo de Window Size). " +
             "La rellena automáticamente Tools > Haces de luz > Colocar en ventanas.")]
    public Vector2[] corners = new Vector2[0];

    [Header("Dirección de la luz")]
    [Tooltip("Si se asigna, la luz sigue la dirección de esta luz (p.ej. la Directional Light). Si no, usa el +Z del objeto.")]
    public Light sun;

    [Tooltip("Apertura del haz en grados (X, Y). 0 = rayos paralelos.")]
    public Vector2 spread = new Vector2(4f, 4f);

    [Header("Rayos")]
    [Range(2, 128)] public int resolutionX = 24;
    [Range(2, 128)] public int resolutionY = 24;
    public float maxDistance = 30f;
    [Tooltip("Capas que bloquean la luz. Necesitan Collider (MeshCollider en el mapa).")]
    public LayerMask obstacles = ~0;
    [Tooltip("Distancia desde la ventana antes de empezar a detectar choques (evita chocar con el marco).")]
    public float startOffset = 0.05f;
    [Tooltip("Separa el final del haz de la superficie para que no parpadee (z-fighting).")]
    public float surfaceOffset = 0.02f;
    [Tooltip("Corta el haz en los bordes de los obstáculos en vez de estirar triángulos entre la piedra y el fondo. " +
             "Es el salto de longitud permitido entre rayos vecinos, en múltiplos del tamaño de celda. Más bajo = cortes más limpios.")]
    public float edgeBreak = 4f;

    [Header("Aspecto")]
    [Tooltip("Alfa (multiplicador) al final del haz si llega a la distancia máxima.")]
    [Range(0f, 1f)] public float endAlpha = 0.35f;

    [Tooltip("Regenerar automáticamente al mover el objeto en el editor.")]
    public bool autoUpdateInEditor = true;

    Mesh _mesh;
    Vector3 _lastPos;
    Quaternion _lastRot;
    Vector3 _lastScale;
    Vector3 _lastSunDir;

    public Vector3 LightDirectionWorld =>
        (sun != null ? sun.transform.forward : transform.forward).normalized;

    void OnEnable() => Generate();

    void OnDestroy()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
    }

    void OnValidate() => _lastPos = new Vector3(float.NaN, 0, 0); // fuerza regenerar en el próximo Update

    void Update()
    {
        if (Application.isPlaying || !autoUpdateInEditor) return;

        var sunDir = LightDirectionWorld;
        if (transform.position != _lastPos || transform.rotation != _lastRot ||
            transform.lossyScale != _lastScale || sunDir != _lastSunDir)
        {
            Generate();
        }
    }

    [ContextMenu("Generar haz")]
    public void Generate()
    {
        _lastPos = transform.position;
        _lastRot = transform.rotation;
        _lastScale = transform.lossyScale;
        _lastSunDir = LightDirectionWorld;

        if (_mesh == null)
        {
            // DontSave: la malla se regenera en OnEnable, no hace falta guardarla en la escena.
            _mesh = new Mesh { name = "LightShaft_" + name, hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
        }
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        Vector3 baseDir = LightDirectionWorld;
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        float tx = Mathf.Tan(spread.x * Mathf.Deg2Rad);
        float ty = Mathf.Tan(spread.y * Mathf.Deg2Rad);

        // No queremos que el haz choque con su propio collider (si tuviera).
        bool hitBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = false;

        var wc = GetCorners();
        float hx = 0f, hy = 0f; // semiextensión de la ventana, para repartir la apertura
        foreach (var p in wc) { hx = Mathf.Max(hx, Mathf.Abs(p.x)); hy = Mathf.Max(hy, Mathf.Abs(p.y)); }
        hx = Mathf.Max(hx, 1e-4f);
        hy = Mathf.Max(hy, 1e-4f);

        // ---- Puntos de salida de los rayos, celdas de la tapa y aristas del borde ----
        // Celda (a, b, c, d): a-b en una fila, c-d en la siguiente; a-c y b-d vecinos.
        var samples = new List<Vector2>();
        var cells = new List<(int a, int b, int c, int d)>();
        var border = new List<(int fromK, int toK)>();
        float cellLocal;

        if (wc.Length == 4)
        {
            // Rejilla bilineal entre las 4 esquinas: rectángulos, trapecios, etc.
            int nx = Mathf.Max(2, resolutionX), ny = Mathf.Max(2, resolutionY);
            int vx = nx + 1;
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float s = i / (float)nx, t = j / (float)ny;
                samples.Add(Vector2.Lerp(Vector2.Lerp(wc[0], wc[1], s), Vector2.Lerp(wc[3], wc[2], s), t));
            }
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * vx + i;
                cells.Add((a, a + 1, a + vx, a + vx + 1));
            }
            for (int i = 0; i < nx; i++) border.Add((i, i + 1));                               // abajo
            for (int i = 0; i < nx; i++) border.Add((ny * vx + i + 1, ny * vx + i));           // arriba
            for (int j = 0; j < ny; j++) border.Add(((j + 1) * vx, j * vx));                   // izquierda
            for (int j = 0; j < ny; j++) border.Add((j * vx + nx, (j + 1) * vx + nx));        // derecha
            cellLocal = Mathf.Max(hx * 2f / nx, hy * 2f / ny);
        }
        else
        {
            // Polígono (círculos, octógonos...): anillos desde el centro hasta el contorno.
            var ring = PerimeterSamples(wc, Mathf.Max(resolutionX, wc.Length), out float perimeter);
            int m = ring.Count;
            int rings = Mathf.Max(1, resolutionY);
            Vector2 center = Vector2.zero;
            foreach (var p in wc) center += p;
            center /= wc.Length;
            float radius = 0f;
            foreach (var p in wc) radius = Mathf.Max(radius, (p - center).magnitude);

            for (int j = 0; j <= rings; j++)
            {
                float r = j / (float)rings;
                foreach (var p in ring) samples.Add(center + (p - center) * r);
            }
            for (int j = 0; j < rings; j++)
            for (int i = 0; i < m; i++)
            {
                int i2 = (i + 1) % m;
                cells.Add((j * m + i, j * m + i2, (j + 1) * m + i, (j + 1) * m + i2));
            }
            for (int i = 0; i < m; i++) border.Add((rings * m + i, rings * m + (i + 1) % m));
            cellLocal = Mathf.Max(perimeter / m, radius / rings);
        }

        // ---- Rayos ----
        int n = samples.Count;
        var start = new Vector3[n];
        var end = new Vector3[n];
        var endA = new float[n];
        var rayLen = new float[n];

        for (int k = 0; k < n; k++)
        {
            Vector2 p2 = samples[k];
            Vector3 local = new Vector3(p2.x, p2.y, 0f);
            Vector3 origin = transform.TransformPoint(local);
            Vector3 dir = (baseDir + right * (p2.x / hx * tx) + up * (p2.y / hy * ty)).normalized;

            Vector3 rayOrigin = origin + dir * startOffset;
            float dist = maxDistance;
            if (Physics.Raycast(rayOrigin, dir, out var hit, maxDistance, obstacles, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - surfaceOffset);

            start[k] = local;
            end[k] = transform.InverseTransformPoint(rayOrigin + dir * dist);
            rayLen[k] = startOffset + dist;
            endA[k] = Mathf.Lerp(1f, endAlpha, (startOffset + dist) / Mathf.Max(0.0001f, maxDistance + startOffset));
        }

        Physics.queriesHitBackfaces = hitBackfaces;

        // ---- Construcción de la malla ----
        // Tapa final: los puntos de impacto (se hunde donde hay piedras -> sombra).
        // Paredes laterales: el contorno de la ventana extruido hasta su punto de impacto.
        int capVerts = n;
        var verts = new Vector3[capVerts + border.Count * 4]; // 4 vértices por segmento de pared
        var colors = new Color[verts.Length];
        var tris = new List<int>(cells.Count * 6 + border.Count * 6);

        for (int k = 0; k < capVerts; k++)
        {
            verts[k] = end[k];
            colors[k] = new Color(1, 1, 1, endA[k]);
        }
        // Tamaño de celda en mundo: un salto de longitud mucho mayor entre rayos vecinos es el borde de un obstáculo.
        var scale = transform.lossyScale;
        float cell = cellLocal * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        float maxJump = Mathf.Max(edgeBreak, 0.01f) * cell;

        void CapTri(int i0, int i1, int i2)
        {
            float lo = Mathf.Min(rayLen[i0], Mathf.Min(rayLen[i1], rayLen[i2]));
            float hi = Mathf.Max(rayLen[i0], Mathf.Max(rayLen[i1], rayLen[i2]));
            if (hi - lo > maxJump) return; // no estirar el triángulo de la piedra al fondo
            tris.Add(i0); tris.Add(i1); tris.Add(i2);
        }

        foreach (var (a, b, c, d) in cells)
        {
            CapTri(a, c, b);
            CapTri(b, c, d);
        }

        int w = capVerts;
        void Wall(int fromK, int toK)
        {
            int s0 = w, e0 = w + 1, s1 = w + 2, e1 = w + 3;
            verts[s0] = start[fromK]; colors[s0] = Color.white;
            verts[e0] = end[fromK];   colors[e0] = new Color(1, 1, 1, endA[fromK]);
            verts[s1] = start[toK];   colors[s1] = Color.white;
            verts[e1] = end[toK];     colors[e1] = new Color(1, 1, 1, endA[toK]);
            tris.Add(s0); tris.Add(s1); tris.Add(e0);
            tris.Add(e0); tris.Add(s1); tris.Add(e1);
            w += 4;
        }

        foreach (var (fromK, toK) in border) Wall(fromK, toK);

        _mesh.Clear();
        _mesh.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.vertices = verts;
        _mesh.colors = colors;
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    /// <summary>Esquinas de la ventana en el plano XY local, en orden alrededor del borde.</summary>
    public Vector2[] GetCorners()
    {
        if (corners != null && corners.Length >= 3) return corners;
        float x = windowSize.x * 0.5f, y = windowSize.y * 0.5f;
        return new[] { new Vector2(-x, -y), new Vector2(x, -y), new Vector2(x, y), new Vector2(-x, y) };
    }

    /// <summary>Puntos repartidos por el contorno según la longitud de cada lado, incluyendo siempre las esquinas.</summary>
    static List<Vector2> PerimeterSamples(Vector2[] poly, int count, out float perimeter)
    {
        perimeter = 0f;
        for (int i = 0; i < poly.Length; i++) perimeter += (poly[(i + 1) % poly.Length] - poly[i]).magnitude;

        var result = new List<Vector2>();
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            int steps = Mathf.Max(1, Mathf.RoundToInt(count * (b - a).magnitude / Mathf.Max(perimeter, 1e-6f)));
            for (int k = 0; k < steps; k++) result.Add(Vector2.Lerp(a, b, k / (float)steps));
        }
        return result;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.95f, 0.6f, 1f);
        var c = GetCorners();
        for (int i = 0; i < c.Length; i++)
            Gizmos.DrawLine(transform.TransformPoint(c[i]), transform.TransformPoint(c[(i + 1) % c.Length]));
        Gizmos.DrawRay(transform.position, LightDirectionWorld * Mathf.Min(maxDistance, 3f));
    }
}

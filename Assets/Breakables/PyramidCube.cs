using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cubo destructible que se rompe como un cristal en trozos piramidales.
///
/// El cubo (perfecto por fuera) está partido por dentro en tetraedros de formas y tamaños irregulares:
/// puntos al azar en esquinas, aristas, caras e interior unidos con una triangulación de Delaunay 3D
/// (<see cref="CrystalFracture"/>). Rellenan el cubo exacto, sin huecos.
/// Mientras está entero se dibuja como una sola malla (solo las caras visibles), con un MeshCollider.
/// Cada impacto (<see cref="Hit"/>) suelta una sola pieza, la golpeada, como objeto con física; las que se
/// quedan sin apoyo caen también, una tras otra.
///
/// Apoyo: por defecto, las piezas que están metidas en otro collider (la pared en la que está clavada la roca).
/// Usa copias de este objeto (escaladas y giradas como quieras) para las rocas.
/// Materiales del MeshRenderer: [0] = superficie del cubo; [1] (opcional) = interior que queda al romper.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class PyramidCube : MonoBehaviour
{
    public enum AnchorMode { Embedded, Bottom, None }

    [Header("Forma")]
    [Tooltip("Cantidad de trozos (≈ trozos por lado). 3 ≈ 180 piezas, 4 ≈ 420, 5 ≈ 830.")]
    [Range(2, 7)] public int detail = 4;
    [Tooltip("Lado del cubo en unidades locales. Para deformarlo usa la escala del Transform.")]
    public float size = 1f;
    [Tooltip("Variante de la fractura. Con 'Seed From Position', cada copia en un sitio distinto se rompe distinto.")]
    public int seed;
    public bool seedFromPosition = true;

    [Header("Rotura")]
    [Tooltip("Las piezas que se quedan sin apoyo también caen (una tras otra).")]
    public bool releaseUnsupported = true;
    [Tooltip("Qué sujeta al cubo.\nEmbedded: las piezas metidas en otro collider (p.ej. la pared). Si no hay ninguna, Bottom.\nBottom: las piezas de abajo del todo.\nNone: nada cae por falta de apoyo.")]
    public AnchorMode anchors = AnchorMode.Embedded;
    public LayerMask anchorLayers = ~0;

    [Header("Piezas sueltas")]
    [Tooltip("Densidad para calcular la masa de cada pieza (piedra ≈ 2500).")]
    public float density = 2500f;
    [Tooltip("Velocidad con la que salen las piezas golpeadas.")]
    public float impulse = 1.5f;
    [Tooltip("Velocidad de giro aleatoria.")]
    public float randomTorque = 6f;
    [Tooltip("Segundos hasta que una pieza suelta desaparece (0 = nunca).")]
    public float pieceLifetime = 8f;
    [Tooltip("Retraso entre piezas sin apoyo que caen en cadena.")]
    public float cascadeDelay = 0.03f;

    class Piece
    {
        public readonly int[] v = new int[4]; // índices en vertexPos
        public Vector3 center;                // local
        public readonly List<int> neighbors = new List<int>();
        public bool anchor;
        public bool released;
    }

    // Las 4 caras de un tetraedro (índices en Piece.v).
    static readonly int[,] Faces = { { 0, 1, 2 }, { 0, 1, 3 }, { 0, 2, 3 }, { 1, 2, 3 } };

    readonly List<Piece> pieces = new List<Piece>();
    readonly Dictionary<long, List<int>> faceOwners = new Dictionary<long, List<int>>();
    readonly List<int> triToPiece = new List<int>();
    Vector3[] vertexPos; // posición local de cada punto de la fractura
    Mesh mesh;
    MeshCollider meshCollider;
    bool dirty;

    // ------------------------------------------------------------------ Ciclo de vida

    void OnEnable()
    {
        Build();
        RebuildMesh();
    }

    void OnValidate() => dirty = true;

    void Update()
    {
        if (Application.isPlaying) return;
        // En el editor: si la forma depende de la posición, se regenera al mover el objeto.
        if (seedFromPosition && transform.hasChanged)
        {
            transform.hasChanged = false;
            dirty = true;
        }
        if (dirty)
        {
            dirty = false;
            Build();
            RebuildMesh();
        }
    }

    int EffectiveSeed()
    {
        if (!seedFromPosition) return seed;
        Vector3 p = transform.position * 100f;
        unchecked
        {
            int h = seed * 486187739;
            h = h * 31 + Mathf.RoundToInt(p.x);
            h = h * 31 + Mathf.RoundToInt(p.y);
            h = h * 31 + Mathf.RoundToInt(p.z);
            return h;
        }
    }

    void Start()
    {
        if (Application.isPlaying) ComputeAnchors();
    }

    void OnDestroy()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
    }

    // ------------------------------------------------------------------ Construcción

    Vector3 Pos(int id) => vertexPos[id];

    // Separación media entre puntos de la fractura (≈ tamaño de un trozo), en unidades locales.
    float Spacing => size / Mathf.Max(1, detail);

    long FaceKey(int a, int b, int c)
    {
        // Ordenar los 3 ids para que la misma cara tenga la misma clave desde las dos piezas.
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        long m = vertexPos.Length;
        return (a * m + b) * m + c;
    }

    void Build()
    {
        pieces.Clear();
        faceOwners.Clear();

        // Puntos al azar del cubo y su triangulación en tetraedros.
        int s = EffectiveSeed();
        var pts = CrystalFracture.CubePoints(size, detail, s);
        var tets = CrystalFracture.Tetrahedralize(pts, s);
        vertexPos = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++) vertexPos[i] = new Vector3((float)pts[i].x, (float)pts[i].y, (float)pts[i].z);

        foreach (var tet in tets)
        {
            var piece = new Piece();
            for (int k = 0; k < 4; k++)
            {
                piece.v[k] = tet[k];
                piece.center += Pos(tet[k]) * 0.25f;
            }

            int index = pieces.Count;
            pieces.Add(piece);
            for (int f = 0; f < 4; f++)
            {
                long key = FaceKey(piece.v[Faces[f, 0]], piece.v[Faces[f, 1]], piece.v[Faces[f, 2]]);
                if (!faceOwners.TryGetValue(key, out var owners)) faceOwners[key] = owners = new List<int>(2);
                owners.Add(index);
            }
        }

        // Vecinas: piezas que comparten una cara.
        foreach (var owners in faceOwners.Values)
            if (owners.Count == 2)
            {
                pieces[owners[0]].neighbors.Add(owners[1]);
                pieces[owners[1]].neighbors.Add(owners[0]);
            }
    }

    /// <summary>Malla con las caras visibles: superficie del cubo y caras interiores que han quedado al aire.</summary>
    void RebuildMesh()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "Cubo de pirámides", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
        }
        meshCollider = GetComponent<MeshCollider>();
        meshCollider.convex = false; // necesario para saber qué pieza se ha golpeado

        var renderer = GetComponent<MeshRenderer>();
        bool interiorMaterial = renderer.sharedMaterials.Length >= 2 && renderer.sharedMaterials[1] != null;

        var verts = new List<Vector3>();
        var surface = new List<int>();
        var interior = new List<int>();
        var surfacePiece = new List<int>();
        var interiorPiece = new List<int>();

        for (int i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.released) continue;
            for (int f = 0; f < 4; f++)
            {
                int a = piece.v[Faces[f, 0]], b = piece.v[Faces[f, 1]], c = piece.v[Faces[f, 2]];
                var owners = faceOwners[FaceKey(a, b, c)];
                int other = owners.Count == 2 ? (owners[0] == i ? owners[1] : owners[0]) : -1;
                if (other >= 0 && !pieces[other].released) continue; // tapada por la vecina

                bool onSurface = other < 0;
                var list = onSurface || !interiorMaterial ? surface : interior;
                (onSurface || !interiorMaterial ? surfacePiece : interiorPiece).Add(i);
                AddFace(verts, list, Pos(a), Pos(b), Pos(c), piece.center);
            }
        }

        mesh.Clear();
        mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.subMeshCount = interiorMaterial ? 2 : 1;
        mesh.SetTriangles(surface, 0);
        if (interiorMaterial) mesh.SetTriangles(interior, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;

        // RaycastHit.triangleIndex recorre los submeshes en orden.
        triToPiece.Clear();
        triToPiece.AddRange(surfacePiece);
        triToPiece.AddRange(interiorPiece);

        meshCollider.sharedMesh = null;
        if (verts.Count > 0) meshCollider.sharedMesh = mesh;
    }

    /// <summary>Añade un triángulo con su cara delantera hacia fuera de la pieza (vértices propios: sombreado plano).</summary>
    static void AddFace(List<Vector3> verts, List<int> indices, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 pieceCenter)
    {
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f - pieceCenter) < 0f) (p1, p2) = (p2, p1);
        int start = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2);
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
    }

    // ------------------------------------------------------------------ Apoyos

    void ComputeAnchors()
    {
        float cellWorld = Spacing * MinAbs(transform.lossyScale);
        var mode = anchors;

        if (mode == AnchorMode.Embedded)
        {
            Physics.SyncTransforms();
            int count = 0;
            foreach (var piece in pieces)
            {
                var hits = Physics.OverlapSphere(transform.TransformPoint(piece.center), cellWorld * 0.35f, anchorLayers, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    if (h == meshCollider || h.GetComponentInParent<PyramidCube>() == this || h.GetComponent<FracturePiece>() != null) continue;
                    piece.anchor = true;
                    count++;
                    break;
                }
            }
            if (count == 0) mode = AnchorMode.Bottom; // no está clavado en nada
        }

        if (mode == AnchorMode.Bottom)
        {
            float minY = float.MaxValue;
            foreach (var piece in pieces) minY = Mathf.Min(minY, transform.TransformPoint(piece.center).y);
            foreach (var piece in pieces) piece.anchor = transform.TransformPoint(piece.center).y <= minY + cellWorld * 0.6f;
        }
        else if (mode == AnchorMode.None)
        {
            foreach (var piece in pieces) piece.anchor = true;
        }
    }

    // ------------------------------------------------------------------ Rotura

    /// <summary>Impacto de una bala (o de un clic) contra el cubo.</summary>
    /// <summary>
    /// Se rompe entero en sus pirámides, que salen despedidas hacia fuera (del centro del cubo y de
    /// <paramref name="desde"/>), caen y desaparecen. Son solo visuales (<see cref="FragmentoVisual"/>). Con
    /// <paramref name="maxPiezas"/> > 0 solo se muestran esas (al azar), para no cargar la escena.
    /// </summary>
    public void Fragmentar(Vector3 desde, float velocidad, float vida, int maxPiezas)
    {
        if (!gameObject.activeInHierarchy) return;
        var renderer = GetComponent<MeshRenderer>();
        bool interiorMaterial = renderer.sharedMaterials.Length >= 2 && renderer.sharedMaterials[1] != null;
        Vector3 centroCubo = renderer.bounds.center;

        var indices = new List<int>();
        for (int i = 0; i < pieces.Count; i++) if (!pieces[i].released) indices.Add(i);
        for (int i = 0; i < indices.Count; i++) { int k = Random.Range(i, indices.Count); (indices[i], indices[k]) = (indices[k], indices[i]); }
        int n = maxPiezas > 0 ? Mathf.Min(maxPiezas, indices.Count) : indices.Count;

        for (int j = 0; j < n; j++)
        {
            var piece = pieces[indices[j]];
            piece.released = true;
            Vector3 center = transform.TransformPoint(piece.center);
            var verts = new List<Vector3>();
            var surface = new List<int>();
            var interior = new List<int>();
            for (int f = 0; f < 4; f++)
            {
                int a = piece.v[Faces[f, 0]], b = piece.v[Faces[f, 1]], c = piece.v[Faces[f, 2]];
                bool onSurface = faceOwners[FaceKey(a, b, c)].Count == 1;
                Vector3 p0 = (transform.TransformPoint(Pos(a)) - center) * PieceShrink;
                Vector3 p1 = (transform.TransformPoint(Pos(b)) - center) * PieceShrink;
                Vector3 p2 = (transform.TransformPoint(Pos(c)) - center) * PieceShrink;
                AddFace(verts, onSurface || !interiorMaterial ? surface : interior, p0, p1, p2, Vector3.zero);
            }
            var m = new Mesh { name = "Fragmento" };
            m.SetVertices(verts);
            m.subMeshCount = interiorMaterial ? 2 : 1;
            m.SetTriangles(surface, 0);
            if (interiorMaterial) m.SetTriangles(interior, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();

            var go = new GameObject(name + " - fragmento");
            go.layer = gameObject.layer;
            go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = renderer.sharedMaterials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fv = go.AddComponent<FragmentoVisual>();
            Vector3 fuera = (center - centroCubo).normalized * 0.7f + (center - desde).normalized * 0.4f + Random.insideUnitSphere * 0.3f;
            fv.velocidad = fuera.normalized * velocidad * Random.Range(0.6f, 1.3f);
            fv.giro = Random.Range(180f, 540f);
            fv.vida = vida * Random.Range(0.8f, 1.2f);
        }
        gameObject.SetActive(false);
    }

    /// <summary>Piezas que aún no se han soltado.</summary>
    public int PiezasRestantes
    {
        get { int n = 0; foreach (var p in pieces) if (!p.released) n++; return n; }
    }

    public void Hit(RaycastHit hit, Vector3 direction)
    {
        if (!Application.isPlaying) return;
        direction = direction.normalized;

        int hitIndex = -1;
        if (hit.collider == meshCollider && hit.triangleIndex >= 0 && hit.triangleIndex < triToPiece.Count)
            hitIndex = triToPiece[hit.triangleIndex];
        if (hitIndex < 0 || pieces[hitIndex].released) hitIndex = NearestPiece(hit.point);
        if (hitIndex < 0) return;

        // Cada impacto suelta una sola pirámide: la golpeada. Sale por la cara que se ha golpeado.
        Release(hitIndex, hit.normal, direction, true);
        RebuildMesh();
        if (releaseUnsupported) StartCoroutine(ReleaseUnsupported());
    }

    int NearestPiece(Vector3 point)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].released) continue;
            float d = (transform.TransformPoint(pieces[i].center) - point).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    IEnumerator ReleaseUnsupported()
    {
        var supported = new bool[pieces.Count];
        var queue = new Queue<int>();
        for (int i = 0; i < pieces.Count; i++)
            if (!pieces[i].released && pieces[i].anchor) { supported[i] = true; queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int n in pieces[i].neighbors)
                if (!supported[n] && !pieces[n].released) { supported[n] = true; queue.Enqueue(n); }
        }

        // Caen de arriba abajo, una tras otra.
        var falling = new List<int>();
        for (int i = 0; i < pieces.Count; i++)
            if (!pieces[i].released && !supported[i]) falling.Add(i);
        falling.Sort((a, b) => transform.TransformPoint(pieces[b].center).y.CompareTo(transform.TransformPoint(pieces[a].center).y));

        foreach (int i in falling)
        {
            if (pieces[i].released) continue;
            Release(i, Vector3.zero, Vector3.zero, false);
            RebuildMesh();
            if (cascadeDelay > 0f) yield return new WaitForSeconds(cascadeDelay);
        }
    }

    /// <summary>
    /// Convierte la pieza en un objeto con física. Si <paramref name="pushed"/>, sale disparada hacia
    /// <paramref name="outward"/> (la normal de la cara golpeada, hacia fuera del bloque).
    /// </summary>
    void Release(int index, Vector3 outward, Vector3 direction, bool pushed)
    {
        var piece = pieces[index];
        if (piece.released) return;
        piece.released = true;

        // Malla en espacio de mundo (respeta escala no uniforme y rotación del cubo).
        Vector3 center = transform.TransformPoint(piece.center);
        var renderer = GetComponent<MeshRenderer>();
        bool interiorMaterial = renderer.sharedMaterials.Length >= 2 && renderer.sharedMaterials[1] != null;

        var verts = new List<Vector3>();
        var surface = new List<int>();
        var interior = new List<int>();
        float volume = 0f;
        for (int f = 0; f < 4; f++)
        {
            int a = piece.v[Faces[f, 0]], b = piece.v[Faces[f, 1]], c = piece.v[Faces[f, 2]];
            bool onSurface = faceOwners[FaceKey(a, b, c)].Count == 1;
            // Un poco más pequeña que su hueco, para que no nazca rozando a sus vecinas.
            Vector3 p0 = (transform.TransformPoint(Pos(a)) - center) * PieceShrink;
            Vector3 p1 = (transform.TransformPoint(Pos(b)) - center) * PieceShrink;
            Vector3 p2 = (transform.TransformPoint(Pos(c)) - center) * PieceShrink;
            AddFace(verts, onSurface || !interiorMaterial ? surface : interior, p0, p1, p2, Vector3.zero);
            int s = verts.Count;
            volume += Vector3.Dot(verts[s - 3], Vector3.Cross(verts[s - 2], verts[s - 1])) / 6f;
        }

        var m = new Mesh { name = "Pieza" };
        m.SetVertices(verts);
        m.subMeshCount = interiorMaterial ? 2 : 1;
        m.SetTriangles(surface, 0);
        if (interiorMaterial) m.SetTriangles(interior, 1);
        m.RecalculateNormals();
        m.RecalculateBounds();

        var go = new GameObject(name + " - pieza");
        go.layer = gameObject.layer;
        go.transform.position = center;
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = m;
        mc.convex = true;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = Mathf.Max(0.01f, Mathf.Abs(volume) * density);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (pushed)
        {
            // Hacia fuera de la cara golpeada (sale por el hueco), con algo de variación.
            Vector3 eject = outward.sqrMagnitude > 1e-8f ? outward.normalized : -direction;
            eject = (eject + Random.insideUnitSphere * 0.25f).normalized;
            rb.AddForce(eject * impulse, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * randomTorque, ForceMode.VelocityChange);
        }
        else
        {
            rb.AddTorque(Random.insideUnitSphere * randomTorque * 0.3f, ForceMode.VelocityChange);
        }

        var fp = go.AddComponent<FracturePiece>();
        fp.lifetime = pieceLifetime;
        // No choca con el bloque hasta haber salido de su hueco (si no, se queda encajada).
        fp.IgnoreUntilClear(mc, meshCollider);
    }

    const float PieceShrink = 0.96f;

    static float MinAbs(Vector3 v) => Mathf.Min(Mathf.Abs(v.x), Mathf.Min(Mathf.Abs(v.y), Mathf.Abs(v.z)));
}

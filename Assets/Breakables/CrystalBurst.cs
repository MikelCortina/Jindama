using UnityEngine;

/// <summary>
/// Estallido de cristal: muchos trocitos piramidales que salen disparados desde una pieza, giran y encogen
/// rápidamente hasta desaparecer. Solo visual (sin física ni colliders).
/// </summary>
public class CrystalBurst : MonoBehaviour
{
    static Mesh tetraMesh;

    Vector3 velocity;
    Vector3 spinAxis;
    float spinSpeed;
    float life, age;
    float gravity;
    Vector3 startScale;

    /// <summary>
    /// Rompe <paramref name="source"/> en <paramref name="count"/> trocitos con su material.
    /// <paramref name="speed"/> en unidades/s y <paramref name="lifetime"/> en segundos.
    /// </summary>
    public static void Spawn(Renderer source, Vector3 center, Vector3 outward, int count, float speed, float lifetime, float sizeFactor = 0.25f, Transform space = null)
    {
        if (source == null || count <= 0) return;

        // Tamaño de la pieza en el mundo (por su malla, para que no dependa de cómo esté girada).
        Mesh mesh = null;
        if (source is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
        else if (source.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
        Vector3 extents = mesh != null ? Vector3.Scale(mesh.bounds.extents, source.transform.lossyScale) : source.bounds.extents;
        float radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
        Spawn(source.sharedMaterial, source.gameObject.layer, center, outward, radius, count, speed, lifetime, sizeFactor, space);
    }

    /// <summary>
    /// Estallido de <paramref name="count"/> trocitos con <paramref name="material"/>, repartidos en una esfera de
    /// <paramref name="radius"/> alrededor de <paramref name="center"/> y lanzados hacia fuera (y hacia <paramref name="outward"/>).
    /// </summary>
    /// Con <paramref name="space"/>, los trocitos se simulan en su espacio local (van con él si se mueve, p.ej. el jugador).
    public static void Spawn(Material material, int layer, Vector3 center, Vector3 outward, float radius, int count, float speed, float lifetime, float sizeFactor = 0.25f, Transform space = null)
    {
        if (count <= 0 || radius <= 0f) return;
        if (tetraMesh == null) tetraMesh = BuildTetra();
        outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Fragmento de cristal");
            go.layer = layer;
            Vector3 offset = Random.insideUnitSphere * radius * 0.6f;
            go.transform.SetPositionAndRotation(center + offset, Random.rotation);
            // Trocitos de tamaños distintos y algo alargados.
            float s = radius * sizeFactor * Random.Range(0.5f, 1.2f);
            go.transform.localScale = new Vector3(s, s * Random.Range(0.7f, 1.6f), s);
            if (space != null) go.transform.SetParent(space, true); // conserva posición y tamaño en el mundo

            go.AddComponent<MeshFilter>().sharedMesh = tetraMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material; // uno solo: con más, Unity dibujaría el trocito varias veces
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var frag = go.AddComponent<CrystalBurst>();
            Vector3 dir = (offset.sqrMagnitude > 1e-8f ? offset.normalized : Random.onUnitSphere) + outward * 0.8f + Random.insideUnitSphere * 0.4f;
            frag.velocity = dir.normalized * speed * Random.Range(0.6f, 1.3f);
            frag.spinAxis = Random.onUnitSphere;
            frag.spinSpeed = Random.Range(360f, 1080f);
            frag.life = lifetime * Random.Range(0.7f, 1.2f);
            frag.gravity = speed * 1.5f;
            frag.startScale = go.transform.localScale;
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / life;
        if (t >= 1f) { Destroy(gameObject); return; }

        velocity *= Mathf.Exp(-3f * Time.deltaTime);  // se frenan un poco
        velocity += Vector3.down * (gravity * Time.deltaTime);
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        // Encogen rápido desde el principio.
        float k = 1f - t;
        transform.localScale = startScale * (k * k);
    }

    /// <summary>Tetraedro de lado ~1 centrado en el origen, con caras planas.</summary>
    static Mesh BuildTetra()
    {
        Vector3 a = new Vector3(0f, 0.8165f, 0f), b = new Vector3(-0.5f, -0.2722f, -0.2887f),
                c = new Vector3(0.5f, -0.2722f, -0.2887f), d = new Vector3(0f, -0.2722f, 0.5774f);
        var v = new[] { a, c, b, a, d, c, a, b, d, b, c, d };
        var mesh = new Mesh { name = "Fragmento" };
        mesh.vertices = v;
        mesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        // Normales suavizadas en UV1/UV2 (como AutoSmoothNormalsTool), para materiales de FlatKit con contorno.
        var uv = new System.Collections.Generic.List<Vector4>(v.Length);
        for (int i = 0; i < v.Length; i++) { var s = v[i].normalized; uv.Add(new Vector4(s.x, s.y, s.z, 0f)); }
        mesh.SetUVs(1, uv);
        mesh.SetUVs(2, uv);
        return mesh;
    }
}

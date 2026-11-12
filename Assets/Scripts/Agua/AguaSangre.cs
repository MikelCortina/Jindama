using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Va al objeto del agua (con el material del shader "Jindama/AguaSangre").
/// Guarda una máscara de sangre que cubre el agua vista desde arriba (en X-Z). Cada mancha que se pinta
/// (<see cref="Pintar"/>) se va extendiendo poco a poco, así que el agua pasa de azul a roja por zonas a medida que
/// avanza la partida. También lanza las ondas donde caen las gotas (<see cref="Onda"/>).
/// El agua es un plano horizontal: su altura es la del centro de su renderer.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class AguaSangre : MonoBehaviour
{
    /// <summary>Todas las aguas activas de la escena.</summary>
    public static readonly List<AguaSangre> Todas = new List<AguaSangre>();

    [Header("Máscara")]
    [Tooltip("Resolución de la máscara de sangre (más = manchas más finas).")]
    public int resolucion = 128;

    [Header("Expansión de la sangre")]
    [Tooltip("Cada cuántos segundos se extiende la sangre un paso.")]
    public float intervaloExpansion = 0.12f;
    [Tooltip("Cuánto llega a extenderse cada mancha (0.99 = poco, 0.999 = mucho).")]
    [Range(0.98f, 0.9995f)] public float expansion = 0.997f;

    [Header("Ondas")]
    [Tooltip("Multiplicador de la fuerza de las ondas de las gotas.")]
    public float fuerzaOnda = 1f;

    static readonly int idMascara = Shader.PropertyToID("_MascaraSangre");
    static readonly int idLimites = Shader.PropertyToID("_AguaLimites");
    static readonly int idOndas = Shader.PropertyToID("_Ondas");

    Renderer rend;
    MaterialPropertyBlock mpb;
    Texture2D textura;
    float[] mascara, auxiliar;
    byte[] bytes;
    Vector4 limites; // x, z mínimos y tamaño
    readonly Vector4[] ondas = new Vector4[8];
    int siguienteOnda;
    bool sucia, expandiendo;
    float relojExpansion;

    /// <summary>Altura (Y) de la superficie del agua.</summary>
    public float Altura { get; private set; }

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        resolucion = Mathf.Clamp(resolucion, 16, 512);
        mascara = new float[resolucion * resolucion];
        auxiliar = new float[mascara.Length];
        bytes = new byte[mascara.Length];
        textura = new Texture2D(resolucion, resolucion, TextureFormat.R8, false, true)
        {
            name = "MascaraSangre_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        textura.LoadRawTextureData(bytes);
        textura.Apply(false);
        for (int i = 0; i < ondas.Length; i++) ondas[i] = new Vector4(0, 0, -100f, 0);
        CalcularLimites();
        Subir();
    }

    void OnEnable() { if (!Todas.Contains(this)) Todas.Add(this); }
    void OnDisable() { Todas.Remove(this); }
    void OnDestroy() { if (textura != null) Destroy(textura); }

    void CalcularLimites()
    {
        Bounds b = rend.bounds;
        limites = new Vector4(b.min.x, b.min.z, Mathf.Max(0.01f, b.size.x), Mathf.Max(0.01f, b.size.z));
        Altura = b.center.y;
        transform.hasChanged = false;
    }

    /// <summary>¿Está el punto encima o debajo del agua (en X-Z)?</summary>
    public bool Contiene(Vector3 p) =>
        p.x >= limites.x && p.x <= limites.x + limites.z && p.z >= limites.y && p.z <= limites.y + limites.w;

    /// <summary>El punto de la superficie del agua más cercano a <paramref name="p"/>.</summary>
    public Vector3 PuntoMasCercano(Vector3 p) =>
        new Vector3(Mathf.Clamp(p.x, limites.x, limites.x + limites.z), Altura, Mathf.Clamp(p.z, limites.y, limites.y + limites.w));

    /// <summary>Pinta una mancha de sangre (radio en metros, fuerza 0..1).</summary>
    public void Pintar(Vector3 p, float radio, float fuerza)
    {
        float u = (p.x - limites.x) / limites.z, v = (p.z - limites.y) / limites.w;
        // Unas cuantas gotitas alrededor del centro para que la mancha no sea un círculo perfecto.
        int trozos = Random.Range(3, 6);
        for (int k = 0; k < trozos; k++)
        {
            Vector2 desp = k == 0 ? Vector2.zero : Random.insideUnitCircle * radio * 0.8f;
            float r = radio * (k == 0 ? 1f : Random.Range(0.35f, 0.7f));
            Mancha(u + desp.x / limites.z, v + desp.y / limites.w, r, fuerza);
        }
        sucia = expandiendo = true;
    }

    void Mancha(float u, float v, float radio, float fuerza)
    {
        int n = resolucion;
        float rx = radio / limites.z * n, ry = radio / limites.w * n;
        float cx = u * n - 0.5f, cy = v * n - 0.5f;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(cx + rx));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(cy + ry));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / Mathf.Max(0.5f, rx), dy = (y - cy) / Mathf.Max(0.5f, ry);
                float d2 = dx * dx + dy * dy;
                if (d2 > 1f) continue;
                int i = y * n + x;
                mascara[i] = Mathf.Min(1f, mascara[i] + fuerza * (1f - d2) * (1f - d2));
            }
    }

    /// <summary>Una onda que se abre desde el punto (donde cae una gota).</summary>
    public void Onda(Vector3 p, float fuerza)
    {
        ondas[siguienteOnda] = new Vector4(p.x, p.z, Time.timeSinceLevelLoad, fuerza * fuerzaOnda);
        siguienteOnda = (siguienteOnda + 1) % ondas.Length;
        sucia = true;
    }

    void Update()
    {
        if (transform.hasChanged) { CalcularLimites(); sucia = true; }

        if (expandiendo)
        {
            relojExpansion += Time.deltaTime;
            if (relojExpansion >= intervaloExpansion) { relojExpansion = 0f; Expandir(); }
        }
        if (sucia) Subir();
    }

    /// <summary>La sangre se extiende hacia los lados (cada mancha llega hasta cierto tamaño y se para).</summary>
    void Expandir()
    {
        int n = resolucion;
        float cambio = 0f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float a = mascara[i];
                float l = x > 0 ? mascara[i - 1] : 0f, r = x < n - 1 ? mascara[i + 1] : 0f;
                float d = y > 0 ? mascara[i - n] : 0f, u = y < n - 1 ? mascara[i + n] : 0f;
                float nuevo = Mathf.Max(a, (l + r + d + u) * 0.25f * expansion);
                if (nuevo < 0.02f) nuevo = 0f;
                auxiliar[i] = nuevo;
                cambio = Mathf.Max(cambio, nuevo - a);
            }
        var t = mascara; mascara = auxiliar; auxiliar = t;
        sucia = true;
        if (cambio < 0.003f) expandiendo = false;
    }

    void Subir()
    {
        for (int i = 0; i < mascara.Length; i++) bytes[i] = (byte)(mascara[i] * 255f);
        textura.LoadRawTextureData(bytes);
        textura.Apply(false);

        rend.GetPropertyBlock(mpb);
        mpb.SetTexture(idMascara, textura);
        mpb.SetVector(idLimites, limites);
        mpb.SetVectorArray(idOndas, ondas);
        rend.SetPropertyBlock(mpb);
        sucia = false;
    }

    /// <summary>Quita toda la sangre.</summary>
    [ContextMenu("Limpiar sangre")]
    public void Limpiar()
    {
        System.Array.Clear(mascara, 0, mascara.Length);
        expandiendo = false;
        sucia = true;
    }

    [ContextMenu("Probar: mancha en el centro")]
    void Probar()
    {
        Vector3 c = PuntoMasCercano(rend.bounds.center);
        Pintar(c, Mathf.Min(limites.z, limites.w) * 0.08f, 0.9f);
        Onda(c, 1f);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dibuja las balas que te quedan como iconos en la esquina superior derecha: un cristal por bala.
/// El icono es una "foto" de uno de los cristales laterales del arma (los que son las balas), hecha al empezar con una
/// cámara aparte, así que se parece siempre a los del arma aunque cambies su modelo o su material.
/// Las balas en el aire siguen contando: el icono solo desaparece cuando la bala se pierde de verdad
/// (<see cref="CrystalWeapon.BalaPerdida"/>: falla, te golpean o estallan todas). Al perderla salta, se pone roja y cae.
/// Con una sola bala, el último icono late en rojo.
/// Lo añade <see cref="Arranque"/> solo si no está en la escena.
/// </summary>
public class HUDBalas : MonoBehaviour
{
    [Tooltip("Imagen de cada bala. Vacío = una foto del cristal lateral del arma.")]
    public Texture2D icono;
    [Tooltip("Resolución de la foto del cristal.")]
    public int resolucionIcono = 256;
    [Tooltip("Inclinación del cristal en la foto (grados, para que se vea en 3D).")]
    public Vector3 giroFoto = new Vector3(15f, 30f, -12f);
    [Tooltip("Multiplica el brillo de la foto (si la escena es muy oscura).")]
    public float brilloFoto = 1.3f;
    [Tooltip("Iconos totalmente blancos (solo la silueta del cristal).")]
    public bool blancos = true;

    [Header("Colocación")]
    [Tooltip("Alto de cada icono en píxeles (para una pantalla de 1080 de alto).")]
    public float tamano = 140f;
    [Tooltip("Espacio entre iconos (px a 1080).")]
    public float separacion = 5f;
    [Tooltip("Distancia a los bordes de la pantalla (px a 1080).")]
    public float margen = 24f;

    [Header("Color y animación")]
    public Color color = Color.white;
    public Color colorPeligro = new Color(1f, 0.25f, 0.2f);
    [Tooltip("Dibujar también los huecos de las balas que faltan (muy transparentes).")]
    public bool mostrarHuecos = false;
    [Tooltip("Segundos que dura la animación de perder una bala.")]
    public float duracionPerdida = 0.6f;

    public static void Auto()
    {
        if (FindAnyObjectByType<HUDBalas>() == null) new GameObject("HUDBalas").AddComponent<HUDBalas>();
    }

    struct Perdida { public int hueco; public float t; public float giro; }

    CrystalWeapon arma;
    Texture2D foto;
    int previas = -1;
    readonly List<Perdida> perdidas = new List<Perdida>();
    readonly Dictionary<int, float> apariciones = new Dictionary<int, float>(); // hueco → segundos desde que apareció
    float golpe; // destello rojo de todos los iconos al perder una bala

    Texture2D Icono => icono != null ? icono : foto;

    void Start() => Buscar();

    void Buscar()
    {
        if (arma != null) return;
        arma = FindAnyObjectByType<CrystalWeapon>();
        if (arma == null) return;
        previas = arma.BalasVisibles;
        arma.BalaPerdida += AlPerder;
        if (icono == null && foto == null)
        {
            try { foto = FotoDelCristal(); } catch (System.Exception e) { Debug.LogWarning("HUDBalas: no se pudo hacer la foto del cristal: " + e.Message); }
            if (foto == null) foto = IconoPorCodigo();
            if (blancos) EnBlanco(foto);
        }
    }

    void OnDestroy()
    {
        if (arma != null) arma.BalaPerdida -= AlPerder;
        if (foto != null) Destroy(foto);
    }

    void AlPerder(int cuantas) => golpe = 1f;

    void Update()
    {
        Buscar();
        if (arma == null) return;
        float dt = Time.unscaledDeltaTime;

        int ahora = arma.BalasVisibles;
        if (ahora < previas)
            for (int i = ahora; i < previas; i++) perdidas.Add(new Perdida { hueco = i, t = 0f, giro = Random.Range(-50f, 50f) });
        else if (ahora > previas)
            for (int i = Mathf.Max(0, previas); i < ahora; i++) apariciones[i] = 0f;
        previas = ahora;

        for (int i = perdidas.Count - 1; i >= 0; i--)
        {
            var p = perdidas[i];
            p.t += dt;
            if (p.t >= duracionPerdida) perdidas.RemoveAt(i); else perdidas[i] = p;
        }
        if (apariciones.Count > 0)
        {
            var claves = new List<int>(apariciones.Keys);
            foreach (var k in claves) { apariciones[k] += dt; if (apariciones[k] > 0.3f) apariciones.Remove(k); }
        }
        golpe = Mathf.Max(0f, golpe - dt * 2.5f);
    }

    // ------------------------------------------------------------------ Dibujo

    /// <summary>Rectángulo del hueco i (el 0 es el de más a la derecha).</summary>
    Rect Hueco(int i, float esc)
    {
        var tex = Icono;
        float alto = tamano * esc;
        float ancho = tex != null ? alto * tex.width / Mathf.Max(1f, tex.height) : alto;
        float x = Screen.width - margen * esc - ancho - i * (ancho + separacion * esc);
        return new Rect(x, margen * esc, ancho, alto);
    }

    void OnGUI()
    {
        var tex = Icono;
        if (arma == null || tex == null || Event.current.type != EventType.Repaint) return;
        GUI.depth = 1; // por debajo del fundido / pantalla de derrota de Partida
        float esc = Screen.height / 1080f;
        var colorPrevio = GUI.color;
        var matrizPrevia = GUI.matrix;
        int balas = arma.BalasVisibles;

        if (mostrarHuecos)
            for (int i = balas; i < arma.MaxBalas; i++) Dibujar(tex, Hueco(i, esc), 1f, 0f, new Color(color.r, color.g, color.b, 0.15f), false);

        bool peligro = balas == 1;
        float latido = peligro ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;
        float t = Time.unscaledTime;
        for (int i = 0; i < balas; i++)
        {
            Rect r = Hueco(i, esc);
            r.y += Mathf.Sin(t * 2f + i * 0.9f) * 2f * esc; // flotan un poco, como en el arma
            float escala = 1f;
            if (apariciones.TryGetValue(i, out float ta))
            {
                float k = Mathf.Clamp01(ta / 0.3f);
                escala = k < 0.6f ? Mathf.Lerp(0.2f, 1.25f, k / 0.6f) : Mathf.Lerp(1.25f, 1f, (k - 0.6f) / 0.4f);
            }
            if (peligro) escala *= 1f + 0.08f * latido;
            Color c = Color.Lerp(color, colorPeligro, Mathf.Max(golpe * 0.8f, latido * 0.7f));
            Dibujar(tex, r, escala, 0f, c, true);
        }

        // Balas que se acaban de perder: saltan, giran, se ponen rojas y caen desvaneciéndose.
        foreach (var p in perdidas)
        {
            float k = Mathf.Clamp01(p.t / duracionPerdida);
            Rect r = Hueco(p.hueco, esc);
            r.y += (-0.3f * Mathf.Sin(k * Mathf.PI) + 1.3f * k * k) * r.height;
            Color c = Color.Lerp(Color.white, colorPeligro, Mathf.Clamp01(k * 4f));
            c.a = 1f - k * k;
            Dibujar(tex, r, Mathf.Lerp(1.3f, 0.6f, k), p.giro * k, c, true);
        }

        GUI.matrix = matrizPrevia;
        GUI.color = colorPrevio;
    }

    static void Dibujar(Texture tex, Rect r, float escala, float angulo, Color c, bool sombra)
    {
        Vector2 centro = r.center;
        r.size *= escala;
        r.center = centro;
        var m = GUI.matrix;
        if (Mathf.Abs(angulo) > 0.01f) GUIUtility.RotateAroundPivot(angulo, centro);
        if (sombra)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.4f * c.a);
            var s = r; s.x += r.width * 0.05f; s.y += r.height * 0.04f;
            GUI.DrawTexture(s, tex);
        }
        GUI.color = c;
        GUI.DrawTexture(r, tex);
        GUI.matrix = m;
    }

    // ------------------------------------------------------------------ Foto del cristal

    /// <summary>
    /// Copia un cristal lateral del arma lejos de todo, lo pone de pie y le hace una foto con una cámara propia.
    /// Se hace dos veces (fondo negro y fondo blanco) para sacar la transparencia sin depender del canal alfa.
    /// </summary>
    Texture2D FotoDelCristal()
    {
        Transform pieza = arma.sides.Count > 0 ? arma.sides[0] : arma.tip;
        if (pieza == null) return null;
        int capa = CapaLibre();
        if (capa < 0) return null;

        var raiz = new GameObject("FotoBala") { hideFlags = HideFlags.HideAndDontSave };
        Vector3 lugar = new Vector3(0f, -5000f, 0f);
        raiz.transform.position = lugar;
        RenderTexture rt = null;
        try
        {
            // Copia del cristal, sin scripts ni colliders.
            var copia = Instantiate(pieza.gameObject, pieza.position, pieza.rotation);
            copia.transform.localScale = pieza.lossyScale;
            copia.SetActive(true);
            foreach (var c in copia.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var b in copia.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
            foreach (var smr in copia.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // De pie: su eje más largo hacia arriba, con la punta hacia arriba en la foto.
            Vector3 eje = EjeLargo(pieza, arma);
            copia.transform.rotation = Quaternion.Euler(giroFoto) * Quaternion.FromToRotation(eje, Vector3.up) * copia.transform.rotation;
            copia.transform.SetParent(raiz.transform, true);
            Capa(copia, capa);

            Bounds b2 = Limites(copia);
            copia.transform.position += lugar - b2.center;
            b2.center = lugar;
            float media = Mathf.Max(b2.extents.y, b2.extents.x * 2f) * 1.12f;

            // Luz propia (aquí abajo no hay nada más).
            var luz = new GameObject("Luz").AddComponent<Light>();
            luz.transform.SetParent(raiz.transform, false);
            luz.transform.position = lugar + new Vector3(-1f, 1.5f, -2f) * media * 2f;
            luz.type = LightType.Point;
            luz.range = media * 12f;
            luz.intensity = 3f;

            // Cámara ortográfica.
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.transform.SetParent(raiz.transform, false);
            cam.transform.position = lugar - Vector3.forward * media * 6f;
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = media;
            cam.aspect = 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = media * 12f;
            cam.cullingMask = 1 << capa;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.enabled = false;
            cam.cameraType = CameraType.Reflection; // sin los efectos a pantalla completa (píxeles, paleta…)
            var datos = cam.GetUniversalAdditionalCameraData();
            datos.renderPostProcessing = false;
            datos.renderShadows = false;
            datos.antialiasing = AntialiasingMode.None;

            int alto = Mathf.Clamp(resolucionIcono, 32, 1024), ancho = alto / 2;
            rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            Color[] negro = Foto(cam, rt, Color.black);
            Color[] blanco = Foto(cam, rt, Color.white);

            var res = new Texture2D(ancho, alto, TextureFormat.RGBA32, false) { name = "IconoBala (foto)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[negro.Length];
            for (int i = 0; i < px.Length; i++)
            {
                Color n = negro[i], w = blanco[i];
                float a = Mathf.Clamp01(1f - ((w.r - n.r) + (w.g - n.g) + (w.b - n.b)) / 3f);
                px[i] = a > 0.004f
                    ? new Color(Mathf.Clamp01(n.r / a * brilloFoto), Mathf.Clamp01(n.g / a * brilloFoto), Mathf.Clamp01(n.b / a * brilloFoto), a)
                    : new Color(0f, 0f, 0f, 0f);
            }
            res.SetPixels(px);
            res.Apply();
            return res;
        }
        finally
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            Destroy(raiz);
        }
    }

    static Color[] Foto(Camera cam, RenderTexture rt, Color fondo)
    {
        cam.backgroundColor = fondo;
        var peticion = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, peticion)) RenderPipeline.SubmitRenderRequest(cam, peticion);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

        var lectura = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        var previa = RenderTexture.active;
        RenderTexture.active = rt;
        lectura.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        lectura.Apply();
        RenderTexture.active = previa;
        var px = lectura.GetPixels();
        Destroy(lectura);
        return px;
    }

    /// <summary>
    /// Dirección (en el mundo) de la punta de la pieza: del centro de sus vértices al vértice más alejado.
    /// Si la malla no se puede leer, hacia fuera del arma.
    /// </summary>
    static Vector3 EjeLargo(Transform pieza, CrystalWeapon arma)
    {
        Vector3 haciaFuera = CrystalWeapon.MeshCenter(pieza) - arma.transform.position;
        Mesh malla = null;
        Transform t = pieza;
        var mf = pieza.GetComponentInChildren<MeshFilter>(true);
        if (mf != null) { malla = mf.sharedMesh; t = mf.transform; }
        if (malla == null) { var smr = pieza.GetComponentInChildren<SkinnedMeshRenderer>(true); if (smr != null) { malla = smr.sharedMesh; t = smr.transform; } }
        if (malla == null || !malla.isReadable)
            return haciaFuera.sqrMagnitude > 1e-8f ? haciaFuera.normalized : pieza.up;

        var v = malla.vertices;
        if (v.Length == 0) return pieza.up;
        Vector3 media = Vector3.zero;
        foreach (var p in v) media += t.TransformPoint(p);
        media /= v.Length;
        Vector3 lejos = media;
        float max = 0f;
        foreach (var p in v) { Vector3 w = t.TransformPoint(p); float d = (w - media).sqrMagnitude; if (d > max) { max = d; lejos = w; } }
        Vector3 dir = lejos - media;
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : pieza.up;
    }

    static Bounds Limites(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static void Capa(GameObject go, int capa)
    {
        go.layer = capa;
        foreach (Transform h in go.transform) Capa(h.gameObject, capa);
    }

    /// <summary>Deja la textura en blanco puro conservando su transparencia (la silueta).</summary>
    static void EnBlanco(Texture2D t)
    {
        var px = t.GetPixels();
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, Mathf.Clamp01(px[i].a * 3f)) /* opaco aunque el cristal sea translúcido */;
        t.SetPixels(px);
        t.Apply();
    }

    /// <summary>Una capa sin nombre (sin usar) para la foto.</summary>
    static int CapaLibre()
    {
        for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return -1;
    }

    /// <summary>Por si no se puede hacer la foto: un cristal alargado suave con caras.</summary>
    static Texture2D IconoPorCodigo()
    {
        const int w = 64, h = 128;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        Color claro = new Color(0.8f, 0.97f, 1f), medio = new Color(0.4f, 0.78f, 0.92f), oscuro = new Color(0.12f, 0.4f, 0.62f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;          // -1..1
                float v = (y + 0.5f) / h;                    // 0 abajo, 1 arriba
                float ancho = v > 0.25f ? Mathf.Lerp(0.9f, 0f, (v - 0.25f) / 0.75f) : Mathf.Lerp(0.15f, 0.9f, v / 0.25f);
                float d = Mathf.Abs(u) - ancho;
                float a = Mathf.Clamp01(-d * w * 0.5f);      // borde suavizado
                if (a <= 0f) { px[y * w + x] = Color.clear; continue; }
                Color c = u < -0.15f * ancho ? claro : (u < 0.35f * ancho ? medio : oscuro);
                c = Color.Lerp(c, Color.white, Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u + 0.3f * ancho) * 8f), 2f) * 0.6f);
                c.a = a * 0.95f;
                px[y * w + x] = c;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }
}

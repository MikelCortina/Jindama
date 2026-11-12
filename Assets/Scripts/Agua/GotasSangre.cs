using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cuando pierdes una bala de verdad (<see cref="CrystalWeapon.BalaPerdida"/>: un disparo que no le da a nadie, un
/// golpe o que estallen todas), el jugador suelta gotas de sangre. Disparar y acertar no sangra.
/// Las gotas caen con gravedad: si caen al agua (<see cref="AguaSangre"/>) la manchan y hacen ondas; si caen al suelo
/// dejan una manchita que se desvanece. Además (opcional) cada golpe mancha el agua más cercana, para que el agua se
/// vaya poniendo roja durante la partida aunque no estés encima de ella.
/// Lo añade <see cref="Arranque"/> solo si no está en la escena.
/// </summary>
public class GotasSangre : MonoBehaviour
{
    /// <summary>Desactivado: ya no se suelta sangre al perder una bala. Para volver a activarlo, pon true.</summary>
    public static bool activado = false;

    public static void Auto()
    {
        if (!activado) return;
        if (FindAnyObjectByType<GotasSangre>() == null) new GameObject("GotasSangre").AddComponent<GotasSangre>();
    }

    [Header("Gotas")]
    public int gotasPorBala = 14;
    public float velocidad = 3.5f;
    public float tamano = 0.05f;
    public float gravedad = 14f;
    public Color color = new Color(0.6f, 0.02f, 0.03f);
    [Tooltip("Material de las gotas y manchas (vacío = uno sin luz del color de arriba).")]
    public Material material;

    [Header("Al caer al agua")]
    public float radioMancha = 0.45f;
    [Range(0f, 1f)] public float fuerzaMancha = 0.7f;

    [Header("Mancha en el agua más cercana")]
    [Tooltip("Cada golpe mancha también el agua más cercana al jugador (la sangre 'llega' al agua).")]
    public bool mancharAguaCercana = true;
    [Tooltip("Solo si el agua está a menos de esta distancia (en horizontal).")]
    public float distanciaMaxima = 60f;
    public float radioManchaCercana = 1.2f;
    [Tooltip("Segundos hasta que aparece esa mancha (para que coincida con las gotas cayendo).")]
    public float retraso = 0.5f;

    [Header("Manchas en el suelo")]
    public bool manchasEnSuelo = true;
    public float duracionManchaSuelo = 10f;

    class Gota { public Transform t; public Vector3 pos, vel; public float vida, tam; }
    class ManchaSuelo { public Transform t; public float vida; public float tam; }

    const int MaxGotas = 80, MaxManchas = 40;
    readonly List<Gota> gotas = new List<Gota>();
    readonly Stack<Gota> libresGotas = new Stack<Gota>();
    readonly List<ManchaSuelo> manchas = new List<ManchaSuelo>();
    readonly RaycastHit[] golpes = new RaycastHit[8];

    CrystalWeapon arma;
    FirstPersonController jugador;
    Material mat;

    void Start()
    {
        mat = material != null ? material : CrearMaterial(color);
        Buscar();
    }

    void Buscar()
    {
        if (arma == null)
        {
            arma = FindAnyObjectByType<CrystalWeapon>();
            if (arma != null) arma.BalaPerdida += Sangrar;
        }
        if (jugador == null) jugador = FindAnyObjectByType<FirstPersonController>();
    }

    void OnDestroy()
    {
        if (arma != null) arma.BalaPerdida -= Sangrar;
        if (material == null && mat != null) Destroy(mat);
    }

    /// <summary>Suelta la sangre de <paramref name="balas"/> balas perdidas.</summary>
    public void Sangrar(int balas)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Transform c = cam.transform;
        Vector3 origen = c.position + c.forward * 0.35f - c.up * 0.25f;
        Vector3 velJugador = jugador != null ? jugador.Velocity : Vector3.zero;

        int n = Mathf.Min(gotasPorBala * Mathf.Max(1, balas), 60);
        for (int k = 0; k < n; k++)
        {
            Vector3 dir = (c.forward * 0.6f + Random.insideUnitSphere * 0.9f + Vector3.up * 0.5f).normalized;
            Lanzar(origen + Random.insideUnitSphere * 0.08f, dir * velocidad * Random.Range(0.4f, 1.2f) + velJugador * 0.8f,
                   tamano * Random.Range(0.6f, 1.4f));
        }

        if (mancharAguaCercana) StartCoroutine(MancharCercana(balas));
    }

    IEnumerator MancharCercana(int balas)
    {
        yield return new WaitForSeconds(retraso);
        if (jugador == null) yield break;
        Vector3 p = jugador.transform.position;
        AguaSangre mejor = null;
        float mejorDist = distanciaMaxima;
        foreach (var a in AguaSangre.Todas)
        {
            Vector3 q = a.PuntoMasCercano(p);
            float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(p.x, p.z));
            if (d <= mejorDist) { mejorDist = d; mejor = a; }
        }
        if (mejor == null) yield break;
        Vector2 desp = Random.insideUnitCircle * 1.5f;
        Vector3 punto = mejor.PuntoMasCercano(p + new Vector3(desp.x, 0f, desp.y));
        mejor.Pintar(punto, radioManchaCercana * Mathf.Sqrt(Mathf.Max(1, balas)), fuerzaMancha);
        mejor.Onda(punto, 1f);
    }

    void Lanzar(Vector3 pos, Vector3 vel, float tam)
    {
        if (gotas.Count >= MaxGotas) return;
        Gota g;
        if (libresGotas.Count > 0) g = libresGotas.Pop();
        else g = new Gota { t = Crear(PrimitiveType.Cube, "Gota").transform };
        g.pos = pos; g.vel = vel; g.vida = 0f; g.tam = tam;
        g.t.gameObject.SetActive(true);
        g.t.position = pos;
        gotas.Add(g);
    }

    GameObject Crear(PrimitiveType tipo, string nombre)
    {
        var go = GameObject.CreatePrimitive(tipo);
        go.name = nombre;
        Destroy(go.GetComponent<Collider>());
        go.layer = 2; // Ignore Raycast
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.SetParent(transform, true);
        return go;
    }

    void Update()
    {
        Buscar();
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = gotas.Count - 1; i >= 0; i--)
        {
            var g = gotas[i];
            g.vida += dt;
            Vector3 antes = g.pos;
            g.vel.y -= gravedad * dt;
            g.pos += g.vel * dt;

            if (g.vida > 5f || Impacto(antes, g)) { Quitar(i); continue; }

            g.t.position = g.pos;
            if (g.vel.sqrMagnitude > 1e-4f) g.t.rotation = Quaternion.LookRotation(g.vel);
            g.t.localScale = new Vector3(g.tam, g.tam, g.tam * (1f + g.vel.magnitude * 0.12f));
        }

        for (int i = manchas.Count - 1; i >= 0; i--)
        {
            var m = manchas[i];
            m.vida += dt;
            float k = Mathf.Clamp01((m.vida - duracionManchaSuelo * 0.7f) / (duracionManchaSuelo * 0.3f));
            m.t.localScale = new Vector3(m.tam, m.tam, 1f) * (1f - k);
            if (m.vida >= duracionManchaSuelo) { Destroy(m.t.gameObject); manchas.RemoveAt(i); }
        }
    }

    /// <summary>¿Ha chocado la gota entre <paramref name="antes"/> y su posición nueva? Si es así, mancha.</summary>
    bool Impacto(Vector3 antes, Gota g)
    {
        Vector3 ahora = g.pos;

        // Agua (aunque no tenga collider).
        foreach (var a in AguaSangre.Todas)
        {
            float h = a.Altura;
            if (antes.y >= h && ahora.y < h)
            {
                float k = (antes.y - h) / Mathf.Max(1e-5f, antes.y - ahora.y);
                Vector3 p = Vector3.Lerp(antes, ahora, k);
                if (!a.Contiene(p)) continue;
                if (Physics.Linecast(antes, p, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && ChoqueSolido(antes, p, out _)) break;
                a.Pintar(p, radioMancha * Random.Range(0.6f, 1.2f), fuerzaMancha);
                a.Onda(p, 0.6f);
                return true;
            }
        }

        // Suelo / paredes.
        if (ChoqueSolido(antes, ahora, out RaycastHit golpe))
        {
            var agua = golpe.collider.GetComponentInParent<AguaSangre>();
            if (agua != null) { agua.Pintar(golpe.point, radioMancha, fuerzaMancha); agua.Onda(golpe.point, 0.6f); }
            else if (manchasEnSuelo) ManchaEnSuelo(golpe.point, golpe.normal, g.tam);
            return true;
        }
        return false;
    }

    bool ChoqueSolido(Vector3 a, Vector3 b, out RaycastHit mejor)
    {
        mejor = default;
        Vector3 d = b - a;
        float dist = d.magnitude;
        if (dist < 1e-5f) return false;
        int n = Physics.RaycastNonAlloc(a, d / dist, golpes, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float min = float.MaxValue;
        bool hay = false;
        for (int i = 0; i < n; i++)
        {
            var h = golpes[i];
            if (jugador != null && h.collider.transform.IsChildOf(jugador.transform)) continue;
            if (h.distance < min) { min = h.distance; mejor = h; hay = true; }
        }
        return hay;
    }

    void ManchaEnSuelo(Vector3 p, Vector3 normal, float tamGota)
    {
        if (manchas.Count >= MaxManchas) { Destroy(manchas[0].t.gameObject); manchas.RemoveAt(0); }
        var go = Crear(PrimitiveType.Quad, "ManchaSangre");
        go.transform.SetPositionAndRotation(p + normal * 0.01f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        float tam = tamGota * Random.Range(2f, 4f);
        go.transform.localScale = new Vector3(tam, tam, 1f);
        manchas.Add(new ManchaSuelo { t = go.transform, vida = 0f, tam = tam });
    }

    void Quitar(int i)
    {
        var g = gotas[i];
        g.t.gameObject.SetActive(false);
        gotas.RemoveAt(i);
        libresGotas.Push(g);
    }

    static Material CrearMaterial(Color c)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Sprites/Default");
        var m = new Material(s) { name = "Sangre (auto)", color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        return m;
    }
}

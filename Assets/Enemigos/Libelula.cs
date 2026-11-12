using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemigo libélula, montado por código (no necesita modelo):
/// - Cuerpo alargado facetado con cabeza y cola, y 4 alas (dos a cada lado) que aletean rápido, las de delante y las de
///   detrás desfasadas.
/// - Entra volando por una ventana y va a por ti por el aire: revolotea cerca, meciéndose y esquivando paredes, y cuando
///   te tiene a tiro se lanza contra ti. Si te da, te quita velocidad base máxima (y balas, si así está configurado) y
///   estalla; si falla, se frena, vuelve a revolotear y lo intenta otra vez.
/// - Un disparo la rompe.
/// Todo se ajusta en <see cref="ConfigLibelulas"/> (Tools > Libélulas > Configuración). La crea <see cref="GeneradorLibelulas"/>.
/// </summary>
public class Libelula : MonoBehaviour
{
    public static readonly List<Libelula> Todas = new List<Libelula>();

    [Tooltip("Configuración (vacía = la del proyecto).")]
    public ConfigLibelulas config;

    enum Estado { Entrando, Revoloteando, Atacando, Frenando }

    Estado estado;
    Vector3 velocidad;
    Vector3 puntoDentro;
    float tiempoEstado, esperaAtaque, fase;
    float ladoRevoloteo;
    int vida;

    Transform jugador;
    Camera camJugador;
    CharacterController cc;
    CrystalWeapon arma;

    Transform cuerpo, cabeza, cola;
    Renderer cuerpoRenderer;
    readonly List<(Transform t, float lado, float delante)> alas = new List<(Transform, float, float)>();

    Material matCuerpoBase, matAlasBase;

    static readonly RaycastHit[] hits = new RaycastHit[16];

    ConfigLibelulas C => config != null ? config : (config = ConfigLibelulas.Cargar());
    float E => Mathf.Max(0.01f, C.escala);

    // ------------------------------------------------------------------ Creación

    /// <summary>Crea una libélula fuera de la ventana (<paramref name="inicio"/>) que entra hasta <paramref name="dentro"/>.</summary>
    public static Libelula Crear(Vector3 inicio, Vector3 dentro, ConfigLibelulas cfg, Material matCuerpo, Material matAlas)
    {
        var go = new GameObject("Libélula");
        go.transform.position = inicio;
        var l = go.AddComponent<Libelula>();
        l.config = cfg;
        l.puntoDentro = dentro;
        Vector3 d = dentro - inicio;
        go.transform.rotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized) : Quaternion.identity;
        l.Montar(matCuerpo, matAlas);
        return l;
    }

    void OnEnable() => Todas.Add(this);
    void OnDisable() => Todas.Remove(this);

    Material MatCuerpo => C.materialCuerpo != null ? C.materialCuerpo : matCuerpoBase;
    Material MatAlas => C.materialAlas != null ? C.materialAlas : (matAlasBase != null ? matAlasBase : MatCuerpo);

    void Montar(Material matCuerpo, Material matAlas)
    {
        matCuerpoBase = matCuerpo;
        matAlasBase = matAlas;
        var fps = FindAnyObjectByType<FirstPersonController>();
        if (fps != null)
        {
            jugador = fps.transform;
            camJugador = fps.cam != null ? fps.cam : fps.GetComponentInChildren<Camera>();
            cc = fps.GetComponent<CharacterController>();
        }
        arma = FindAnyObjectByType<CrystalWeapon>();
        vida = C.vida;
        fase = Random.value * 10f;
        ladoRevoloteo = Random.value < 0.5f ? -1f : 1f;

        float l = C.largoCuerpo * E, g = C.grosorCuerpo * E;
        // Cuerpo: tórax facetado; delante la cabeza y detrás una cola larga y fina.
        cuerpo = Pieza("Cuerpo", Arana.MallaFacetada(new Vector3(0.5f, 0.5f, 0.5f)), MatCuerpo);
        cuerpo.localScale = new Vector3(g, g, l * 0.7f);
        cuerpo.localPosition = new Vector3(0f, 0f, l * 0.15f);
        cuerpoRenderer = cuerpo.GetComponent<Renderer>();
        cabeza = Pieza("Cabeza", Arana.MallaFacetada(new Vector3(0.6f, 0.5f, 0.5f)), MatCuerpo);
        cabeza.localScale = Vector3.one * g * 1.2f;
        cabeza.localPosition = new Vector3(0f, 0f, l * 0.55f);
        cola = Pieza("Cola", Arana.MallaFacetada(new Vector3(0.3f, 0.3f, 0.5f)), MatCuerpo);
        cola.localScale = new Vector3(g * 0.8f, g * 0.8f, l * 1.1f);
        cola.localPosition = new Vector3(0f, 0f, -l * 0.55f);

        // 4 alas: dos a cada lado (delante y detrás), barras planas que salen del tórax.
        for (int lado = -1; lado <= 1; lado += 2)
            for (int k = 0; k < 2; k++)
            {
                var ala = Pieza("Ala", Arana.CuboContorno(), MatAlas);
                alas.Add((ala, lado, k == 0 ? 1f : -1f));
            }

        // Colisión: para que la rompan los disparos.
        var sc = gameObject.AddComponent<SphereCollider>();
        sc.radius = Mathf.Max(g, l * 0.4f);
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        ColocarAlas();
        estado = Estado.Entrando;
    }

    Transform Pieza(string nombre, Mesh malla, Material mat)
    {
        var go = new GameObject(nombre);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var r = go.AddComponent<MeshRenderer>();
        if (mat != null) r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // ------------------------------------------------------------------ Bucle

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        tiempoEstado += dt;
        esperaAtaque -= dt;
        fase += dt;
        Vector3 yo = CuerpoJugador();

        switch (estado)
        {
            case Estado.Entrando:
                Volar(puntoDentro, C.velocidad, dt);
                if (Vector3.Distance(transform.position, puntoDentro) < 0.1f * E || tiempoEstado > 4f) CambiarEstado(Estado.Revoloteando);
                break;

            case Estado.Revoloteando:
            {
                // Revolotea a un lado de ti, algo por encima, meciéndose.
                Vector3 haciaMi = transform.position - yo;
                haciaMi.y = 0f;
                if (haciaMi.sqrMagnitude < 1e-4f) haciaMi = Vector3.forward;
                Vector3 lado = Vector3.Cross(Vector3.up, haciaMi.normalized) * ladoRevoloteo;
                Vector3 objetivo = yo + (haciaMi.normalized + lado * 0.6f).normalized * C.distanciaRevoloteo * E
                                   + Vector3.up * (C.alturaSobreJugador * E + Mathf.Sin(fase * 2.3f) * C.balanceo * E);
                Volar(objetivo, C.velocidad, dt, yo);
                if (tiempoEstado > 3f) { ladoRevoloteo = -ladoRevoloteo; tiempoEstado = 0f; } // cambia de lado de vez en cuando
                if (esperaAtaque <= 0f && Vector3.Distance(transform.position, yo) < C.alcanceAtaque * E && HayVision(yo))
                {
                    velocidad = (yo - transform.position).normalized * C.velocidadAtaque * E;
                    CambiarEstado(Estado.Atacando);
                }
                break;
            }

            case Estado.Atacando:
                // Se lanza recto contra ti.
                transform.position += velocidad * dt;
                if (velocidad.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocidad), 1f - Mathf.Exp(-20f * dt));
                if (TeHaDado()) { Golpear(); return; }
                if (tiempoEstado > C.duracionAtaque || ChocaConAlgo(velocidad * dt)) { esperaAtaque = C.esperaTrasAtaque; CambiarEstado(Estado.Frenando); }
                break;

            case Estado.Frenando:
                velocidad = Vector3.Lerp(velocidad, Vector3.zero, 1f - Mathf.Exp(-4f * dt));
                if (!ChocaConAlgo(velocidad * dt)) transform.position += velocidad * dt;
                if (tiempoEstado > 0.5f) CambiarEstado(Estado.Revoloteando);
                break;
        }

        ColocarAlas();
        AplicarMateriales();
    }

    void CambiarEstado(Estado e) { estado = e; tiempoEstado = 0f; }

    /// <summary>Vuela hacia <paramref name="objetivo"/> girando poco a poco y esquivando paredes; mira hacia <paramref name="mirar"/> si se da.</summary>
    void Volar(Vector3 objetivo, float rapidez, float dt, Vector3? mirar = null)
    {
        Vector3 d = objetivo - transform.position;
        float dist = d.magnitude;
        Vector3 deseada = dist > 1e-4f ? d / dist * Mathf.Min(rapidez * E, dist * 3f) : Vector3.zero;
        // Esquivar: si delante hay algo cerca, se aparta según su normal.
        if (deseada.sqrMagnitude > 1e-6f && Rayo(transform.position, deseada.normalized, C.distanciaEsquivar * E, out var h))
            deseada += h.normal * rapidez * E * (1f - h.distance / (C.distanciaEsquivar * E));
        velocidad = Vector3.Lerp(velocidad, deseada, 1f - Mathf.Exp(-4f * dt));
        transform.position += velocidad * dt;

        Vector3 haciaDonde = mirar.HasValue ? mirar.Value - transform.position : velocidad;
        if (haciaDonde.sqrMagnitude > 1e-6f)
        {
            // Se inclina un poco en los giros, como una libélula.
            var rot = Quaternion.LookRotation(haciaDonde.normalized, Vector3.up);
            float alabeo = Mathf.Clamp(-Vector3.Dot(velocidad, transform.right) * 25f, -30f, 30f);
            rot *= Quaternion.AngleAxis(alabeo, Vector3.forward);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, C.giro * dt);
        }
    }

    // ------------------------------------------------------------------ Alas

    void ColocarAlas()
    {
        float la = C.largoAla * E, aa = C.anchoAla * E, g = C.grosorCuerpo * E;
        foreach (var (t, lado, delante) in alas)
        {
            // Aletean rápido; las de delante y las de detrás van desfasadas.
            float aleteo = Mathf.Sin(fase * C.aleteos * Mathf.PI * 2f + (delante > 0f ? 0f : Mathf.PI * 0.6f)) * C.aperturaAleteo;
            Quaternion rotLocal = Quaternion.AngleAxis(-lado * aleteo, Vector3.forward) * Quaternion.AngleAxis(delante * 12f * lado, Vector3.up);
            Vector3 raiz = new Vector3(lado * g * 0.4f, g * 0.35f, delante * C.largoCuerpo * E * 0.12f + C.largoCuerpo * E * 0.15f);
            Vector3 haciaFuera = rotLocal * (Vector3.right * lado);
            t.localPosition = raiz + haciaFuera * la * 0.5f;
            t.localRotation = Quaternion.LookRotation(rotLocal * Vector3.forward, rotLocal * Vector3.up);
            t.localScale = new Vector3(la, g * 0.08f, aa);
        }
    }

    void AplicarMateriales()
    {
        Poner(cuerpo, MatCuerpo); Poner(cabeza, MatCuerpo); Poner(cola, MatCuerpo);
        foreach (var (t, _, _) in alas) Poner(t, MatAlas);
    }

    static void Poner(Transform t, Material m)
    {
        if (t == null || m == null) return;
        var r = t.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
    }

    // ------------------------------------------------------------------ Jugador y rayos

    Vector3 CuerpoJugador()
    {
        if (camJugador != null) return camJugador.transform.position - Vector3.up * 0.06f * EscalaJugador();
        return jugador != null ? jugador.position : transform.position + transform.forward;
    }

    float EscalaJugador() => jugador != null ? Mathf.Abs(jugador.lossyScale.y) : 1f;

    bool Ignorar(Collider c)
    {
        if (c == null || c.isTrigger) return true;
        if (c.transform.IsChildOf(transform) || c.GetComponentInParent<Libelula>() != null || c.GetComponentInParent<Arana>() != null) return true;
        if (jugador != null && c.transform.IsChildOf(jugador)) return true;
        if (c.GetComponent<FracturePiece>() != null || c.GetComponent<SegmentoCiempies>() != null) return true;
        var rb = c.attachedRigidbody;
        return rb != null && !rb.isKinematic;
    }

    bool Rayo(Vector3 origen, Vector3 dir, float dist, out RaycastHit mejor)
    {
        mejor = default;
        int n = Physics.RaycastNonAlloc(origen, dir, hits, dist, ~0, QueryTriggerInteraction.Ignore);
        float md = float.MaxValue;
        bool ok = false;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].distance <= 0f || Ignorar(hits[i].collider)) continue;
            if (hits[i].distance < md) { md = hits[i].distance; mejor = hits[i]; ok = true; }
        }
        return ok;
    }

    bool HayVision(Vector3 objetivo)
    {
        Vector3 d = objetivo - transform.position;
        return !Rayo(transform.position, d.normalized, d.magnitude * 0.95f, out _);
    }

    bool ChocaConAlgo(Vector3 mov) => mov.sqrMagnitude > 1e-10f && Rayo(transform.position, mov.normalized, mov.magnitude + C.grosorCuerpo * E, out _);

    bool TeHaDado()
    {
        if (cc == null) return false;
        float esc = EscalaJugador();
        Vector3 c = cc.transform.TransformPoint(cc.center);
        float medio = Mathf.Max(0f, cc.height * 0.5f - cc.radius) * esc;
        Vector3 a = c - Vector3.up * medio, b = c + Vector3.up * medio, ab = b - a;
        float k = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(transform.position - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(transform.position, a + ab * k) < cc.radius * esc + C.radioGolpe * E;
    }

    void Golpear()
    {
        if (Partida.Instancia != null) Partida.Instancia.PenalizarVelocidad(C.penalizacionVelocidad);
        if (arma != null) for (int i = 0; i < C.balasQuePierdes; i++) arma.PerderBala();
        Morir(true);
    }

    // ------------------------------------------------------------------ Daño

    /// <summary>Le ha dado un disparo: pierde una vida y, sin vidas, se rompe.</summary>
    public void RecibirDisparo()
    {
        vida--;
        if (vida > 0) return;
        if (Partida.Instancia != null) Partida.Instancia.ContarArana(); // cuenta como enemigo matado
        Morir(true);
    }

    public void Morir(bool estallido = true)
    {
        if (estallido && cuerpoRenderer != null)
            CrystalBurst.Spawn(cuerpoRenderer, transform.position, transform.forward, C.trocitosAlMorir, 1.2f * E, 0.45f, 0.35f);
        Destroy(gameObject);
    }
}

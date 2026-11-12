using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemigo araña de 4 patas, montado por código (no necesita modelo):
/// - Cuerpo low poly facetado; 4 patas de dos segmentos colocadas con IK de dos huesos.
/// - Los pies dan pasos procedurales por parejas en diagonal (como una araña).
/// Comportamiento: sale de la puerta y va directa a por ti por el suelo (bordea las paredes). Si de camino ve un
/// cristal en el suelo, va a por él, lo coge y te lo lanza (lo puedes atrapar con E; si te da, pierdes una bala), y
/// sigue a por ti. Cuando te tiene a tiro salta contra ti: si te da, pierdes una bala y se rompe.
/// Un disparo de cristal la rompe. Todo se ajusta en <see cref="ConfigAranas"/> (Tools > Arañas > Configuración).
/// La crea <see cref="GeneradorAranas"/> al abrirse una puerta.
/// </summary>
public class Arana : MonoBehaviour
{
    public static readonly List<Arana> Todas = new List<Arana>();

    [Tooltip("Configuración (vacía = la del proyecto, Assets/Enemigos/Resources/ConfigAranas).")]
    public ConfigAranas config;

    enum Estado { Esperando, Saliendo, Persiguiendo, Cogiendo, Apuntando, EnElAire }

    class Pata
    {
        public float x, z;               // lado (-1/1) y delante/detrás (1/-1)
        public Vector3 pie, desde, hasta;
        public float t = -1f;            // -1 = apoyada; 0..1 = dando un paso
        public int grupo;
        public Transform muslo, tibia;
    }

    readonly List<Pata> patas = new List<Pata>();
    Estado estado;
    Vector3 pos, normal = Vector3.up, adelante = Vector3.forward, arribaCuerpo = Vector3.up;
    Vector3 puntoSalida;
    float tiempoEstado, bloqueado, esperaCristal, esperaSalto;
    Vector3 velAire;
    bool ataque;
    int vida;

    FracturePiece cristal;          // el que lleva en la boca
    Rigidbody rbCristal;
    Collider[] colsCristal;
    Vector3 cristalDesde;
    FracturePiece objetivoCristal;  // al que va
    FracturePiece cristalIgnorado;  // uno al que no llega: lo deja en paz un rato
    float ignorarHasta, siguienteBusqueda;

    Transform jugador;
    Camera camJugador;
    CharacterController ccJugador;
    AtraerFragmentos atraer;
    CrystalWeapon arma;
    SphereCollider miCollider;
    Transform cuerpo, cabeza;
    Renderer cuerpoRenderer;

    // Materiales: los del asset de configuración; si están vacíos, los de la escena (cubo de cristal y puerta).
    Material matCuerpoBase, matPatasBase;
    Material MatCuerpo => C.materialCuerpo != null ? C.materialCuerpo : matCuerpoBase;
    Material MatPatas => C.materialPatas != null ? C.materialPatas : matPatasBase;
    Material MatCristal => C.materialCristal != null ? C.materialCristal : MatCuerpo;
    Material MatCuerda => C.materialCuerda != null ? C.materialCuerda : MatPatas;

    // Cristal protector atado encima.
    Transform escudo;
    Renderer escudoRenderer;
    Transform cuerda;
    int vidaEscudo;
    float faseEscudo;

    /// <summary>¿Sigue teniendo su cristal protector? Mientras lo tenga, los disparos no le hacen nada.</summary>
    public bool TieneEscudo => escudo != null;

    static readonly RaycastHit[] hits = new RaycastHit[24];
    static Material materialPorDefecto;

    ConfigAranas C => config != null ? config : (config = ConfigAranas.Cargar());
    float E => Mathf.Max(0.01f, C.escala);
    float Tamano => C.tamano * E;
    float Altura => C.alturaCuerpo * E;

    // ------------------------------------------------------------------ Creación

    /// <summary>Crea una araña apoyada en <paramref name="punto"/> que sale hacia <paramref name="salida"/>.</summary>
    public static Arana Crear(Vector3 punto, Vector3 normalSuelo, Vector3 salida, ConfigAranas cfg, Material matCuerpo, Material matPatas)
    {
        var go = new GameObject("Araña");
        var a = go.AddComponent<Arana>();
        a.config = cfg;
        a.pos = punto;
        a.normal = a.arribaCuerpo = normalSuelo.sqrMagnitude > 1e-6f ? normalSuelo.normalized : Vector3.up;
        a.adelante = Vector3.ProjectOnPlane(salida, a.normal).normalized;
        if (a.adelante.sqrMagnitude < 1e-6f) a.adelante = Vector3.forward;
        a.puntoSalida = punto + a.adelante * a.C.distanciaSalida;
        a.Montar(matCuerpo, matPatas);
        return a;
    }

    void OnEnable() => Todas.Add(this);
    void OnDisable() => Todas.Remove(this);

    void Montar(Material matCuerpo, Material matPatas)
    {
        matCuerpoBase = matCuerpo;
        matPatasBase = matPatas;
        matCuerpo = MatCuerpo;
        matPatas = MatPatas;
        var fps = FindAnyObjectByType<FirstPersonController>();
        if (fps != null)
        {
            jugador = fps.transform;
            camJugador = fps.cam != null ? fps.cam : fps.GetComponentInChildren<Camera>();
            ccJugador = fps.GetComponent<CharacterController>();
        }
        atraer = FindAnyObjectByType<AtraerFragmentos>();
        arma = FindAnyObjectByType<CrystalWeapon>();
        vida = C.vida;

        transform.SetPositionAndRotation(pos + normal * Altura, Quaternion.LookRotation(adelante, normal));

        cuerpo = Pieza("Cuerpo", MallaFacetada(new Vector3(1f, 0.6f, 1.25f)), matCuerpo);
        cuerpoRenderer = cuerpo.GetComponent<Renderer>();
        cabeza = Pieza("Cabeza", MallaFacetada(new Vector3(0.6f, 0.45f, 0.6f)), matCuerpo);

        // Colisión: para que la rompan los disparos y que choque con el jugador.
        miCollider = gameObject.AddComponent<SphereCollider>();
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        AjustarTamano();

        // Patas a cada lado, repartidas de delante (z = 1) a detrás (z = -1). Andan en dos grupos alternos (como las
        // arañas de verdad): la 1.ª y 3.ª de un lado con la 2.ª y 4.ª del otro.
        int porLado = Mathf.Max(1, C.patasPorLado);
        for (int lado = 0; lado < 2; lado++)
            for (int k = 0; k < porLado; k++)
            {
                var p = new Pata
                {
                    x = lado == 0 ? -1f : 1f,
                    z = porLado > 1 ? Mathf.Lerp(1f, -1f, k / (porLado - 1f)) : 0f,
                    grupo = (k + lado) % 2,
                };
                p.muslo = Pieza("Muslo", null, matPatas, true);
                p.tibia = Pieza("Tibia", null, matPatas, true);
                patas.Add(p);
                p.pie = PuntoReposo(p);
            }
        if (C.cristalProtector) CrearEscudo(matCuerpo, matPatas);
        AplicarMateriales();
        estado = Estado.Saliendo;
    }

    /// <summary>El cristal que flota atado encima: hay que romperlo antes de poder dañar a la araña.</summary>
    void CrearEscudo(Material matCuerpo, Material matPatas)
    {
        vidaEscudo = C.vidaCristal;
        faseEscudo = Random.value * Mathf.PI * 2f;
        escudo = Pieza("Cristal protector", MallaFacetada(new Vector3(0.6f, 1.4f, 0.6f)), MatCristal);
        escudoRenderer = escudo.GetComponent<Renderer>();
        var sc = escudo.gameObject.AddComponent<SphereCollider>();
        sc.radius = 1.1f; // en unidades locales: se escala con el cristal
        escudo.gameObject.AddComponent<CristalEscudo>().arana = this;

        // La cuerda es una barra fina (no un LineRenderer) para que lleve normales suavizadas como el resto (contorno de FlatKit).
        cuerda = Pieza("Cuerda", null, MatCuerda != null ? MatCuerda : escudoRenderer.sharedMaterial, true);
        ActualizarEscudo();
    }

    /// <summary>Sube y baja sobre el cuerpo (siempre hacia arriba en el mundo), girando, con la cuerda tensa hasta el lomo.</summary>
    void ActualizarEscudo()
    {
        if (escudo == null) return;
        float s = C.tamanoCristal * E;
        float h = (C.alturaCristal + Mathf.Sin(Time.time * C.velocidadOscilacion * Mathf.PI * 2f + faseEscudo) * C.oscilacionCristal) * E;
        Vector3 lomo = transform.position + transform.up * Tamano * 0.5f;
        Vector3 p = transform.position + Vector3.up * h;
        escudo.SetPositionAndRotation(p, Quaternion.Euler(0f, Time.time * C.giroCristal + faseEscudo * Mathf.Rad2Deg, 0f));
        escudo.localScale = Vector3.one * s;
        if (cuerda != null)
        {
            Vector3 punta = p - Vector3.up * s * 1.2f; // la punta de abajo del cristal
            Vector3 d = punta - lomo;
            if (d.sqrMagnitude > 1e-10f)
            {
                cuerda.SetPositionAndRotation((lomo + punta) * 0.5f, Quaternion.LookRotation(d.normalized, transform.forward));
                float g = C.grosorCuerda * E;
                cuerda.localScale = new Vector3(g, g, d.magnitude);
            }
        }
    }

    /// <summary>Un disparo le ha dado al cristal protector: pierde una vida y, sin vidas, estalla y la araña queda expuesta.</summary>
    public void DanarEscudo()
    {
        if (escudo == null) return;
        vidaEscudo--;
        if (vidaEscudo > 0) return;
        if (escudoRenderer != null)
        {
            CrystalBurst.Spawn(escudoRenderer, escudo.position, Vector3.up, C.trocitosAlMorir, 1.2f * E, 0.45f, 0.35f);
            // Suelta un trozo de cristal, como los de los cubos (se puede atraer, atrapar o devolver).
            if (C.soltarCristal)
            {
                // Se ha hecho añicos y solo queda una esquirla pequeña, del tamaño de los trozos de las paredes.
                float tam = C.tamanoCristalSuelto > 0f ? C.tamanoCristalSuelto : TamanoTrozoPared();
                var malla = Esquirla(tam);
                FracturePiece.Crear(malla, escudoRenderer.sharedMaterial, escudo.gameObject.layer,
                                    escudo.position, Random.rotation, Vector3.one,
                                    Vector3.up * 1.2f * E + Random.insideUnitSphere * 0.4f * E, C.vidaCristalSuelto);
                Destroy(malla); // Crear usa su propia copia
            }
        }
        Destroy(escudo.gameObject);
        if (cuerda != null) Destroy(cuerda.gameObject);
        escudo = null;
        cuerda = null;
    }

    /// <summary>Tamaño típico de un trozo de los cubos de las paredes (lado del cubo entre su número de trozos por lado).</summary>
    static float TamanoTrozoPared()
    {
        var cubo = FindAnyObjectByType<PyramidCube>();
        if (cubo == null) return 0.03f;
        Vector3 s = cubo.transform.lossyScale;
        float lado = cubo.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        return lado / Mathf.Max(1, cubo.detail) * 0.6f;
    }

    /// <summary>Esquirla irregular de cristal (tetraedro deformado, caras planas) de unos <paramref name="tam"/> de largo.</summary>
    static Mesh Esquirla(float tam)
    {
        float h = tam * 0.5f;
        var p = new Vector3[4];
        p[0] = new Vector3(0f, h * Random.Range(0.9f, 1.3f), 0f);
        for (int i = 0; i < 3; i++)
        {
            float a = (i * 120f + Random.Range(-25f, 25f)) * Mathf.Deg2Rad;
            float r = h * Random.Range(0.45f, 0.8f);
            p[i + 1] = new Vector3(Mathf.Cos(a) * r, -h * Random.Range(0.3f, 0.6f), Mathf.Sin(a) * r);
        }
        Vector3 c = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
        int[,] caras = { { 0, 1, 2 }, { 0, 2, 3 }, { 0, 3, 1 }, { 1, 3, 2 } };
        var verts = new Vector3[12];
        var idx = new int[12];
        for (int f = 0; f < 4; f++)
        {
            Vector3 a = p[caras[f, 0]] - c, b = p[caras[f, 1]] - c, d = p[caras[f, 2]] - c;
            // Unity: cara frontal en sentido horario visto desde fuera (normal = cross(b-a, d-a) hacia fuera).
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), a + b + d) < 0f) { var t = b; b = d; d = t; }
            verts[f * 3] = a; verts[f * 3 + 1] = b; verts[f * 3 + 2] = d;
            idx[f * 3] = f * 3; idx[f * 3 + 1] = f * 3 + 1; idx[f * 3 + 2] = f * 3 + 2;
        }
        var m = new Mesh { name = "Esquirla" };
        m.vertices = verts;
        m.triangles = idx;
        m.RecalculateNormals();
        m.RecalculateBounds();
        NormalesParaContorno(m);
        return m;
    }

    /// <summary>Aplica los materiales del asset de configuración (también si se cambian en Play).</summary>
    void AplicarMateriales()
    {
        Poner(cuerpo, MatCuerpo);
        Poner(cabeza, MatCuerpo);
        foreach (var p in patas) { Poner(p.muslo, MatPatas); Poner(p.tibia, MatPatas); }
        Poner(escudo, MatCristal);
        Poner(cuerda, MatCuerda);
    }

    static string Nombre(Object o) => o != null ? o.name : "(vacío)";

    static void Poner(Transform t, Material m)
    {
        if (t == null || m == null) return;
        var r = t.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
    }

    /// <summary>Aplica el tamaño de la configuración (se puede cambiar en Play).</summary>
    void AjustarTamano()
    {
        float t = Tamano;
        if (cuerpo != null) cuerpo.localScale = Vector3.one * t;
        if (cabeza != null) { cabeza.localScale = Vector3.one * t; cabeza.localPosition = new Vector3(0f, 0.15f, 1.25f) * t; }
        if (miCollider != null) miCollider.radius = t * 1.1f;
    }

    Transform Pieza(string nombre, Mesh malla, Material mat, bool cubo = false)
    {
        GameObject go;
        if (cubo)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = nombre;
            go.GetComponent<MeshFilter>().sharedMesh = CuboContorno();
        }
        else
        {
            go = new GameObject(nombre);
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            go.AddComponent<MeshRenderer>();
        }
        var r = go.GetComponent<MeshRenderer>();
        if (mat == null)
        {
            if (materialPorDefecto == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                materialPorDefecto = tmp.GetComponent<Renderer>().sharedMaterial;
                Destroy(tmp);
            }
            mat = materialPorDefecto;
        }
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    static Mesh cuboContorno;

    /// <summary>El cubo de Unity con normales suavizadas para el contorno (las patas y la cuerda).</summary>
    public static Mesh CuboContorno()
    {
        if (cuboContorno != null) return cuboContorno;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuboContorno = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
        cuboContorno.name = "Cubo (contorno)";
        Destroy(tmp);
        NormalesParaContorno(cuboContorno);
        return cuboContorno;
    }

    /// <summary>
    /// Normales suavizadas en UV1 y UV2, igual que AutoSmoothNormalsTool: el contorno de FlatKit (Vertex Extrusion Smooth
    /// Normals) las lee de ahí. Sin ellas el contorno se dibuja encima de toda la pieza y la tapa (se ve blanca).
    /// </summary>
    public static void NormalesParaContorno(Mesh m)
    {
        var v = m.vertices;
        var n = m.normals;
        var suma = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < v.Length; i++) suma[v[i]] = (suma.TryGetValue(v[i], out var s) ? s : Vector3.zero) + n[i];
        var uv = new List<Vector4>(v.Length);
        for (int i = 0; i < v.Length; i++) { var s = suma[v[i]].normalized; uv.Add(new Vector4(s.x, s.y, s.z, 0f)); }
        m.SetUVs(1, uv);
        m.SetUVs(2, uv);
    }

    static readonly Dictionary<Vector3, Mesh> mallas = new Dictionary<Vector3, Mesh>();

    /// <summary>Octaedro con caras planas (low poly), con las medias medidas de <paramref name="r"/>.</summary>
    public static Mesh MallaFacetada(Vector3 r)
    {
        if (mallas.TryGetValue(r, out var m) && m != null) return m;
        Vector3 d = new Vector3(0, 0, r.z), t = new Vector3(0, 0, -r.z * 0.85f), iz = new Vector3(-r.x, 0, 0), de = new Vector3(r.x, 0, 0),
                ar = new Vector3(0, r.y, 0), ab = new Vector3(0, -r.y * 0.7f, 0);
        var tris = new[] { ar, de, d, ar, d, iz, ar, iz, t, ar, t, de, ab, d, de, ab, iz, d, ab, t, iz, ab, de, t };
        // Unity toma como cara frontal la que tiene los vértices en sentido horario visto desde fuera: se invierte el
        // orden de cada triángulo para que las caras (y las normales, de las que sale el contorno) miren hacia fuera.
        var idx = new int[tris.Length];
        for (int i = 0; i < idx.Length; i += 3) { idx[i] = i; idx[i + 1] = i + 2; idx[i + 2] = i + 1; }
        m = new Mesh { name = "Araña facetada" };
        m.vertices = tris;
        m.triangles = idx;
        m.RecalculateNormals();
        m.RecalculateBounds();
        NormalesParaContorno(m);
        mallas[r] = m;
        return m;
    }

    // ------------------------------------------------------------------ Rayos

    bool Ignorar(Collider c)
    {
        if (c == null || c.isTrigger) return true;
        if (c.transform.IsChildOf(transform) || c.GetComponentInParent<Arana>() != null || c.GetComponentInParent<Libelula>() != null) return true;
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

    static bool EsSuelo(Vector3 n) => n.y > 0.6f;

    // ------------------------------------------------------------------ Andar por el suelo

    /// <summary>
    /// Anda por el suelo hacia <paramref name="objetivo"/>. Sube rampas suaves; si choca con una pared, la bordea
    /// (va a lo largo de ella hacia el lado que más le acerca). Devuelve false si no ha podido avanzar.
    /// </summary>
    bool Andar(Vector3 objetivo, float dt, float rapidez = 1f, bool escalar = false)
    {
        Vector3 deseada = Vector3.ProjectOnPlane(objetivo - pos, normal);
        if (deseada.sqrMagnitude < 1e-6f) return false;
        adelante = Vector3.RotateTowards(adelante, deseada.normalized, C.giro * Mathf.Deg2Rad * dt, 0f);
        adelante = Vector3.ProjectOnPlane(adelante, normal).normalized;
        float paso = Mathf.Min(C.velocidad * rapidez * dt, deseada.magnitude);
        float h = Altura;

        Vector3 dir = adelante;
        // Algo delante.
        if (Rayo(pos + normal * h * 0.6f, dir, paso + Tamano * 1.4f, out var hc) && Vector3.Angle(hc.normal, normal) > 25f)
        {
            if (escalar || EsSuelo(hc.normal))
            {
                // Escalando (o una rampa suave): pasa a esa superficie.
                CambiarSuperficie(hc.point, hc.normal);
                return true;
            }
            // Pared: bordearla.
            Vector3 lado = Vector3.Cross(normal, hc.normal);
            if (lado.sqrMagnitude < 1e-6f) return false;
            lado.Normalize();
            if (Vector3.Dot(lado, deseada) < 0f) lado = -lado;
            dir = lado;
            if (Rayo(pos + normal * h * 0.6f, dir, paso + Tamano * 1.4f, out var hl) && !EsSuelo(hl.normal)) return false; // esquina
        }

        // Seguir pegada a la superficie (sube y baja rampas y escalones pequeños).
        Vector3 np = pos + dir * paso;
        if (Rayo(np + normal * h * 1.5f, -normal, h * 3.5f, out var hd))
        {
            if (!escalar && !EsSuelo(hd.normal))
            {
                // Borde con una pendiente que baja (p.ej. la rampa de una plataforma): se deja caer en vez de quedarse parada.
                SaltoLibre(dir * C.velocidad + normal * C.velocidad * 0.5f);
                return false;
            }
            pos = hd.point;
            normal = Vector3.Slerp(normal, hd.normal, 0.5f).normalized;
            if (dir != adelante) adelante = Vector3.RotateTowards(adelante, dir, C.giro * Mathf.Deg2Rad * dt, 0f);
            return true;
        }
        // Escalando: borde convexo (p.ej. arriba de la pared de una caja), rodearlo y pasar a la otra cara.
        if (escalar && Rayo(np - normal * h * 1.2f, -dir, paso + Tamano * 2.5f, out var he))
        {
            CambiarSuperficie(he.point, he.normal);
            return true;
        }
        // Nada debajo: cae.
        Saltar(Vector3.zero, false);
        return false;
    }

    void CambiarSuperficie(Vector3 punto, Vector3 n)
    {
        Vector3 anterior = normal;
        pos = punto;
        normal = n.normalized;
        Vector3 f = Vector3.ProjectOnPlane(adelante, normal);
        // Al subir de frente a una pared, "adelante" pasa a ser hacia arriba por la pared.
        if (f.sqrMagnitude < 0.05f) f = Vector3.ProjectOnPlane(anterior, normal);
        if (f.sqrMagnitude > 1e-6f) adelante = f.normalized;
    }

    /// <summary>¿Tiene que escalar para llegar a ti? (estás más alto que ella, o ya está en una pared y tiene que seguir).</summary>
    bool DebeEscalar(Vector3 objetivoPies)
    {
        if (!C.escalarParedes) return false;
        if (!EsSuelo(normal)) return true;
        return objetivoPies.y > pos.y + C.alturaParaEscalar * E;
    }

    Vector3 PiesJugador()
    {
        if (ccJugador != null) return ccJugador.transform.TransformPoint(ccJugador.center - Vector3.up * ccJugador.height * 0.5f);
        return jugador != null ? jugador.position : transform.position;
    }

    // ------------------------------------------------------------------ Bucle

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        tiempoEstado += dt;
        esperaCristal -= dt;
        esperaSalto -= dt;
        AjustarTamano();
        AplicarMateriales();

        switch (estado)
        {
            case Estado.Esperando: break; // quieta detrás de la puerta hasta que termine de bajar
            case Estado.Saliendo: Saliendo(dt); break;
            case Estado.Persiguiendo: Persiguiendo(dt); break;
            case Estado.Cogiendo:
                if (cristal == null) CambiarEstado(Estado.Persiguiendo);
                else if (tiempoEstado >= C.tiempoCoger) CambiarEstado(Estado.Apuntando);
                break;
            case Estado.Apuntando: Apuntando(dt); break;
            case Estado.EnElAire: EnElAire(dt); break;
        }
        if (this == null) return;

        if (estado != Estado.EnElAire)
        {
            arribaCuerpo = Vector3.Slerp(arribaCuerpo, normal, 1f - Mathf.Exp(-12f * dt)).normalized;
            Vector3 f = Vector3.ProjectOnPlane(adelante, arribaCuerpo);
            if (f.sqrMagnitude > 1e-6f)
                transform.SetPositionAndRotation(pos + arribaCuerpo * Altura, Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f, arribaCuerpo), 1f - Mathf.Exp(-15f * dt)));
        }
        if (transform.position.y < -20f) Morir(false);
    }

    void LateUpdate()
    {
        ActualizarPatas(Time.deltaTime);
        ActualizarEscudo();
        if (cristal != null && (estado == Estado.Cogiendo || estado == Estado.Apuntando)) SujetarCristal();
    }

    void CambiarEstado(Estado e)
    {
        estado = e;
        tiempoEstado = 0f;
        bloqueado = 0f;
    }

    /// <summary>Se queda quieta (detrás de la puerta cerrada) hasta que se llame a <see cref="Salir"/>.</summary>
    public void Esperar() => CambiarEstado(Estado.Esperando);

    /// <summary>Empieza a salir por la puerta (cuando ya ha bajado).</summary>
    public void Salir() { if (estado == Estado.Esperando) CambiarEstado(Estado.Saliendo); }

    void Saliendo(float dt)
    {
        Andar(puntoSalida, dt);
        if (Vector3.Distance(pos, puntoSalida) < 0.03f || tiempoEstado > 1.5f) CambiarEstado(Estado.Persiguiendo);
    }

    void Persiguiendo(float dt)
    {
        // (Las arañas ya no cogen ni lanzan cristales: ahora los cristales suben solos al agujero del centro.)
        // A por el jugador.
        if (jugador == null) return;
        Vector3 objetivo = CuerpoJugador();
        bool escalar = DebeEscalar(PiesJugador());
        bool avanzaJugador = Andar(objetivo, dt, 1f, escalar);
        // Atascada (esquina, borde…): un saltito hacia delante para salir.
        bloqueado = avanzaJugador ? 0f : bloqueado + dt;
        if (estado == Estado.Persiguiendo && bloqueado > 0.5f && EsSuelo(normal))
        {
            Vector3 hacia = Vector3.ProjectOnPlane(objetivo - pos, Vector3.up).normalized;
            SaltoLibre((hacia + adelante).normalized * C.velocidad * 1.2f + Vector3.up * C.velocidad * 1.6f);
            return;
        }
        // Si estás más alto y ella sigue en el suelo, primero escala (no se queda esperando abajo para saltar).
        bool esperarAEscalar = escalar && EsSuelo(normal);
        if (!esperarAEscalar && esperaSalto <= 0f && Vector3.Distance(transform.position, objetivo) < C.alcanceSalto && HayVision(objetivo))
            Saltar(objetivo, true);
    }

    // ------------------------------------------------------------------ Cristales

    FracturePiece CristalCercano()
    {
        FracturePiece mejor = null;
        float md = C.radioCristales;
        foreach (var fp in FindObjectsByType<FracturePiece>(FindObjectsSortMode.None))
        {
            if (fp.sujeta || fp.GetComponent<FragmentoAtraido>() != null) continue;
            if (fp == cristalIgnorado && Time.time < ignorarHasta) continue;
            var rb = fp.GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic || rb.linearVelocity.sqrMagnitude > 0.04f) continue; // quieto en el suelo
            bool otra = false;
            foreach (var a in Todas) if (a != this && (a.cristal == fp || a.objetivoCristal == fp)) { otra = true; break; }
            if (otra) continue;
            float d = Vector3.Distance(fp.transform.position, pos);
            if (d < md) { md = d; mejor = fp; }
        }
        return mejor;
    }

    Vector3 Boca() => transform.TransformPoint(new Vector3(0f, 0.25f, 1.9f) * Tamano);

    void Coger(FracturePiece c)
    {
        cristal = c;
        objetivoCristal = null;
        rbCristal = c.GetComponent<Rigidbody>();
        cristal.sujeta = true;
        rbCristal.isKinematic = true;
        colsCristal = c.GetComponentsInChildren<Collider>();
        foreach (var col in colsCristal) col.enabled = false;
        cristalDesde = rbCristal.worldCenterOfMass;
        CambiarEstado(Estado.Cogiendo);
    }

    void Apuntando(float dt)
    {
        if (cristal == null) { CambiarEstado(Estado.Persiguiendo); return; }
        if (jugador != null)
        {
            Vector3 f = Vector3.ProjectOnPlane(CuerpoJugador() - pos, normal);
            if (f.sqrMagnitude > 1e-6f) adelante = Vector3.RotateTowards(adelante, f.normalized, C.giro * 1.5f * Mathf.Deg2Rad * dt, 0f);
        }
        if (tiempoEstado >= C.tiempoApuntar) Lanzar();
    }

    void SujetarCristal()
    {
        if (rbCristal == null) return;
        Vector3 boca = Boca();
        Vector3 centro = estado == Estado.Cogiendo
            ? Vector3.Lerp(cristalDesde, boca, Mathf.SmoothStep(0f, 1f, tiempoEstado / Mathf.Max(0.01f, C.tiempoCoger)))
            : boca;
        var tr = rbCristal.transform;
        tr.position = centro - tr.TransformVector(rbCristal.centerOfMass);
    }

    void SoltarCristal()
    {
        if (cristal == null) return;
        foreach (var col in colsCristal)
        {
            if (col == null) continue;
            col.enabled = true;
            if (miCollider != null) Physics.IgnoreCollision(col, miCollider, true);
        }
        rbCristal.isKinematic = false;
        rbCristal.WakeUp();
        cristal.sujeta = false;
        cristal.Rejuvenecer();
    }

    void Lanzar()
    {
        var c = cristal;
        var rb = rbCristal;
        SoltarCristal();
        cristal = null;
        rbCristal = null;
        // Mismo lanzamiento que el clic derecho: se puede atrapar con E y, si te da, pierdes una bala.
        if ((atraer == null || !atraer.LanzarPieza(c)) && jugador != null && rb != null)
        {
            float t = 0.8f;
            rb.linearVelocity = (CuerpoJugador() - rb.worldCenterOfMass) / t - 0.5f * Physics.gravity * t;
        }
        esperaCristal = C.esperaTrasLanzar;
        CambiarEstado(Estado.Persiguiendo);
    }

    // ------------------------------------------------------------------ Salto

    Vector3 CuerpoJugador()
    {
        if (camJugador != null) return camJugador.transform.position - Vector3.up * 0.08f * EscalaJugador();
        return jugador != null ? jugador.position : transform.position;
    }

    float EscalaJugador() => jugador != null ? Mathf.Abs(jugador.lossyScale.y) : 1f;

    bool HayVision(Vector3 objetivo)
    {
        Vector3 o = transform.position;
        Vector3 d = objetivo - o;
        return !Rayo(o, d.normalized, d.magnitude * 0.95f, out _);
    }

    /// <summary>Salto o caída sin atacar, con una velocidad inicial (para bajar de bordes o desatascarse).</summary>
    void SaltoLibre(Vector3 velocidadInicial)
    {
        ataque = false;
        velAire = velocidadInicial;
        CambiarEstado(Estado.EnElAire);
    }

    void Saltar(Vector3 objetivo, bool contraJugador)
    {
        ataque = contraJugador;
        float t = Mathf.Max(0.05f, C.tiempoSalto);
        velAire = contraJugador ? (objetivo - transform.position) / t - 0.5f * Physics.gravity * t : Vector3.zero;
        CambiarEstado(Estado.EnElAire);
    }

    void EnElAire(float dt)
    {
        velAire += Physics.gravity * dt;
        Vector3 p = transform.position;
        Vector3 mov = velAire * dt;

        // ¿Le ha dado al jugador?
        if (ataque && ccJugador != null)
        {
            float esc = EscalaJugador();
            Vector3 c = ccJugador.transform.TransformPoint(ccJugador.center);
            float medio = Mathf.Max(0f, ccJugador.height * 0.5f - ccJugador.radius) * esc;
            Vector3 a = c - Vector3.up * medio, b = c + Vector3.up * medio;
            Vector3 ab = b - a;
            float k = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            if (Vector3.Distance(p, a + ab * k) < ccJugador.radius * esc + C.radioGolpe * E)
            {
                // Se inmola contra ti: te quita velocidad base máxima (y balas, si así está configurado).
                if (Partida.Instancia != null) Partida.Instancia.PenalizarVelocidad(C.penalizacionVelocidad);
                if (arma != null) for (int i = 0; i < C.balasQuePierdes; i++) arma.PerderBala();
                Morir(true);
                return;
            }
        }

        // ¿Aterriza?
        if (mov.sqrMagnitude > 1e-10f && Rayo(p, mov.normalized, mov.magnitude + Altura, out var h))
        {
            if (EsSuelo(h.normal))
            {
                pos = h.point;
                normal = arribaCuerpo = h.normal.normalized;
                Vector3 f = Vector3.ProjectOnPlane(velAire, normal);
                if (f.sqrMagnitude > 1e-6f) adelante = f.normalized;
                transform.SetPositionAndRotation(pos + normal * Altura, Quaternion.LookRotation(adelante, normal));
                esperaSalto = C.esperaTrasSalto;
                CambiarEstado(Estado.Persiguiendo);
                return;
            }
            // Pared o pendiente: si estás más alto, se agarra a ella para escalar; si no, resbala (como en el surf) hasta el suelo.
            if (!ataque && C.escalarParedes && h.normal.y < 0.95f && PiesJugador().y > h.point.y + C.alturaParaEscalar * E)
            {
                CambiarSuperficie(h.point, h.normal);
                arribaCuerpo = normal;
                CambiarEstado(Estado.Persiguiendo);
                return;
            }
            velAire = Vector3.ProjectOnPlane(velAire, h.normal);
            if (h.normal.y > 0.05f && velAire.y > 0f) velAire.y = 0f; // en una pendiente, nunca hacia arriba
            mov = (h.point + h.normal * Altura) - p + velAire * dt;
            tiempoEstado = Mathf.Min(tiempoEstado, 4f); // mientras resbala no cuenta como perdida en el aire
        }

        transform.position = p + mov;
        if (velAire.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velAire.normalized, Vector3.up), 1f - Mathf.Exp(-8f * dt));
        if (tiempoEstado > 6f) Morir(false);
    }

    // ------------------------------------------------------------------ Patas (IK)

    Vector3 Cadera(Pata p) => transform.TransformPoint(new Vector3(p.x * 0.55f, 0.1f, p.z * 0.5f) * Tamano);
    Vector3 ReposoLocal(Pata p) => new Vector3(p.x, 0f, p.z * 0.85f) * (C.aperturaPatas * E);

    Vector3 PuntoReposo(Pata p)
    {
        Vector3 encima = transform.TransformPoint(ReposoLocal(p)) + arribaCuerpo * Altura;
        if (Rayo(encima, -arribaCuerpo, Altura * 3.5f, out var h)) return h.point;
        return transform.TransformPoint(ReposoLocal(p)) - arribaCuerpo * Altura;
    }

    void ActualizarPatas(float dt)
    {
        bool aire = estado == Estado.EnElAire;
        Vector3 vel = adelante * C.velocidad;
        float tiempoPaso = Mathf.Max(0.01f, C.tiempoPaso);
        foreach (var p in patas)
        {
            if (aire)
            {
                // Patas recogidas en el aire.
                Vector3 recogida = transform.TransformPoint(ReposoLocal(p) * 0.55f) - transform.up * Altura * 0.3f;
                p.pie = Vector3.Lerp(p.pie, recogida, 1f - Mathf.Exp(-14f * dt));
                p.t = -1f;
            }
            else if (p.t < 0f)
            {
                Vector3 reposo = PuntoReposo(p);
                bool otroGrupoEnPaso = false;
                foreach (var q in patas) if (q.grupo != p.grupo && q.t >= 0f) { otroGrupoEnPaso = true; break; }
                if (!otroGrupoEnPaso && Vector3.Distance(p.pie, reposo) > C.distanciaPaso * E)
                {
                    p.desde = p.pie;
                    p.hasta = reposo + vel * tiempoPaso * 0.5f; // se adelanta un poco al movimiento
                    p.t = 0f;
                }
            }
            if (!aire && p.t >= 0f)
            {
                p.t += dt / tiempoPaso;
                float u = Mathf.Clamp01(p.t);
                p.pie = Vector3.Lerp(p.desde, p.hasta, Mathf.SmoothStep(0f, 1f, u)) + arribaCuerpo * Mathf.Sin(u * Mathf.PI) * C.alturaPaso * E;
                if (p.t >= 1f) { p.t = -1f; p.pie = p.hasta; }
            }
            ColocarPata(p);
        }
    }

    /// <summary>IK de dos huesos: cadera → rodilla → pie, con la rodilla hacia arriba y hacia fuera.</summary>
    void ColocarPata(Pata p)
    {
        float l1 = C.largoMuslo * E, l2 = C.largoTibia * E;
        Vector3 cadera = Cadera(p);
        Vector3 hacia = p.pie - cadera;
        float d = Mathf.Clamp(hacia.magnitude, 1e-4f, l1 + l2 - 1e-4f);
        Vector3 dir = hacia.sqrMagnitude > 1e-10f ? hacia.normalized : -transform.up;
        Vector3 fuera = cadera - transform.position;
        Vector3 doblez = Vector3.ProjectOnPlane(transform.up + fuera.normalized * 0.6f, dir);
        if (doblez.sqrMagnitude < 1e-8f) doblez = Vector3.ProjectOnPlane(transform.up, dir);
        doblez.Normalize();
        float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        float alto = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
        Vector3 rodilla = cadera + dir * a + doblez * alto;
        Vector3 pie = cadera + dir * d;
        Hueso(p.muslo, cadera, rodilla, doblez);
        Hueso(p.tibia, rodilla, pie, doblez);
    }

    void Hueso(Transform b, Vector3 a, Vector3 c, Vector3 arriba)
    {
        Vector3 d = c - a;
        float largo = d.magnitude;
        if (largo < 1e-5f) return;
        b.SetPositionAndRotation((a + c) * 0.5f, Quaternion.LookRotation(d / largo, arriba));
        float g = C.grosorPata * E;
        b.localScale = new Vector3(g, g, largo);
    }

    // ------------------------------------------------------------------ Vida

    /// <summary>Le ha dado un disparo: si aún tiene su cristal protector no le pasa nada; si no, pierde una vida y, sin vidas, se rompe.</summary>
    public void RecibirDisparo()
    {
        if (TieneEscudo) return; // primero hay que romper su cristal
        vida--;
        if (vida <= 0)
        {
            if (Partida.Instancia != null) Partida.Instancia.ContarArana(); // la has matado tú: cuenta para la puntuación
            Morir(true);
        }
    }

    /// <summary>Se rompe en trocitos de cristal (y suelta el cristal si llevaba uno).</summary>
    public void Morir(bool estallido = true)
    {
        if (cristal != null) { SoltarCristal(); cristal = null; }
        if (estallido && cuerpoRenderer != null)
            CrystalBurst.Spawn(cuerpoRenderer, transform.position, transform.up, C.trocitosAlMorir, 1.2f * E, 0.45f, 0.35f);
        Destroy(gameObject);
    }
}

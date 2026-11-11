using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ciempiés de varios segmentos, montado por código (no necesita modelo):
/// - Entra por una ventana agarrándose a la pared y anda pegado a las superficies con rayos (como las arañas al
///   escalar): sube, baja, rodea esquinas y puede pasar al techo. Los segmentos siguen exactamente el rastro de la cabeza.
/// - Se pasea por las paredes y, cada <see cref="ConfigCiempies.intervaloCristales"/> segundos, va al cristal de la pared
///   (<see cref="PyramidCube"/>) más cercano y le da unos golpes: cada golpe suelta una pieza, que cae a la arena.
/// - Solo se puede romper por la cola: el último segmento (late y lleva el material "vulnerable") es el único al que le
///   hacen daño los disparos. Al romperlo, el siguiente pasa a ser la cola, hasta llegar a la cabeza.
/// Todo se ajusta en <see cref="ConfigCiempies"/> (Tools > Ciempiés > Configuración). Lo crea <see cref="GeneradorCiempies"/>.
/// </summary>
public class Ciempies : MonoBehaviour
{
    public static readonly List<Ciempies> Todos = new List<Ciempies>();

    [Tooltip("Configuración (vacía = la del proyecto).")]
    public ConfigCiempies config;

    class Segmento
    {
        public Transform t;
        public Renderer r;
        public int vida;
        public float tamano;
        public readonly List<Transform> patas = new List<Transform>();
        public readonly List<float> ladoPata = new List<float>();
        public readonly List<float> zPata = new List<float>(); // dónde sale a lo largo del segmento (-0.5..0.5)
    }

    struct Rastro { public Vector3 p, n; }

    enum Estado { Entrando, Paseando, IrACristal, Rompiendo }

    readonly List<Segmento> segs = new List<Segmento>(); // 0 = cabeza, último = cola
    readonly List<Rastro> rastro = new List<Rastro>();   // 0 = lo más reciente (la cabeza)
    Estado estado;
    Vector3 pos, normal = Vector3.up, adelante = Vector3.forward, arribaSuave = Vector3.up;
    Vector3 destinoPaseo;
    float tiempoRumbo, tiempoCristales, siguienteGolpe, fase;
    int golpes;
    PyramidCube objetivo, anterior;
    Material matCabeza, matSegmento, matPatas;

    static readonly RaycastHit[] hits = new RaycastHit[24];

    ConfigCiempies C => config != null ? config : (config = ConfigCiempies.Cargar());
    float E => Mathf.Max(0.01f, C.escala);
    float Altura => C.tamanoSegmento * E * C.alturaSobrePared;
    float Tamano => C.tamanoCabeza * E;

    // ------------------------------------------------------------------ Creación

    /// <summary>Crea un ciempiés en la ventana <paramref name="ventana"/>, que entra hacia <paramref name="haciaDentro"/> y se agarra a la pared.</summary>
    public static Ciempies Crear(Vector3 ventana, Vector3 haciaDentro, ConfigCiempies cfg, Material cabeza, Material segmento, Material patas)
    {
        var go = new GameObject("Ciempiés");
        var c = go.AddComponent<Ciempies>();
        c.config = cfg;
        c.matCabeza = cabeza;
        c.matSegmento = segmento;
        c.matPatas = patas;
        c.adelante = haciaDentro.sqrMagnitude > 1e-6f ? haciaDentro.normalized : Vector3.forward;
        c.entrando = true; // al entrar puede pisar cualquier cosa (el marco de la ventana)
        c.ventana = ventana;
        c.haciaDentro = c.adelante;
        c.destinoEntrada = c.PuntoDeEntrada(ventana, c.adelante);
        c.Agarrarse(ventana + c.adelante * 0.05f);
        c.Montar();
        return c;
    }

    void OnEnable() => Todos.Add(this);
    void OnDisable() => Todos.Remove(this);

    /// <summary>Se pega a la superficie más cercana a <paramref name="punto"/> (mirando en todas direcciones).</summary>
    void Agarrarse(Vector3 punto, bool soloSuPared = false)
    {
        float mejor = float.MaxValue;
        bool hay = false;
        RaycastHit elegido = default;
        for (int i = 0; i < 27; i++)
        {
            int x = i % 3 - 1, y = i / 3 % 3 - 1, z = i / 9 - 1;
            if (x == 0 && y == 0 && z == 0) continue;
            var d = new Vector3(x, y, z).normalized;
            if (Rayo(punto, d, 3f * E, out var h) && h.distance < mejor && (soloSuPared ? EsParedDeVerdad(h) : EsPared(h))) { mejor = h.distance; elegido = h; hay = true; }
        }
        pos = hay ? elegido.point : punto;
        normal = arribaSuave = hay ? elegido.normal : Vector3.up;
        Vector3 f = Vector3.ProjectOnPlane(adelante, normal);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Vector3.forward, normal);
        adelante = f.normalized;
    }

    void Montar()
    {
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        int n = 1 + Mathf.Max(1, C.segmentos);
        for (int i = 0; i < n; i++)
        {
            bool esCabeza = i == 0;
            var s = new Segmento
            {
                tamano = (esCabeza ? C.tamanoCabeza : C.tamanoSegmento) * E,
                vida = esCabeza ? C.vidaCabeza : C.vidaSegmento,
            };
            var go = new GameObject(esCabeza ? "Cabeza" : "Segmento " + i);
            go.transform.SetParent(transform, false);
            go.transform.position = pos + normal * Altura;
            go.AddComponent<MeshFilter>().sharedMesh = Arana.MallaFacetada(esCabeza ? new Vector3(0.9f, 0.6f, 1.3f) : new Vector3(1f, 0.55f, 0.8f));
            s.r = go.AddComponent<MeshRenderer>();
            s.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * s.tamano;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 1.1f;
            go.AddComponent<SegmentoCiempies>().ciempies = this;
            s.t = go.transform;

            // Patas: parejas a los lados, cada una una barra fina que sale hacia fuera y hacia la pared.
            int parejas = esCabeza ? 0 : C.parejasDePatas;
            for (int k = 0; k < parejas; k++)
                for (int lado = -1; lado <= 1; lado += 2)
                {
                    var pata = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(pata.GetComponent<Collider>());
                    pata.name = "Pata";
                    pata.GetComponent<MeshFilter>().sharedMesh = Arana.CuboContorno();
                    pata.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    pata.transform.SetParent(transform, false);
                    s.patas.Add(pata.transform);
                    s.ladoPata.Add(lado);
                    s.zPata.Add(parejas > 1 ? k / (parejas - 1f) - 0.5f : 0f);
                }
            segs.Add(s);
        }
        // Al principio todo el cuerpo está en la ventana y va saliendo detrás de la cabeza.
        rastro.Add(new Rastro { p = pos, n = normal });
        fase = Random.value * 10f;
        tiempoCristales = 0f;
        estado = Estado.Entrando;
        AplicarMateriales();
        ColocarCuerpo();
    }

    Material MatSegmento => C.materialSegmento != null ? C.materialSegmento : matSegmento;
    Material MatCabeza => C.materialCabeza != null ? C.materialCabeza : MatSegmento;
    Material MatPatas => C.materialPatas != null ? C.materialPatas : matPatas;

    void AplicarMateriales()
    {
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            bool cola = i == segs.Count - 1;
            Material m = cola && C.materialVulnerable != null ? C.materialVulnerable : (i == 0 ? MatCabeza : MatSegmento);
            if (m != null && s.r.sharedMaterial != m) s.r.sharedMaterial = m;
            if (MatPatas != null)
                foreach (var p in s.patas)
                {
                    var pr = p.GetComponent<Renderer>();
                    if (pr.sharedMaterial != MatPatas) pr.sharedMaterial = MatPatas;
                }
        }
    }

    // ------------------------------------------------------------------ Rayos

    static bool Ignorar(Collider c)
    {
        if (c == null || c.isTrigger) return true;
        if (c.GetComponent<SegmentoCiempies>() != null || c.GetComponent<FracturePiece>() != null) return true;
        if (c.GetComponentInParent<Arana>() != null || c.GetComponentInParent<FirstPersonController>() != null) return true;
        var rb = c.attachedRigidbody;
        return rb != null && !rb.isKinematic;
    }

    static bool Rayo(Vector3 origen, Vector3 dir, float dist, out RaycastHit mejor)
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

    // ------------------------------------------------------------------ Su pared (filtro por material)

    Material materialPared; // el de la pared por la que entra, si no se indican materiales en la configuración
    bool entrando;
    float tiempoEntrando;
    Vector3 ventana, haciaDentro, destinoEntrada;

    /// <summary>
    /// Punto de la pared de dentro, debajo de la ventana (un rayo desde el centro del mapa). Si no hay materiales indicados
    /// en la configuración, el material de ese punto es "su pared" (la pared blanca), no el marco de la ventana.
    /// </summary>
    Vector3 PuntoDeEntrada(Vector3 v, Vector3 dentro)
    {
        Vector3 centro = CentroMapa();
        Vector3 lado = Vector3.Cross(Vector3.up, dentro);
        Vector3[] candidatos = { v - Vector3.up * 0.6f * E, v - Vector3.up * 0.9f * E, v + lado * 0.7f * E, v - lado * 0.7f * E };
        foreach (var objetivo in candidatos)
        {
            Vector3 d = objetivo - centro;
            if (Rayo(centro, d.normalized, d.magnitude + 1f, out var h) && Mathf.Abs(h.normal.y) < 0.7f)
            {
                if (materialPared == null && (C.materialesPared == null || C.materialesPared.Count == 0)) materialPared = MaterialMasComun(centro, centro.y);
                return h.point;
            }
        }
        return v + dentro * 0.5f * E;
    }

    /// <summary>El material que más aparece en las paredes alrededor del centro del mapa (la pared blanca).</summary>
    static Material MaterialMasComun(Vector3 centro, float altura)
    {
        var cuenta = new Dictionary<Material, int>();
        Vector3 o = new Vector3(centro.x, altura, centro.z);
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            if (!Rayo(o, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 30f, out var h) || Mathf.Abs(h.normal.y) > 0.7f) continue;
            var m = MaterialDe(h);
            if (m != null) cuenta[m] = cuenta.TryGetValue(m, out var n) ? n + 1 : 1;
        }
        Material mejor = null;
        int max = 0;
        foreach (var kv in cuenta) if (kv.Value > max) { max = kv.Value; mejor = kv.Key; }
        return mejor;
    }
    float bloqueo;          // tiempo seguido sin poder avanzar
    // Vigilante de atascos.
    float relojAtasco;
    int atascos;
    Vector3 posVigilada;
    float desatascarHasta = -1f;
    bool Desatascando => Time.time < desatascarHasta;

    /// <summary>¿Es de un material por el que puede andar?</summary>
    bool EsPared(RaycastHit h) => entrando || fueraDeSuPared || EsParedDeVerdad(h);

    /// <summary>¿Es de su material, sin excepciones (aunque esté entrando o volviendo a su pared)?</summary>
    bool EsParedDeVerdad(RaycastHit h)
    {
        // Solo paredes: nada de suelos ni techos (pueden ser del mismo material que la pared).
        if (Mathf.Abs(h.normal.y) > C.inclinacionMaxima) return false;
        var lista = C.materialesPared;
        bool hayLista = lista != null && lista.Count > 0;
        if (!hayLista && materialPared == null) return true;
        var m = MaterialDe(h);
        return hayLista ? lista.Contains(m) : m == materialPared;
    }

    /// <summary>Material de lo que ha tocado el rayo (en mallas con varios materiales, el del triángulo tocado).</summary>
    static Material MaterialDe(RaycastHit h)
    {
        var c = h.collider;
        if (c == null) return null;
        var rend = c.GetComponent<Renderer>();
        if (rend == null) rend = c.GetComponentInParent<Renderer>();
        if (rend == null) rend = c.GetComponentInChildren<Renderer>();
        if (rend == null) return null;
        var mats = rend.sharedMaterials;
        if (mats.Length == 1) return mats[0];
        if (c is MeshCollider mc && mc.sharedMesh != null && !mc.convex && h.triangleIndex >= 0)
        {
            var mesh = mc.sharedMesh;
            int tri = h.triangleIndex * 3, inicio = 0;
            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
            {
                int n = (int)mesh.GetIndexCount(s);
                if (tri < inicio + n) return mats[s];
                inicio += n;
            }
        }
        return mats.Length > 0 ? mats[0] : null;
    }

    /// <summary>
    /// Si <paramref name="punto"/> cae encima de su cuerpo (los segmentos a partir del tercero) o de su rastro, gira la
    /// cabeza hacia el lado contrario y devuelve true (no da el paso este fotograma).
    /// </summary>
    bool ApartarseDeSiMismo(Vector3 punto)
    {
        float radio = Mathf.Max(C.tamanoSegmento * E * 1.4f, C.separacion * E * 0.8f);
        float saltar = C.separacion * E * 2.5f; // lo más cercano a la cabeza no cuenta (es por donde viene)
        float recorrido = 0f;
        for (int i = 1; i < rastro.Count; i++)
        {
            recorrido += Vector3.Distance(rastro[i - 1].p, rastro[i].p);
            if (recorrido < saltar) continue;
            Vector3 d = punto - rastro[i].p;
            if (d.sqrMagnitude > radio * radio) continue;
            // Gira hacia el lado que le aleja de ese punto.
            Vector3 hacia = Vector3.ProjectOnPlane(rastro[i].p - pos, normal);
            float lado = Mathf.Sign(Vector3.Dot(Vector3.Cross(adelante, hacia), normal));
            if (lado == 0f) lado = 1f;
            adelante = (Quaternion.AngleAxis(-lado * C.giro * Time.deltaTime * 1.5f, normal) * adelante).normalized;
            return true;
        }
        return false;
    }

    /// <summary>Delante hay algo que no es su pared: gira y busca otro camino.</summary>
    void Esquivar()
    {
        float lado = Random.value < 0.5f ? -1f : 1f;
        adelante = (Quaternion.AngleAxis(lado * Random.Range(45f, 80f), normal) * adelante).normalized; // nunca media vuelta
        bloqueo += Time.deltaTime;
        if (estado == Estado.Paseando) NuevoDestinoPaseo();
    }

    // ------------------------------------------------------------------ Andar pegado a las superficies

    void CambiarSuperficie(Vector3 punto, Vector3 n)
    {
        Vector3 anteriorN = normal;
        pos = punto;
        normal = n.normalized;
        Vector3 f = Vector3.ProjectOnPlane(adelante, normal);
        if (f.sqrMagnitude < 0.05f) f = Vector3.ProjectOnPlane(anteriorN, normal); // de frente contra una pared: hacia arriba por ella
        if (f.sqrMagnitude > 1e-6f) adelante = f.normalized;
    }

    /// <summary>Anda por la superficie hacia <paramref name="objetivo"/>, serpenteando. Pasa a paredes y techos y rodea esquinas.</summary>
    bool Andar(Vector3 objetivo, float dt, float rapidez = 1f)
    {
        Vector3 deseada = Vector3.ProjectOnPlane(objetivo - pos, normal);
        if (deseada.sqrMagnitude < 1e-6f) return false;
        // No vuelve sobre sus pasos: si lo que quiere está detrás, gira en curva amplia hacia ese lado en vez de darse la vuelta.
        Vector3 haciaAdelante = Vector3.ProjectOnPlane(adelante, normal).normalized;
        if (!Desatascando && Vector3.Dot(deseada.normalized, haciaAdelante) < -0.2f)
        {
            float ladoGiro = Mathf.Sign(Vector3.Dot(Vector3.Cross(haciaAdelante, deseada), normal));
            if (ladoGiro == 0f) ladoGiro = 1f;
            deseada = Quaternion.AngleAxis(ladoGiro * 90f, normal) * haciaAdelante;
        }
        deseada = Quaternion.AngleAxis(Mathf.Sin(fase * C.ritmoSerpenteo * Mathf.PI * 2f) * C.serpenteo, normal) * deseada.normalized;
        adelante = Vector3.RotateTowards(adelante, deseada, C.giro * Mathf.Deg2Rad * dt, 0f);
        adelante = Vector3.ProjectOnPlane(adelante, normal).normalized;
        float paso = C.velocidad * E * rapidez * dt;
        float h = Altura;

        // 1) Algo delante: si es de su pared, pasa a esa superficie; si no (suelo, cristal, puerta...), es un obstáculo.
        if (Rayo(pos + normal * h * 0.6f, adelante, paso + Tamano * 1.2f, out var hc) && Vector3.Angle(hc.normal, normal) > 25f)
        {
            if (!EsPared(hc)) { Esquivar(); return false; }
            CambiarSuperficie(hc.point, hc.normal);
            return true;
        }
        // 2) Seguir pegado a la superficie.
        // Tampoco se mete en su propio cuerpo ni en el rastro por el que acaba de pasar: se aparta hacia el otro lado.
        if (!Desatascando && ApartarseDeSiMismo(pos + adelante * (paso + Tamano))) return false;
        Vector3 np = pos + adelante * paso;
        if (Rayo(np + normal * h * 1.5f, -normal, h * 4f, out var hd))
        {
            if (!EsPared(hd)) { Esquivar(); return false; } // se acaba su pared: no la pisa
            pos = hd.point;
            normal = Vector3.Slerp(normal, hd.normal, 0.5f).normalized;
            return true;
        }
        // 3) Borde convexo: rodearlo y pasar a la otra cara.
        if (Rayo(np - normal * h * 1.2f, -adelante, paso + Tamano * 2.5f, out var he))
        {
            if (!EsPared(he)) { Esquivar(); return false; }
            CambiarSuperficie(he.point, he.normal);
            return true;
        }
        // 4) Se ha quedado sin superficie: vuelve a agarrarse a la más cercana.
        Agarrarse(pos + normal * h);
        return false;
    }

    // ------------------------------------------------------------------ Bucle

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || segs.Count == 0) return;
        fase += dt;
        VigilarAtasco(dt);

        // Fuera de su pared (p.ej. en el marco o el cristal de la ventana): pisa lo que sea y vuelve a la pared más cercana;
        // si en unos segundos no llega, salta a ella.
        if (!entrando)
        {
            comprobarPared -= dt;
            if (comprobarPared <= 0f) { comprobarPared = 0.25f; fueraDeSuPared = !SobreSuPared(); if (fueraDeSuPared) puntoVuelta = PuntoDeSuPared(); }
            if (fueraDeSuPared)
            {
                tiempoFuera += dt;
                if (tiempoFuera > 3f) { CambiarSuperficie(puntoVuelta, normalVuelta); tiempoFuera = 0f; fueraDeSuPared = !SobreSuPared(); }
                else Andar(puntoVuelta, dt);
                arribaSuave = Vector3.Slerp(arribaSuave, normal, 1f - Mathf.Exp(-12f * dt)).normalized;
                GuardarRastro();
                ColocarCuerpo();
                AplicarMateriales();
                return;
            }
            tiempoFuera = 0f;
        }
        tiempoRumbo -= dt;
        if (estado == Estado.Paseando) tiempoCristales += dt;

        switch (estado)
        {
            case Estado.Entrando:
                // Cruza la ventana (pisando lo que sea) hasta la pared de dentro; ahí ya solo anda por su pared.
                Andar(destinoEntrada, dt);
                bool dentro = Vector3.Dot(pos - ventana, haciaDentro) > Tamano;
                // Termina de entrar al estar ya sobre su pared (si tarda, sigue el modo de volver a su pared).
                if ((dentro && SobreSuPared()) || tiempoEntrando > 8f) { entrando = false; VolverAPasear(); }
                tiempoEntrando += dt;
                break;
            case Estado.Paseando:
                if (tiempoRumbo <= 0f || Vector3.Distance(pos, destinoPaseo) < 0.2f * E) NuevoDestinoPaseo();
                if (Andar(destinoPaseo, dt)) bloqueo = 0f;
                if (tiempoCristales >= C.intervaloCristales)
                {
                    ElegirCristal();
                    if (objetivo != null) estado = Estado.IrACristal;
                    else tiempoCristales = 0f;
                }
                break;
            case Estado.IrACristal:
                if (objetivo == null || objetivo.PiezasRestantes == 0) { ElegirCristal(); if (objetivo == null) { VolverAPasear(); break; } }
                if (Andar(Centro(objetivo), dt)) bloqueo = 0f;
                // Lo golpea al llegar cerca; si su pared no le deja acercarse más, desde donde pueda (o lo deja si está lejos).
                float dc = DistanciaA(objetivo);
                if (dc < C.alcanceGolpe * E || (bloqueo > 0.4f && dc < C.alcanceGolpe * E * 3f)) { estado = Estado.Rompiendo; golpes = 0; siguienteGolpe = 0f; bloqueo = 0f; }
                else if (bloqueo > 3f) { anterior = objetivo; VolverAPasear(); }
                break;
            case Estado.Rompiendo:
                if (objetivo == null) { VolverAPasear(); break; }
                Andar(Centro(objetivo), dt, 0.25f); // se queda rondándolo mientras lo golpea
                siguienteGolpe -= dt;
                if (siguienteGolpe <= 0f)
                {
                    Golpear(objetivo);
                    golpes++;
                    siguienteGolpe = C.intervaloGolpes;
                    if (golpes >= C.golpesPorCristal || objetivo.PiezasRestantes == 0) { anterior = objetivo; VolverAPasear(); }
                }
                break;
        }

        GuardarRastro();
        arribaSuave = Vector3.Slerp(arribaSuave, normal, 1f - Mathf.Exp(-12f * dt)).normalized;
        ColocarCuerpo();
        AplicarMateriales();
    }

    /// <summary>
    /// Cada segundo mira si la cabeza ha avanzado. Si no: un rato sin las reglas de no girarse ni pisar su cuerpo, y otro
    /// destino. Si sigue atascado, se vuelve a agarrar a la pared más cercana.
    /// </summary>
    void VigilarAtasco(float dt)
    {
        relojAtasco += dt;
        if (relojAtasco < 1f) return;
        relojAtasco = 0f;
        bool quieto = Vector3.Distance(pos, posVigilada) < C.velocidad * E * 0.25f;
        posVigilada = pos;
        if (!quieto || estado == Estado.Rompiendo) { atascos = 0; return; }

        atascos++;
        desatascarHasta = Time.time + 1.2f;
        adelante = (Quaternion.AngleAxis(Random.Range(120f, 240f), normal) * adelante).normalized; // ahora sí, media vuelta
        if (estado == Estado.Paseando) NuevoDestinoPaseo();
        else if (estado == Estado.IrACristal && atascos >= 2) { anterior = objetivo; VolverAPasear(); }
        if (atascos >= 3)
        {
            Agarrarse(pos + normal * Altura); // el cuerpo sigue por el rastro que ya tenía
            atascos = 0;
        }
    }

    /// <summary>Rastro de la cabeza (lo siguen los segmentos).</summary>
    void GuardarRastro()
    {
        float paso = C.separacion * E * 0.15f;
        if (rastro.Count > 0 && (rastro[0].p - pos).sqrMagnitude <= paso * paso) return;
        rastro.Insert(0, new Rastro { p = pos, n = normal });
        float maxLargo = C.separacion * E * (segs.Count + 2);
        float largo = 0f;
        for (int i = 1; i < rastro.Count; i++)
        {
            largo += Vector3.Distance(rastro[i - 1].p, rastro[i].p);
            if (largo > maxLargo) { rastro.RemoveRange(i + 1, rastro.Count - i - 1); break; }
        }
    }

    // Volver a su pared.
    bool fueraDeSuPared;
    float comprobarPared, tiempoFuera;
    Vector3 puntoVuelta, normalVuelta = Vector3.up;

    /// <summary>¿Lo que tiene debajo es de su pared (sin excepciones)?</summary>
    bool SobreSuPared()
    {
        float h = Altura;
        return Rayo(pos + normal * h * 1.5f, -normal, h * 4f, out var hd) && EsParedDeVerdad(hd);
    }

    /// <summary>El punto de su pared más cercano (rayos en todas direcciones; si no, hacia el centro del mapa).</summary>
    Vector3 PuntoDeSuPared()
    {
        Vector3 o = pos + normal * Altura;
        float mejor = float.MaxValue;
        Vector3 punto = pos;
        for (int i = 0; i < 64; i++)
        {
            // Direcciones repartidas por la esfera (espiral de Fibonacci).
            float y = 1f - (i + 0.5f) * 2f / 64f;
            float r = Mathf.Sqrt(1f - y * y);
            float a = i * 2.39996f;
            var d = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            if (Rayo(o, d, 5f * E, out var h) && h.distance < mejor && EsParedDeVerdad(h)) { mejor = h.distance; punto = h.point; normalVuelta = h.normal; }
        }
        if (mejor < float.MaxValue) return punto;
        // Nada cerca: hacia el centro del mapa y, desde ahí, a la pared en su dirección.
        Vector3 centro = CentroMapa();
        Vector3 haciaAqui = pos - centro;
        haciaAqui.y = 0f;
        if (Rayo(centro, haciaAqui.sqrMagnitude > 1e-6f ? haciaAqui.normalized : Vector3.forward, 30f, out var hc) && EsParedDeVerdad(hc)) { normalVuelta = hc.normal; return hc.point; }
        return pos;
    }

    void VolverAPasear()
    {
        estado = Estado.Paseando;
        tiempoCristales = 0f;
        NuevoDestinoPaseo();
    }

    static Vector3 CentroMapa()
    {
        var mapa = GameObject.Find("Mapa1");
        return mapa != null ? mapa.transform.position : Vector3.zero;
    }

    /// <summary>Un punto de pared al azar: un rayo desde el centro del mapa hacia fuera, a una altura al azar.</summary>
    void NuevoDestinoPaseo()
    {
        tiempoRumbo = C.cambioDeRumbo * Random.Range(0.7f, 1.3f);
        Vector3 centro = CentroMapa();
        for (int intento = 0; intento < 8; intento++)
        {
            float a = Random.value * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), Random.Range(-0.3f, 0.5f), Mathf.Sin(a)).normalized;
            if (Rayo(centro, d, 30f, out var h) && Mathf.Abs(h.normal.y) < 0.7f && EsPared(h)) { destinoPaseo = h.point; return; }
        }
        destinoPaseo = pos + adelante;
    }

    /// <summary>Segmentos a lo largo del rastro de la cabeza, cada uno con la normal de la superficie por la que pasó.</summary>
    void ColocarCuerpo()
    {
        float sep = C.separacion * E;
        for (int i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            Rastro r = Muestra(i * sep, out Vector3 haciaDelante);
            Vector3 n = i == 0 ? arribaSuave : r.n;
            Vector3 dir = i == 0 ? adelante : haciaDelante;
            dir = Vector3.ProjectOnPlane(dir, n);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.ProjectOnPlane(adelante, n);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.ProjectOnPlane(Vector3.forward, n);
            Vector3 p = (i == 0 ? pos : r.p) + n * Altura;
            s.t.SetPositionAndRotation(p, Quaternion.LookRotation(dir.normalized, n));

            // La cola (la que se puede romper) late.
            bool cola = i == segs.Count - 1;
            float latido = cola ? 1f + Mathf.Abs(Mathf.Sin(fase * 5f)) * C.latidoCola : 1f;
            s.t.localScale = Vector3.one * s.tamano * latido;

            // Patas: del costado hacia fuera y hacia la pared, adelante y atrás en ola a lo largo del cuerpo.
            for (int k = 0; k < s.patas.Count; k++)
            {
                float lado = Mathf.Sign(s.ladoPata[k]);
                float onda = Mathf.Sin(fase * C.ritmoPatas * Mathf.PI * 2f - i * 0.9f + (lado > 0 ? Mathf.PI : 0f));
                Vector3 derecha = s.t.right * lado;
                Vector3 haciaFuera = Quaternion.AngleAxis(onda * C.aperturaPasos, s.t.up) * (derecha * 0.8f - s.t.up * 0.6f);
                Vector3 a = p + derecha * s.tamano * 0.7f + s.t.forward * s.zPata[k] * s.tamano * 1.4f;
                Vector3 b = a + haciaFuera.normalized * C.largoPata * E;
                Vector3 db = b - a;
                var pata = s.patas[k];
                pata.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(db.normalized, s.t.up));
                float g = C.grosorPata * E;
                pata.localScale = new Vector3(g, g, db.magnitude);
            }
        }
    }

    /// <summary>El punto del rastro a <paramref name="distancia"/> por detrás de la cabeza (y hacia dónde iba ahí).</summary>
    Rastro Muestra(float distancia, out Vector3 haciaDelante)
    {
        haciaDelante = adelante;
        if (rastro.Count == 0) return new Rastro { p = pos, n = normal };
        float recorrido = 0f;
        for (int i = 1; i < rastro.Count; i++)
        {
            float tramo = Vector3.Distance(rastro[i - 1].p, rastro[i].p);
            if (recorrido + tramo >= distancia && tramo > 1e-6f)
            {
                float u = (distancia - recorrido) / tramo;
                haciaDelante = (rastro[i - 1].p - rastro[i].p) / tramo;
                return new Rastro
                {
                    p = Vector3.Lerp(rastro[i - 1].p, rastro[i].p, u),
                    n = Vector3.Slerp(rastro[i - 1].n, rastro[i].n, u).normalized,
                };
            }
            recorrido += tramo;
        }
        // Más allá del rastro (al salir de la ventana): todo junto al final.
        var ultimo = rastro[rastro.Count - 1];
        if (rastro.Count > 1) haciaDelante = (rastro[rastro.Count - 2].p - ultimo.p).normalized;
        return ultimo;
    }

    // ------------------------------------------------------------------ Cristales de las paredes

    static Vector3 Centro(PyramidCube c) => c.GetComponent<Renderer>() is Renderer r ? r.bounds.center : c.transform.position;

    float DistanciaA(PyramidCube c)
    {
        var r = c.GetComponent<Renderer>();
        Vector3 p = r != null ? r.bounds.ClosestPoint(pos) : c.transform.position;
        return Vector3.Distance(p, pos);
    }

    /// <summary>El cristal con piezas más cercano (no el último que rompió), con algo de azar entre los 3 más cercanos.</summary>
    void ElegirCristal()
    {
        var cand = new List<(PyramidCube c, float d)>();
        foreach (var c in FindObjectsByType<PyramidCube>(FindObjectsSortMode.None))
        {
            if (c == anterior || c.PiezasRestantes == 0) continue;
            bool otro = false;
            foreach (var o in Todos) if (o != this && o.objetivo == c) { otro = true; break; }
            if (otro) continue;
            cand.Add((c, Vector3.Distance(c.transform.position, pos)));
        }
        if (cand.Count == 0) { objetivo = null; return; }
        cand.Sort((a, b) => a.d.CompareTo(b.d));
        objetivo = cand[Random.Range(0, Mathf.Min(3, cand.Count))].c;
    }

    /// <summary>Un golpe al cristal: suelta una pieza, que cae a la arena.</summary>
    void Golpear(PyramidCube c)
    {
        var r = c.GetComponent<Renderer>();
        Vector3 centro = Centro(c);
        Vector3 cabeza = segs[0].t.position;
        Vector3 dir = (centro - cabeza).normalized;
        foreach (var h in Physics.RaycastAll(cabeza, dir, Vector3.Distance(cabeza, centro) + 1f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponentInParent<PyramidCube>() == c) { c.Hit(h, dir); return; }
        // Si el rayo no lo encuentra, golpe en el punto del cristal más cercano a la cabeza.
        c.Hit(new RaycastHit { point = r != null ? r.bounds.ClosestPoint(cabeza) : centro, normal = -dir }, dir);
    }

    // ------------------------------------------------------------------ Daño

    /// <summary>¿Es la cola (el único segmento al que le hacen daño los disparos)?</summary>
    public bool EsCola(Transform segmento) => segs.Count > 0 && segs[segs.Count - 1].t == segmento;

    /// <summary>Le han disparado a un segmento. Devuelve true si era la cola (le ha hecho daño).</summary>
    public bool RecibirDisparo(Transform segmento)
    {
        if (!EsCola(segmento)) return false;
        var cola = segs[segs.Count - 1];
        cola.vida--;
        if (cola.vida > 0) return true;

        // Se rompe la cola.
        CrystalBurst.Spawn(cola.r, cola.t.position, cola.t.up, C.trocitosAlRomper, 1.2f * E, 0.45f, 0.35f);
        foreach (var p in cola.patas) if (p != null) Destroy(p.gameObject);
        Destroy(cola.t.gameObject);
        segs.RemoveAt(segs.Count - 1);
        if (segs.Count == 0) Destroy(gameObject); // era la cabeza: muerto
        return true;
    }
}

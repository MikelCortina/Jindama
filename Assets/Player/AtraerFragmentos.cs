using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Atraer y atrapar trozos de cristal (como los guantes de gravedad de Half-Life: Alyx).
/// - Clic derecho apuntando a un trozo de cristal que sube hacia el agujero del centro (<see cref="Absorcion"/>): la mano
///   izquierda hace el gesto de atraer y el trozo sale volando en arco hacia ella.
/// - Alrededor del jugador hay una esfera (trigger "Esfera de atrapada"). Si pulsas E mientras el trozo está dentro
///   y todavía no te ha dado, el trozo va con un lerp al centro de la palma izquierda y, al llegar, la mano hace la
///   animación de la E (GolpeAbajo): al cerrar el puño el cristal estalla en trocitos.
/// - Si en ese mismo momento pulsas el clic izquierdo en vez de la E, la mano lo estruja y se abre de golpe: el cristal
///   sale devuelto como una perdigonada (8 perdigones esparcidos hacia la mira) en vez de recuperar una bala, y el arma
///   no dispara.
/// - Si no lo atrapas, te golpea y el arma pierde una bala del cargador (como al disparar).
/// - Sin un trozo que atrapar, la E no hace nada.
/// Va en el jugador (el objeto con FirstPersonController); si no está, se añade solo al empezar la partida.
/// </summary>
[DefaultExecutionOrder(50)]
public class AtraerFragmentos : MonoBehaviour
{
    [Header("Referencias (vacías = automáticas)")]
    public Camera cam;
    [Tooltip("La mano izquierda (la que hace la animación de la E).")]
    public ManoJugador manoIzquierda;

    [Header("Atraer (clic derecho)")]
    public float alcance = 25f;
    [Tooltip("Margen al apuntar: radio del rayo con el que se busca el trozo.")]
    public float radioApuntar = 0.2f;
    public LayerMask capas = ~0;
    [Tooltip("Velocidad media del vuelo (marca cuánto tarda en llegar).")]
    public float velocidadVuelo = 8f;
    [Tooltip("Duración mínima y máxima del vuelo, en segundos.")]
    public Vector2 tiempoVuelo = new Vector2(0.5f, 1.1f);
    [Tooltip("Giro aleatorio del trozo mientras vuela.")]
    public float giroVuelo = 4f;

    [Header("Esfera de atrapada (E)")]
    public Key teclaAtrapar = Key.E;
    [Tooltip("Radio de la esfera alrededor del jugador, en unidades del jugador (1 = su altura, se escala con él).")]
    public float radioEsfera = 1.5f;
    [Tooltip("Dentro de la esfera el trozo se dirige hacia ti sin frenar (mantiene su velocidad): si no lo atrapas, te da.")]
    public bool dirigirHaciaTi = true;
    [Tooltip("Segundos del lerp hasta la palma.")]
    public float tiempoLerp = 0.12f;
    [Tooltip("Tamaño máximo del trozo en la mano (unidades del mundo). Si es más grande, se encoge mientras va a la palma.")]
    public float tamanoEnMano = 0.1f;
    [Tooltip("Segundos que la mano sujeta el cristal (agarre con retroceso) antes de cerrar el puño.")]
    public float sostener = 0.3f;
    [Tooltip("Segundos desde que empieza la animación de la E hasta que el puño se cierra y el cristal estalla.")]
    public float romperTras = 0.25f;
    [Tooltip("Arma de cristal: pierde una bala cuando un trozo te golpea. Vacía = la de la escena.")]
    public CrystalWeapon arma;

    [Header("Devolver (clic izquierdo con el cristal dentro de la esfera)")]
    [Tooltip("En vez de atraparlo con E, con clic izquierdo la mano lo estruja y lo devuelve como una perdigonada (no recupera bala).")]
    public bool devolverConClic = true;
    [Tooltip("Número de perdigones.")]
    [Min(1)] public int perdigones = 8;
    [Tooltip("Apertura del cono de la perdigonada (grados desde el centro de la mira).")]
    public float aperturaPerdigones = 7f;
    [Tooltip("Tamaño de cada perdigón respecto a una bala normal.")]
    public float tamanoPerdigon = 0.45f;
    [Tooltip("Velocidad de los perdigones (0 = la de las balas normales).")]
    public float velocidadPerdigones = 0f;

    public enum TipoPropiedad { Color, Float, Vector }

    [System.Serializable]
    public class PropiedadResalte
    {
        [Tooltip("Nombre de la propiedad del shader (p.ej. _BaseColor, _ColorDim, _ColorCelShadow).")]
        public string nombre = "_BaseColor";
        public TipoPropiedad tipo = TipoPropiedad.Color;
        [ColorUsage(true, true)] public Color color = Color.white;
        public float valor;
        public Vector4 vector;
    }

    [Header("Resalte en el radio de agarre (MaterialPropertyBlock)")]
    [Tooltip("Valores que toma el material del trozo cuando entra en el radio en el que se puede agarrar. Solo se aplican las propiedades que tenga su material.")]
    public List<PropiedadResalte> propiedadesEnRadio = new List<PropiedadResalte>
    {
        new PropiedadResalte { nombre = "_BaseColor", color = new Color(0.75f, 1f, 0.95f, 1f) },
        new PropiedadResalte { nombre = "_ColorDim", color = new Color(0.45f, 0.85f, 0.8f, 1f) },
    };
    [Tooltip("Rapidez del lerp hacia esos valores (y de vuelta).")]
    public float velocidadResalte = 25f;

    [Header("Estallido al cerrar el puño")]
    [Tooltip("Número de trocitos.")]
    public int trocitos = 16;
    [Tooltip("Velocidad de los trocitos (unidades del jugador por segundo).")]
    public float velocidadTrocitos = 3f;
    [Tooltip("Segundos hasta que desaparecen.")]
    public float vidaTrocitos = 0.4f;
    [Tooltip("Tamaño de los trocitos respecto al cristal.")]
    [Range(0.05f, 0.6f)] public float tamanoTrocitos = 0.3f;
    [Tooltip("Tamaño del estallido respecto al cristal en la mano (como al romper un cubo, que es 2).")]
    public float escalaEstallido = 2f;

    class Vuelo
    {
        public Rigidbody rb;
        public FracturePiece pieza;
        public FragmentoAtraido marca;
        public float tiempo;
        public bool dentro;
        public float distInicial; // distancia al borde de la esfera al atraerlo (para el resalte progresivo)
        public bool Choco => marca == null || marca.choco;
    }

    enum Estado { Libre, Lerp, Agarrado, EnMano, Devolviendo }

    readonly List<Vuelo> enVuelo = new List<Vuelo>();
    SphereCollider esfera;
    Collider[] colsJugador;

    Estado estado;
    Vuelo sujeto;
    Collider[] colsSujeto;
    float t;
    Vector3 desde, escalaOriginal, escalaMano;

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void AnadirAlJugador()
    {
        if (FindAnyObjectByType<AtraerFragmentos>() != null) return;
        var fps = FindAnyObjectByType<FirstPersonController>();
        if (fps != null) fps.gameObject.AddComponent<AtraerFragmentos>();
    }

    void Start()
    {
        if (cam == null) { var fps = GetComponent<FirstPersonController>(); if (fps != null) cam = fps.cam; }
        if (cam == null) cam = GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;

        if (manoIzquierda == null)
            foreach (var m in FindObjectsByType<ManoJugador>(FindObjectsSortMode.None))
                if (m.isActiveAndEnabled && m.LadoReal == ManoJugador.Lado.Izquierda) { manoIzquierda = m; break; }
        if (manoIzquierda != null) manoIzquierda.usarTecla = false; // la E la gestiono yo
        else Debug.LogWarning("AtraerFragmentos: no encuentro la mano izquierda (ManoJugador).", this);

        // Esfera alrededor del jugador, centrada a la altura de la cámara.
        var existente = transform.Find("Esfera de atrapada");
        var go = existente != null ? existente.gameObject : new GameObject("Esfera de atrapada");
        go.transform.SetParent(transform, false);
        go.layer = 2; // Ignore Raycast: que no la toquen los disparos ni los rayos
        if (cam != null) go.transform.position = cam.transform.position;
        esfera = go.GetComponent<SphereCollider>();
        if (esfera == null) esfera = go.AddComponent<SphereCollider>();
        esfera.isTrigger = true;
        esfera.radius = radioEsfera;

        colsJugador = GetComponentsInChildren<Collider>(true);
        cc = GetComponent<CharacterController>();
        if (arma == null) arma = GetComponentInChildren<CrystalWeapon>();
        if (arma == null) arma = FindAnyObjectByType<CrystalWeapon>();
        if (arma != null) arma.interceptarClic = ClicIzquierdo;
    }

    void OnDestroy()
    {
        if (arma != null && arma.interceptarClic == ClicIzquierdo) arma.interceptarClic = null;
    }

    /// <summary>Escala del jugador en el mundo (para pasar de unidades del jugador a unidades del mundo).</summary>
    float Escala => Mathf.Abs(transform.lossyScale.y) > 1e-4f ? Mathf.Abs(transform.lossyScale.y) : 1f;
    float RadioMundo => radioEsfera * Escala;

    CharacterController cc;
    /// <summary>Hacia dónde flota el trozo dentro de la esfera: el cuerpo (si no lo atrapas, te da).</summary>
    Vector3 Cuerpo => cam != null ? cam.transform.position - transform.up * 0.15f * Escala : transform.position;
    float RadioCuerpo => cc != null ? cc.radius * Escala : 0.3f * Escala;

    void OnValidate() { if (esfera != null) esfera.radius = radioEsfera; }

    bool EsDelJugador(Collider c) => c != null && c.transform.IsChildOf(transform);

    void Update()
    {
        var mouse = Mouse.current;
        // Clic derecho: gesto de atraer con la mano izquierda y atrae el cristal (que sube al agujero) al que apuntas.
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            if (manoIzquierda != null && estado == Estado.Libre) manoIzquierda.GestoAtraer();
            Atraer();
        }
        var kb = Keyboard.current;
        if (kb != null && kb[teclaAtrapar].wasPressedThisFrame) PulsarE();
        ComprobarVuelos();
    }

    // ------------------------------------------------------------------ Atraer

    /// <summary>Clic derecho: atrae el cristal suelto que estás apuntando (en el suelo, cayendo o subiendo hacia el agujero). Devuelve true si lo ha atraído.</summary>
    bool Atraer()
    {
        if (cam == null) return false;
        Ray ray = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
            ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Lo primero que no sea del jugador ni un trozo: la pared. Solo valen trozos que estén delante de ella.
        float pared = alcance;
        if (Physics.Raycast(ray, out var h0, alcance, capas, QueryTriggerInteraction.Ignore) && h0.collider.GetComponent<FracturePiece>() == null && !EsDelJugador(h0.collider))
            pared = h0.distance;

        FracturePiece mejor = null;
        float mejorD = float.MaxValue;
        foreach (var hit in Physics.SphereCastAll(ray, radioApuntar, alcance, capas, QueryTriggerInteraction.Ignore))
        {
            var fp = hit.collider.GetComponent<FracturePiece>();
            if (fp == null || fp.sujeta || hit.rigidbody == null) continue;
            float d = hit.distance > 0f ? hit.distance : Vector3.Dot(hit.collider.bounds.center - ray.origin, ray.direction);
            if (d > pared + radioApuntar * 2f) continue;
            // Preferir el más centrado en la mira y, a igualdad, el más cercano.
            float desvio = Vector3.Cross(ray.direction, hit.collider.bounds.center - ray.origin).magnitude;
            float score = desvio * 4f + d * 0.05f;
            if (score < mejorD) { mejorD = score; mejor = fp; }
        }
        if (mejor == null) return false;
        var absorcion = mejor.GetComponent<Absorcion>();
        if (absorcion != null) absorcion.Detener(); // deja de subir y vuelve a tener física
        return LanzarPieza(mejor);
    }

    /// <summary>
    /// Lanza un trozo hacia la mano izquierda del jugador en arco, como al atraerlo con el clic derecho: se puede
    /// atrapar con E dentro de la esfera y, si te da, pierdes una bala. Lo usan también las arañas.
    /// </summary>
    public bool LanzarPieza(FracturePiece mejor, float anticipacion = 0f)
    {
        if (mejor == null || cam == null) return false;
        var rb = mejor.GetComponent<Rigidbody>();
        if (rb == null) return false;
        for (int i = enVuelo.Count - 1; i >= 0; i--) if (enVuelo[i].rb == rb) Quitar(i);
        Vector3 destino = manoIzquierda != null ? manoIzquierda.CentroPalma : cam.transform.position + cam.transform.forward * 0.3f;
        Vector3 p = rb.worldCenterOfMass;
        float vuelo = Mathf.Clamp(Vector3.Distance(p, destino) / Mathf.Max(0.1f, velocidadVuelo), tiempoVuelo.x, tiempoVuelo.y);
        if (anticipacion > 0f)
        {
            // Apunta a donde vas a estar: tu velocidad horizontal por el tiempo de vuelo (dos pasadas, porque el vuelo
            // cambia al cambiar el destino).
            var fps = GetComponent<FirstPersonController>();
            Vector3 velJugador = fps != null ? fps.Velocity : Vector3.zero;
            velJugador.y = 0f;
            Vector3 destinoBase = destino;
            for (int i = 0; i < 2; i++)
            {
                destino = destinoBase + velJugador * vuelo * anticipacion;
                vuelo = Mathf.Clamp(Vector3.Distance(p, destino) / Mathf.Max(0.1f, velocidadVuelo), tiempoVuelo.x, tiempoVuelo.y);
            }
        }
        // Tiro parabólico que llega a la palma en 'vuelo' segundos.
        Vector3 v = (destino - p) / vuelo - 0.5f * Physics.gravity * vuelo;
        rb.WakeUp();
        rb.linearVelocity = v;
        rb.angularVelocity = Random.insideUnitSphere * giroVuelo;
        mejor.Rejuvenecer();
        var marca = mejor.GetComponent<FragmentoAtraido>();
        if (marca == null) marca = mejor.gameObject.AddComponent<FragmentoAtraido>();
        marca.jugador = transform;
        marca.choco = false;
        float dist0 = Mathf.Max(0.01f, Vector3.Distance(p, esfera != null ? esfera.transform.position : destino) - RadioMundo);
        enVuelo.Add(new Vuelo { rb = rb, pieza = mejor, marca = marca, distInicial = dist0 });
        Resaltar(rb.gameObject, 0f); // empieza el resalte (sube según se acerca)
        return true;
    }

    void ComprobarVuelos()
    {
        for (int i = enVuelo.Count - 1; i >= 0; i--)
        {
            var v = enVuelo[i];
            if (v.rb == null) { enVuelo.RemoveAt(i); continue; }
            v.tiempo += Time.deltaTime;
            // Resalte según se acerca: 0 al atraerlo, 1 justo al entrar en la esfera.
            if (!v.Choco && esfera != null)
            {
                float falta = Vector3.Distance(v.rb.worldCenterOfMass, esfera.transform.position) - RadioMundo;
                Resaltar(v.rb.gameObject, v.dentro ? 1f : 1f - Mathf.Clamp01(falta / v.distInicial));
            }
            // Si choca con el jugador ya no se puede atrapar; si nunca llega, se olvida.
            if (v.marca != null && v.marca.choco && arma != null) arma.PerderBala(); // te ha dado: pierdes una bala
            if (v.Choco || v.tiempo > tiempoVuelo.y + 3f) Quitar(i);
        }
    }

    void Quitar(int i)
    {
        var v = enVuelo[i];
        enVuelo.RemoveAt(i);
        if (v.rb != null && v.dentro) v.rb.useGravity = true;
        if (v.marca != null) Destroy(v.marca);
        if (v.rb != null) Resaltar(v.rb.gameObject, false);
    }

    /// <summary>Lleva el material del trozo a los valores de resalte (o de vuelta a los suyos) con un lerp rápido.</summary>
    void Resaltar(GameObject go, bool encendido)
    {
        var r = go.GetComponent<ResalteCristal>();
        if (r == null)
        {
            if (!encendido || propiedadesEnRadio == null || propiedadesEnRadio.Count == 0) return;
            r = go.AddComponent<ResalteCristal>();
        }
        r.propiedades = propiedadesEnRadio;
        r.velocidad = velocidadResalte;
        r.objetivo = encendido ? 1f : 0f;
        r.directo = encendido; // apagar (si te da) sí va con lerp rápido
    }

    /// <summary>Resalte con una cantidad exacta (0..1), sin lerp: lo marca la distancia.</summary>
    void Resaltar(GameObject go, float cantidad)
    {
        if (propiedadesEnRadio == null || propiedadesEnRadio.Count == 0) return;
        var r = go.GetComponent<ResalteCristal>();
        if (r == null) r = go.AddComponent<ResalteCristal>();
        r.propiedades = propiedadesEnRadio;
        r.velocidad = velocidadResalte;
        r.objetivo = cantidad;
        r.directo = true;
    }

    // Dentro de la esfera: sigue a la misma velocidad, recto hacia ti (sin gravedad), hasta que lo atrapas o te da.
    void FixedUpdate()
    {
        if (esfera == null) return;
        Vector3 cuerpo = Cuerpo;
        foreach (var v in enVuelo)
        {
            if (v.rb == null || v.Choco || v.rb.isKinematic) continue;
            if (!v.dentro && DentroDeEsfera(v))
            {
                v.dentro = true;
                Resaltar(v.rb.gameObject, true); // ya se puede agarrar
                v.rb.useGravity = false;
            }
            if (!v.dentro) continue;
            Vector3 hacia = cuerpo - v.rb.worldCenterOfMass;
            // Si ya está pegado al cuerpo, cuenta como golpe aunque no salte OnCollisionEnter.
            if (hacia.magnitude < RadioCuerpo + v.rb.GetComponent<Collider>().bounds.extents.magnitude * 0.5f) { v.marca.choco = true; continue; }
            // Misma velocidad, pero apuntando a ti (sin frenar).
            if (dirigirHaciaTi) v.rb.linearVelocity = hacia.normalized * v.rb.linearVelocity.magnitude;
        }
    }

    bool DentroDeEsfera(Vuelo v)
    {
        if (esfera == null) return false;
        return Vector3.Distance(v.rb.worldCenterOfMass, esfera.transform.position) <= RadioMundo;
    }

    // ------------------------------------------------------------------ Atrapar

    /// <summary>El cristal atraído que está dentro de la esfera y más cerca (null si no hay ninguno que atrapar).</summary>
    Vuelo Atrapable()
    {
        Vuelo mejor = null;
        float mejorD = float.MaxValue;
        foreach (var v in enVuelo)
        {
            if (v.rb == null || v.Choco || !DentroDeEsfera(v)) continue;
            float d = Vector3.Distance(v.rb.worldCenterOfMass, esfera.transform.position);
            if (d < mejorD) { mejorD = d; mejor = v; }
        }
        return mejor;
    }

    void PulsarE()
    {
        if (estado != Estado.Libre) return;
        var mejor = Atrapable();
        // La animación de la E solo sale si hay un cristal que atrapar.
        if (mejor == null || manoIzquierda == null) return;
        Sujetar(mejor);
        estado = Estado.Lerp;
        // Feedback de que lo has cogido a tiempo: la mano va a por él con los dedos abiertos.
        manoIzquierda.EmpezarAgarre(desde - manoIzquierda.CentroPalma);
    }

    /// <summary>
    /// Clic izquierdo (lo llama el arma antes de disparar): si hay un cristal que atrapar, la mano lo estruja y lo
    /// devuelve como una perdigonada, y el arma no dispara. Si no, devuelve false y el arma dispara normal.
    /// </summary>
    bool ClicIzquierdo()
    {
        if (!devolverConClic || estado != Estado.Libre || manoIzquierda == null || !isActiveAndEnabled) return false;
        var mejor = Atrapable();
        if (mejor == null) return false;
        Sujetar(mejor);
        estado = Estado.Devolviendo;
        manoIzquierda.GestoEscopeta();
        return true;
    }

    /// <summary>Lo quita de los vuelos y lo deja sin física, listo para llevarlo a la palma.</summary>
    void Sujetar(Vuelo mejor)
    {
        int idx = enVuelo.IndexOf(mejor);
        if (idx >= 0) Quitar(idx);
        Resaltar(mejor.rb.gameObject, true); // sigue resaltado mientras lo tienes
        sujeto = mejor;
        sujeto.pieza.sujeta = true;
        sujeto.rb.isKinematic = true;
        sujeto.rb.interpolation = RigidbodyInterpolation.None;
        colsSujeto = sujeto.rb.GetComponentsInChildren<Collider>();
        foreach (var c in colsSujeto) c.enabled = false;

        desde = sujeto.rb.worldCenterOfMass;
        escalaOriginal = sujeto.rb.transform.localScale;
        var r = sujeto.rb.GetComponent<Renderer>();
        float tam = r != null ? r.bounds.size.magnitude : 0f;
        escalaMano = tam > tamanoEnMano && tam > 0f ? escalaOriginal * (tamanoEnMano / tam) : escalaOriginal;
        t = 0f;
    }

    // En LateUpdate: la palma ya tiene la pose de este fotograma (el Animator se evalúa antes).
    void LateUpdate()
    {
        if (estado == Estado.Libre || sujeto == null) return;
        if (sujeto.rb == null) { estado = Estado.Libre; sujeto = null; return; }

        var tr = sujeto.rb.transform;
        Vector3 palma = manoIzquierda.CentroPalma;
        // El pivote de la pieza no tiene por qué ser su centro: llevar el centro de la malla a la palma.
        Vector3 centroLocal = sujeto.rb.centerOfMass;
        t += Time.deltaTime;

        if (estado == Estado.Devolviendo)
        {
            // Va a la palma mientras el puño se cierra; al abrirse la mano de golpe, sale la perdigonada.
            float momento = manoIzquierda.MomentoAbrirEscopeta;
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, Mathf.Min(tiempoLerp, manoIzquierda.escopetaCerrar)));
            float k = u * u * (3f - 2f * u);
            tr.localScale = Vector3.Lerp(escalaOriginal, escalaMano, k);
            tr.position = Vector3.Lerp(desde, palma, k) - tr.TransformVector(centroLocal);
            if (t >= momento) Devolver(palma);
            return;
        }

        if (estado == Estado.Lerp)
        {
            float u = tiempoLerp > 0f ? Mathf.Clamp01(t / tiempoLerp) : 1f;
            float k = u * u * (3f - 2f * u);
            tr.localScale = Vector3.Lerp(escalaOriginal, escalaMano, k);
            Vector3 centro = Vector3.Lerp(desde, palma, k);
            tr.position = centro - tr.TransformVector(centroLocal);
            if (u >= 1f)
            {
                // Llega a la palma: los dedos se cierran alrededor y la mano encaja el golpe.
                estado = Estado.Agarrado;
                t = 0f;
                manoIzquierda.CerrarAgarre(palma - desde);
            }
            return;
        }

        if (estado == Estado.Agarrado)
        {
            tr.position = palma - tr.TransformVector(centroLocal);
            if (t >= sostener)
            {
                estado = Estado.EnMano;
                t = 0f;
                manoIzquierda.SoltarAgarre();
                manoIzquierda.EjecutarE(); // ahora sí: puño (y al cerrarse, el cristal estalla)
            }
            return;
        }

        // En la mano: sigue a la palma hasta que el puño se cierra, y entonces estalla.
        tr.position = palma - tr.TransformVector(centroLocal);
        if (t >= romperTras) Romper(palma);
    }

    /// <summary>Copia de <paramref name="original"/> con los valores finales de <see cref="propiedadesEnRadio"/>.</summary>
    Material MaterialResaltado(Material original)
    {
        if (original == null || propiedadesEnRadio == null || propiedadesEnRadio.Count == 0) return original;
        var mat = new Material(original) { name = original.name + " (resaltado)" };
        foreach (var p in propiedadesEnRadio)
        {
            if (p == null || string.IsNullOrEmpty(p.nombre) || !mat.HasProperty(p.nombre)) continue;
            switch (p.tipo)
            {
                case TipoPropiedad.Color: mat.SetColor(p.nombre, p.color); break;
                case TipoPropiedad.Float: mat.SetFloat(p.nombre, p.valor); break;
                case TipoPropiedad.Vector: mat.SetVector(p.nombre, p.vector); break;
            }
        }
        return mat;
    }

    /// <summary>Te has comido un cristal (lo has atraído y roto en el puño con la E): recuperas una bala.</summary>
    public static event System.Action CristalComido;

    /// <summary>El cristal se rompe en el puño: trocitos como al romper un cubo, y desaparece.</summary>
    void Romper(Vector3 palma)
    {
        var v = sujeto;
        var r = v.rb.GetComponent<Renderer>();
        if (r != null)
        {
            float radio = r.bounds.extents.magnitude * 0.6f * escalaEstallido;
            Vector3 fuera = cam != null ? (palma - cam.transform.position) : Vector3.up;
            // Los trocitos salen con el color final del resalte: copia del material con esos valores.
            var mat = MaterialResaltado(r.sharedMaterial);
            CrystalBurst.Spawn(mat, r.gameObject.layer, palma, fuera, radio, trocitos,
                               velocidadTrocitos * Escala, vidaTrocitos, tamanoTrocitos, transform); // en el espacio del jugador: van con él
            if (mat != r.sharedMaterial) Destroy(mat, vidaTrocitos * 1.5f + 0.5f); // cuando ya no quedan trocitos
        }
        if (arma != null) arma.RecuperarBala(); // atrapar un cristal te devuelve una bala
        CristalComido?.Invoke();
        Destroy(v.rb.gameObject);
        sujeto = null;
        colsSujeto = null;
        estado = Estado.Libre;
    }

    /// <summary>La mano se abre de golpe: el cristal estalla y sale como una perdigonada hacia la mira (no recupera bala).</summary>
    void Devolver(Vector3 palma)
    {
        var v = sujeto;
        var r = v.rb.GetComponent<Renderer>();
        if (r != null)
        {
            float radio = r.bounds.extents.magnitude * 0.6f * escalaEstallido;
            Vector3 fuera = cam != null ? cam.transform.forward : Vector3.forward;
            var mat = MaterialResaltado(r.sharedMaterial);
            CrystalBurst.Spawn(mat, r.gameObject.layer, palma, fuera, radio, trocitos,
                               velocidadTrocitos * Escala, vidaTrocitos, tamanoTrocitos, transform);
            if (mat != r.sharedMaterial) Destroy(mat, vidaTrocitos * 1.5f + 0.5f);
        }
        if (arma != null) arma.DispararPerdigones(palma, perdigones, aperturaPerdigones, tamanoPerdigon, velocidadPerdigones);
        Destroy(v.rb.gameObject);
        sujeto = null;
        colsSujeto = null;
        estado = Estado.Libre;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Vector3 c = esfera != null ? esfera.transform.position : (cam != null ? cam.transform.position : transform.position);
        Gizmos.DrawWireSphere(c, radioEsfera);
    }
}

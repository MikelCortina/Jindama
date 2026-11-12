using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manos del jugador en primera persona.
/// - Las dos: idle moviendo los dedos (como la garra de GolpeAbajo).
/// - Derecha (la del cristal): al disparar el CrystalWeapon cierra el puño y lo vuelve a abrir.
/// - Izquierda: al pulsar E cierra el puño y lo lleva contra la cámara.
/// Con "Auto", el lado se decide por la posición de la mano respecto a la cámara.
/// El acercamiento a la cámara se hace aquí (no en el clip) para respetar dónde hayas colocado la mano.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ManoJugador : MonoBehaviour
{
    public const string EstadoIdle = "Idle";
    public const string EstadoDisparo = "Disparo";
    public const string EstadoPuno = "PunoCamara";
    public const string EstadoAbrir = "CerrarAbrir";
    public const string EstadoGolpe = "GolpeAbajo";

    public enum Lado { Auto, Derecha, Izquierda }
    public enum AnimacionE { GolpeAbajo, CerrarAbrir, PunoCamara }

    [Tooltip("Auto: la que está a la derecha de la cámara reacciona al disparo; la de la izquierda, a la tecla.")]
    public Lado lado = Lado.Auto;
    [Tooltip("Controlador con los estados Idle / Disparo / PunoCamara. Se pone al empezar, aunque el Animator tenga otro.")]
    public RuntimeAnimatorController controlador;
    [Tooltip("Si se deja vacío, usa la cámara padre o la principal.")]
    public Camera cam;
    [Tooltip("Si se deja vacío, busca el arma de cristal de la escena.")]
    public CrystalWeapon arma;

    [Header("Mano izquierda")]
    public Key teclaPuno = Key.E;
    [Tooltip("GolpeAbajo: garra, puño y golpe hacia abajo (vídeo 23.27.09). CerrarAbrir: palma arriba, puño, se abre. PunoCamara: puño contra la cámara.")]
    public AnimacionE animacionE = AnimacionE.GolpeAbajo;
    [Tooltip("Segundos de mezcla desde el idle hasta la animación de la E.")]
    public float mezclaAbrir = 0.25f;

    [Header("Puño contra la cámara (solo si Animacion E = PunoCamara)")]
    [Tooltip("Adónde llega la muñeca, en el espacio de la cámara (Z = distancia delante).")]
    public Vector3 puntoCamara = new Vector3(0.0f, -0.05f, 0.12f);
    [Tooltip("Espera antes de moverse, para que le dé tiempo a cerrar el puño.")]
    public float retraso = 0.08f;
    public float tiempoIda = 0.14f;
    public float tiempoQuieto = 0.3f;
    public float tiempoVuelta = 0.35f;
    [Tooltip("Baja el near clip de la cámara (solo en juego) para que el puño no se corte al acercarse.")]
    public bool ajustarNearClip = true;

    [Header("Sway")]
    [Tooltip("Usar el mismo sway que el arma de cristal (balanceo al mirar, moverse, andar y saltar). La mano va dentro de un pivote con un WeaponSway copiado del arma.")]
    public bool swayComoElArma = true;

    Animator anim;
    Transform mano;
    Lado ladoReal;
    Coroutine golpe;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (controlador != null) anim.runtimeAnimatorController = controlador;
        mano = transform.Find("Antebrazo/Mano");
    }

    void Start()
    {
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        if (swayComoElArma) PonerSway();

        ladoReal = lado;
        if (ladoReal == Lado.Auto)
        {
            Vector3 p = mano != null ? mano.position : transform.position;
            ladoReal = cam != null && cam.transform.InverseTransformPoint(p).x < 0f ? Lado.Izquierda : Lado.Derecha;
        }

        if (ladoReal == Lado.Derecha)
        {
            if (arma == null) arma = FindAnyObjectByType<CrystalWeapon>();
            if (arma != null) arma.Fired += OnDisparo;
            else Debug.LogWarning("ManoJugador: no hay CrystalWeapon en la escena; la mano derecha no reaccionará al disparo.", this);
        }
        else if (animacionE == AnimacionE.PunoCamara && ajustarNearClip && cam != null && cam.nearClipPlane > 0.03f)
        {
            cam.nearClipPlane = 0.03f;
        }

        anim.Play(EstadoIdle, 0, Random.value); // desfase para que las dos manos no se muevan igual
    }

    void OnDestroy()
    {
        if (arma != null) arma.Fired -= OnDisparo;
    }

    void OnDisparo() => anim.CrossFadeInFixedTime(EstadoDisparo, 0.03f, 0, 0f);

    /// <summary>Si es false, la E la gestiona otro script (AtraerFragmentos) y llama a <see cref="EjecutarE"/>.</summary>
    [System.NonSerialized] public bool usarTecla = true;

    /// <summary>Hueso de la mano (la palma está entre este y la base del dedo corazón).</summary>
    public Transform Mano => mano != null ? mano : (mano = transform.Find("Antebrazo/Mano"));

    /// <summary>Centro de la palma, en el mundo.</summary>
    public Vector3 CentroPalma
    {
        get
        {
            var m = Mano;
            if (m == null) return transform.position;
            var c = m.Find("Corazon1");
            return c != null ? Vector3.Lerp(m.position, c.position, 0.55f) : m.position;
        }
    }

    /// <summary>Lado que usa de verdad (con Auto, según dónde esté respecto a la cámara).</summary>
    public Lado LadoReal
    {
        get
        {
            if (lado != Lado.Auto) return lado;
            var c = cam != null ? cam : (GetComponentInParent<Camera>() ?? Camera.main);
            return c != null && c.transform.InverseTransformPoint(Mano != null ? Mano.position : transform.position).x < 0f ? Lado.Izquierda : Lado.Derecha;
        }
    }

    void Update()
    {
        if (!usarTecla || ladoReal != Lado.Izquierda || golpe != null) return;
        var kb = Keyboard.current;
        if (kb == null || !kb[teclaPuno].wasPressedThisFrame) return;
        EjecutarE();
    }

    /// <summary>La animación de la tecla E (la que haya en Animacion E).</summary>
    public void EjecutarE()
    {
        if (golpe != null) return;
        if (animacionE == AnimacionE.GolpeAbajo) anim.CrossFadeInFixedTime(EstadoGolpe, mezclaAbrir, 0, 0f);
        else if (animacionE == AnimacionE.CerrarAbrir) anim.CrossFadeInFixedTime(EstadoAbrir, mezclaAbrir, 0, 0f);
        else golpe = StartCoroutine(Golpe());
    }

    IEnumerator Golpe()
    {
        anim.CrossFadeInFixedTime(EstadoPuno, 0.05f, 0, 0f);

        Vector3 basePos = transform.localPosition;
        Vector3 desde = mano != null ? mano.position : transform.position;
        Vector3 delta = cam != null ? cam.transform.TransformPoint(puntoCamara) - desde : Vector3.zero;
        Vector3 deltaLocal = transform.parent != null ? transform.parent.InverseTransformVector(delta) : delta;

        float t = 0f, total = retraso + tiempoIda + tiempoQuieto + tiempoVuelta;
        while (t < total)
        {
            t += Time.deltaTime;
            float k;
            if (t < retraso) k = 0f;
            else if (t < retraso + tiempoIda) { float u = (t - retraso) / tiempoIda; k = u * u; }     // acelera hacia la cámara
            else if (t < retraso + tiempoIda + tiempoQuieto) k = 1f;
            else { float u = Mathf.Clamp01((t - retraso - tiempoIda - tiempoQuieto) / tiempoVuelta); k = 1f - u * u * (3f - 2f * u); }
            transform.localPosition = basePos + deltaLocal * k;
            yield return null;
        }
        transform.localPosition = basePos;
        golpe = null;
    }

    /// <summary>
    /// Mete la mano en un pivote (en su sitio, alineado con la cámara) con un WeaponSway con los mismos valores que el
    /// del arma. Así se balancea igual que el arma sin pisar lo que la mano hace con su propia raíz (agarre, puño).
    /// </summary>
    void PonerSway()
    {
        if (transform.parent == null || transform.parent.GetComponent<WeaponSway>() != null) return;
        var armaRef = arma != null ? arma : FindAnyObjectByType<CrystalWeapon>();
        var swayArma = armaRef != null ? armaRef.GetComponent<WeaponSway>() : null;

        var pivote = new GameObject("Sway " + name).transform;
        pivote.SetParent(transform.parent, false);
        pivote.localPosition = transform.localPosition;
        pivote.localRotation = Quaternion.identity;
        pivote.localScale = Vector3.one;
        transform.SetParent(pivote, true);

        var sway = pivote.gameObject.AddComponent<WeaponSway>();
        if (swayArma != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(swayArma), sway); // mismos valores que el arma
    }

    // ------------------------------------------------------------------ Agarre (feedback al atrapar un cristal)
    // Capa procedural encima del Animator: los dedos se abren al ir a por el cristal, se cierran de golpe alrededor
    // de él al llegar, y la mano retrocede con el impacto (muelle amortiguado). La usa AtraerFragmentos.

    [Header("Agarre al atrapar un cristal")]
    [Tooltip("Cuánto se adelanta la mano hacia el cristal al ir a por él (en largos de mano).")]
    public float agarreAlcance = 0.35f;
    [Tooltip("Retroceso de la mano por el impacto del cristal (en largos de mano).")]
    public float agarreRetroceso = 0.45f;
    [Tooltip("Giro de la muñeca por el impacto (grados).")]
    public float agarreGiro = 14f;
    [Tooltip("Grados que se abren los dedos al ir a por el cristal (base, medio, punta).")]
    public Vector3 agarreAbrir = new Vector3(30f, 35f, 25f);
    [Tooltip("Grados que se cierran los dedos alrededor del cristal (base, medio, punta).")]
    public Vector3 agarreCerrar = new Vector3(24f, 28f, 20f);

    static readonly string[] Dedos = { "Indice", "Corazon", "Anular", "Menique", "Pulgar" };
    Transform[,] huesosDedo;
    enum FaseAgarre { Nada, Alcanzar, Agarrado, Soltando }
    FaseAgarre fase;
    float curl, curlObjetivo, peso, tAgarre;
    Vector3 dirAlcance, muellePos, muelleVel, ejeGiro;
    float muelleAng, muelleAngVel;
    Vector3 agarreBasePos;
    Quaternion agarreBaseRot;

    float LargoMano
    {
        get
        {
            var m = Mano;
            var c = m != null ? m.Find("Corazon1/Corazon2/Corazon3") : null;
            return c != null ? Vector3.Distance(m.position, c.position) : 0.1f;
        }
    }

    void BuscarDedos()
    {
        if (huesosDedo != null || Mano == null) return;
        huesosDedo = new Transform[Dedos.Length, 3];
        for (int d = 0; d < Dedos.Length; d++)
        {
            Transform h = Mano;
            for (int j = 0; j < 3; j++)
            {
                h = h != null ? h.Find(Dedos[d] + (j + 1)) : null;
                huesosDedo[d, j] = h;
            }
        }
    }

    /// <summary>Va a por el cristal: la mano se adelanta hacia él y abre los dedos.</summary>
    public void EmpezarAgarre(Vector3 haciaCristal)
    {
        // Si estaba haciendo el gesto de atraer, vuelve a su sitio antes de guardar la pose base del agarre.
        if (gestoT >= 0f) { transform.localPosition = gestoBasePos; transform.localRotation = gestoBaseRot; gestoT = -1f; }
        BuscarDedos();
        if (fase == FaseAgarre.Nada) { agarreBasePos = transform.localPosition; agarreBaseRot = transform.localRotation; }
        fase = FaseAgarre.Alcanzar;
        tAgarre = 0f;
        dirAlcance = haciaCristal.sqrMagnitude > 1e-8f ? haciaCristal.normalized : Vector3.zero;
        curlObjetivo = -1f;
        muellePos = muelleVel = Vector3.zero;
        muelleAng = muelleAngVel = 0f;
    }

    /// <summary>El cristal llega a la palma: los dedos se cierran de golpe alrededor y la mano encaja el impacto.</summary>
    public void CerrarAgarre(Vector3 direccionImpacto)
    {
        if (fase == FaseAgarre.Nada) EmpezarAgarre(Vector3.zero);
        fase = FaseAgarre.Agarrado;
        tAgarre = 0f;
        curlObjetivo = 1f;
        Vector3 d = direccionImpacto.sqrMagnitude > 1e-8f ? direccionImpacto.normalized : Vector3.zero;
        // El impacto empuja la mano hacia donde iba el cristal y la tuerce un poco.
        muelleVel = d * agarreRetroceso * LargoMano * 14f;
        ejeGiro = Vector3.Cross(d, cam != null ? cam.transform.up : Vector3.up);
        if (ejeGiro.sqrMagnitude < 1e-6f) ejeGiro = cam != null ? cam.transform.right : Vector3.right;
        ejeGiro.Normalize();
        muelleAngVel = agarreGiro * 16f;
    }

    /// <summary>Quita la capa de agarre poco a poco (cuando empieza la animación de la E).</summary>
    public void SoltarAgarre()
    {
        if (fase != FaseAgarre.Nada) { fase = FaseAgarre.Soltando; tAgarre = 0f; }
    }

    void LateUpdate()
    {
        if (escopetaT >= 0f) ActualizarEscopeta();
        else if (fase != FaseAgarre.Nada) ActualizarAgarre();
        else if (gestoT >= 0f) ActualizarGesto();
    }

    // ------------------------------------------------------------------ Gesto de devolver (clic izquierdo al atrapar)
    // Garra medio abierta -> aprieta el puño de golpe (estruja el cristal) -> la mano se abre de golpe con los dedos muy
    // separados, empujando hacia delante (sale la perdigonada) -> aguanta abierta con el retroceso -> vuelve a su sitio.
    // Lo lanza AtraerFragmentos al pulsar el clic izquierdo con un cristal dentro de la esfera.

    [Header("Gesto de devolver (clic izquierdo al atrapar)")]
    [Tooltip("Segundos que dura el gesto entero.")]
    public float escopetaDuracion = 0.75f;
    [Tooltip("Segundos hasta que el puño se cierra del todo (el cristal llega a la palma).")]
    public float escopetaCerrar = 0.12f;
    [Tooltip("Segundos que aguanta el puño apretado antes de abrirse de golpe (ahí sale la perdigonada).")]
    public float escopetaApretar = 0.08f;
    [Tooltip("Grados que se cierran los dedos al apretar el puño (base, medio, punta).")]
    public Vector3 escopetaPuno = new Vector3(70f, 85f, 60f);
    [Tooltip("Grados que se abren (estiran hacia atrás) los dedos al soltar (base, medio, punta).")]
    public Vector3 escopetaAbrir = new Vector3(30f, 20f, 15f);
    [Tooltip("Grados que se separan los dedos entre sí al abrirse.")]
    public float escopetaSeparar = 14f;
    [Tooltip("Eje local de los huesos de los dedos para separarlos (el de doblar es el X).")]
    public Vector3 escopetaEjeSeparar = Vector3.forward;
    [Tooltip("Cuánto empuja la mano hacia delante al abrirse (en largos de mano).")]
    public float escopetaEmpuje = 0.45f;
    [Tooltip("Cuánto se echa hacia atrás al apretar el puño (en largos de mano).")]
    public float escopetaCarga = 0.15f;
    [Tooltip("Grados del retroceso de la muñeca hacia arriba al soltar.")]
    public float escopetaRetroceso = 18f;

    float escopetaT = -1f;
    Vector3 escopetaBasePos;
    Quaternion escopetaBaseRot;

    /// <summary>Segundos desde el inicio del gesto de devolver hasta que la mano se abre (cuando sale la perdigonada).</summary>
    public float MomentoAbrirEscopeta => escopetaCerrar + escopetaApretar;

    /// <summary>Gesto de devolver el cristal como una perdigonada. Corta el gesto de atraer y el agarre si los hubiera.</summary>
    public void GestoEscopeta()
    {
        if (escopetaT >= 0f) return;
        // Vuelve a la pose base de lo que estuviera haciendo antes de guardarla.
        if (gestoT >= 0f) { transform.localPosition = gestoBasePos; transform.localRotation = gestoBaseRot; gestoT = -1f; }
        if (fase != FaseAgarre.Nada) { transform.localPosition = agarreBasePos; transform.localRotation = agarreBaseRot; fase = FaseAgarre.Nada; peso = 0f; curl = 0f; }
        escopetaBasePos = transform.localPosition;
        escopetaBaseRot = transform.localRotation;
        escopetaT = 0f;
        BuscarDedos();
    }

    void ActualizarEscopeta()
    {
        escopetaT += Time.deltaTime;
        float t1 = Mathf.Max(0.01f, escopetaCerrar), t2 = t1 + Mathf.Max(0f, escopetaApretar);
        float total = Mathf.Max(t2 + 0.1f, escopetaDuracion);
        float t3 = t2 + 0.07f;                         // se abre de golpe
        float t4 = Mathf.Lerp(t3, total, 0.55f);       // aguanta abierta
        float dedos, empuje, giro, separar;            // dedos: +1 puño, -1 abierta
        float t = escopetaT;
        if (t < t1) { float u = Mathf.SmoothStep(0f, 1f, t / t1); dedos = u; empuje = -escopetaCarga * u; giro = 0f; separar = 0f; }
        else if (t < t2) { dedos = 1f + Mathf.Sin((t - t1) * 70f) * 0.04f; empuje = -escopetaCarga; giro = 0f; separar = 0f; } // aprieta (tiembla)
        else if (t < t3) { float u = (t - t2) / (t3 - t2); u = 1f - (1f - u) * (1f - u); dedos = Mathf.Lerp(1f, -1f, u); empuje = Mathf.Lerp(-escopetaCarga, escopetaEmpuje, u); giro = escopetaRetroceso * u; separar = u; }
        else if (t < t4) { float u = (t - t3) / (t4 - t3); dedos = -1f; empuje = Mathf.Lerp(escopetaEmpuje, escopetaEmpuje * 0.6f, u); giro = escopetaRetroceso * (1f - 0.3f * u); separar = 1f; }
        else { float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - t4) / (total - t4))); dedos = Mathf.Lerp(-1f, 0f, u); empuje = Mathf.Lerp(escopetaEmpuje * 0.6f, 0f, u); giro = Mathf.Lerp(escopetaRetroceso * 0.7f, 0f, u); separar = 1f - u; }

        transform.localPosition = escopetaBasePos;
        transform.localRotation = escopetaBaseRot;
        Vector3 haciaDelante = cam != null ? cam.transform.forward : transform.forward;
        transform.position += haciaDelante * empuje * LargoMano;
        if (Mano != null && cam != null && Mathf.Abs(giro) > 1e-3f) transform.RotateAround(Mano.position, cam.transform.right, -giro);

        Vector3 grados = dedos >= 0f ? escopetaPuno * dedos : escopetaAbrir * dedos;
        for (int d = 0; d < Dedos.Length; d++)
        {
            float pulgar = d == 4 ? 0.6f : 1f;
            // Separación: el índice hacia un lado, el meñique hacia el otro, el pulgar se aparta más.
            float abanico = (d == 4 ? -2f : d - 1.5f) * escopetaSeparar * separar;
            for (int j = 0; j < 3; j++)
            {
                var h = huesosDedo[d, j];
                if (h == null || (d == 4 && j == 0)) continue;
                float g = (j == 0 ? grados.x : j == 1 ? grados.y : grados.z) * pulgar;
                h.localRotation = h.localRotation * Quaternion.AngleAxis(g, Vector3.right);
                if (j == 0 || (d == 4 && j == 1)) h.localRotation = h.localRotation * Quaternion.AngleAxis(abanico, escopetaEjeSeparar);
            }
        }

        if (t >= total)
        {
            transform.localPosition = escopetaBasePos;
            transform.localRotation = escopetaBaseRot;
            escopetaT = -1f;
        }
    }

    // ------------------------------------------------------------------ Gesto de atraer (clic derecho)
    // La mano se adelanta con los dedos muy abiertos, tira hacia ti cerrándolos con un golpe de muñeca hacia arriba
    // (como los guantes de gravedad) y vuelve a su sitio. Lo lanza AtraerFragmentos al pulsar el clic derecho.

    [Header("Gesto de atraer (clic derecho)")]
    [Tooltip("Segundos que dura el gesto entero.")]
    public float gestoDuracion = 0.55f;
    [Tooltip("Cuánto se adelanta la mano al principio (en largos de mano).")]
    public float gestoAlcance = 0.5f;
    [Tooltip("Cuánto tira hacia ti al cerrar (en largos de mano).")]
    public float gestoTiron = 0.35f;
    [Tooltip("Grados del golpe de muñeca hacia arriba al tirar.")]
    public float gestoGiro = 25f;
    [Tooltip("Grados que se abren los dedos al adelantarse (base, medio, punta).")]
    public Vector3 gestoAbrir = new Vector3(35f, 40f, 30f);
    [Tooltip("Grados que se cierran los dedos al tirar (base, medio, punta).")]
    public Vector3 gestoCerrar = new Vector3(35f, 45f, 35f);

    float gestoT = -1f;
    Vector3 gestoBasePos;
    Quaternion gestoBaseRot;

    /// <summary>Gesto de atraer cristales (no hace nada si la mano está agarrando un cristal).</summary>
    public void GestoAtraer()
    {
        if (fase != FaseAgarre.Nada || escopetaT >= 0f) return;
        if (gestoT < 0f) { gestoBasePos = transform.localPosition; gestoBaseRot = transform.localRotation; }
        gestoT = 0f;
        BuscarDedos();
    }

    void ActualizarGesto()
    {
        gestoT += Time.deltaTime;
        float t = Mathf.Clamp01(gestoT / Mathf.Max(0.05f, gestoDuracion));
        const float a1 = 0.22f, a2 = 0.58f;
        float alcance, dedos, giro;
        if (t < a1) { float u = Mathf.SmoothStep(0f, 1f, t / a1); alcance = gestoAlcance * u; dedos = -u; giro = 0f; }
        else if (t < a2) { float u = Mathf.SmoothStep(0f, 1f, (t - a1) / (a2 - a1)); alcance = Mathf.Lerp(gestoAlcance, -gestoTiron, u); dedos = Mathf.Lerp(-1f, 1f, u); giro = gestoGiro * u; }
        else { float u = Mathf.SmoothStep(0f, 1f, (t - a2) / (1f - a2)); alcance = Mathf.Lerp(-gestoTiron, 0f, u); dedos = Mathf.Lerp(1f, 0f, u); giro = Mathf.Lerp(gestoGiro, 0f, u); }

        transform.localPosition = gestoBasePos;
        transform.localRotation = gestoBaseRot;
        Vector3 haciaDelante = cam != null ? cam.transform.forward : transform.forward;
        transform.position += haciaDelante * alcance * LargoMano;
        if (Mano != null && cam != null && Mathf.Abs(giro) > 1e-3f) transform.RotateAround(Mano.position, cam.transform.right, -giro);

        Vector3 grados = dedos >= 0f ? gestoCerrar * dedos : gestoAbrir * dedos;
        for (int d = 0; d < Dedos.Length; d++)
        {
            float pulgar = d == 4 ? 0.6f : 1f;
            for (int j = 0; j < 3; j++)
            {
                var h = huesosDedo[d, j];
                if (h == null || (d == 4 && j == 0)) continue;
                float g = (j == 0 ? grados.x : j == 1 ? grados.y : grados.z) * pulgar;
                h.localRotation = h.localRotation * Quaternion.AngleAxis(g, Vector3.right);
            }
        }

        if (t >= 1f)
        {
            transform.localPosition = gestoBasePos;
            transform.localRotation = gestoBaseRot;
            gestoT = -1f;
        }
    }

    void ActualizarAgarre()
    {
        if (fase == FaseAgarre.Nada) return;
        float dt = Time.deltaTime;
        tAgarre += dt;

        // Peso de la capa: entra rápido, sale suave al soltar.
        float pesoObjetivo = fase == FaseAgarre.Soltando ? 0f : 1f;
        peso = Mathf.MoveTowards(peso, pesoObjetivo, dt / (fase == FaseAgarre.Soltando ? 0.15f : 0.04f));
        // Los dedos: abrir despacio, cerrar de golpe.
        curl = Mathf.Lerp(curl, curlObjetivo, 1f - Mathf.Exp(-dt * (curlObjetivo > curl ? 45f : 22f)));

        // Muelle amortiguado del retroceso (posición y giro).
        const float k = 260f, c = 22f;
        muelleVel += (-k * muellePos - c * muelleVel) * dt;
        muellePos += muelleVel * dt;
        muelleAngVel += (-k * muelleAng - c * muelleAngVel) * dt;
        muelleAng += muelleAngVel * dt;

        // Raíz: base + adelantarse hacia el cristal + retroceso.
        transform.localPosition = agarreBasePos;
        transform.localRotation = agarreBaseRot;
        float alcance = fase == FaseAgarre.Alcanzar ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tAgarre / 0.1f))
                      : fase == FaseAgarre.Agarrado ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tAgarre / 0.2f)) : 0f;
        Vector3 offset = (dirAlcance * agarreAlcance * LargoMano * alcance + muellePos) * peso;
        transform.position += offset;
        if (Mano != null && Mathf.Abs(muelleAng) > 1e-4f) transform.RotateAround(Mano.position, ejeGiro, muelleAng * peso);

        // Dedos encima de lo que pone el Animator (doblan sobre su eje X local).
        BuscarDedos();
        Vector3 grados = curl >= 0f ? agarreCerrar * curl : agarreAbrir * curl;
        // Apretón: un temblor pequeño mientras lo sujeta.
        float apreton = fase == FaseAgarre.Agarrado ? Mathf.Sin(tAgarre * 55f) * 2.5f * Mathf.Clamp01(1f - tAgarre * 2f) : 0f;
        for (int d = 0; d < Dedos.Length; d++)
        {
            float pulgar = d == 4 ? 0.6f : 1f;
            for (int j = 0; j < 3; j++)
            {
                var h = huesosDedo[d, j];
                if (h == null || (d == 4 && j == 0)) continue; // la base del pulgar no se dobla igual
                float g = (j == 0 ? grados.x : j == 1 ? grados.y : grados.z) * pulgar + apreton;
                h.localRotation = h.localRotation * Quaternion.AngleAxis(g * peso, Vector3.right);
            }
        }

        if (fase == FaseAgarre.Soltando && peso <= 0f)
        {
            fase = FaseAgarre.Nada;
            transform.localPosition = agarreBasePos;
            transform.localRotation = agarreBaseRot;
            curl = 0f;
        }
    }
}

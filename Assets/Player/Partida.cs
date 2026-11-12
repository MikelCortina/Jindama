using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Lógica de la partida:
/// - Empiezas en el centro del mapa, apareciendo desde un fundido a negro.
/// - Tienes un minuto. El tiempo solo corre si vas más despacio que tu velocidad base máxima (la de andar al empezar):
///   a esa velocidad o más rápido (bunny hop, surf…) no corre; parado corre a ritmo normal; entre medias, en proporción.
/// - Cada araña que te da al saltar te quita un 5 % de velocidad base máxima (<see cref="PenalizarVelocidad"/>), así que
///   ya no llegas a esa velocidad andando y el tiempo corre aunque te muevas.
/// - Pierdes si se acaba el tiempo o si te quedas sin balas. Al perder: R (o clic) para volver a empezar.
/// Si no hay ninguna en la escena, se añade sola al empezar la partida.
/// </summary>
public class Partida : MonoBehaviour
{
    public static Partida Instancia { get; private set; }

    [Header("Inicio")]
    [Tooltip("Colocar al jugador en el centro del mapa al empezar.")]
    public bool empezarEnElCentro = true;
    [Tooltip("Punto de inicio (vacío = el centro del mapa, el objeto 'Mapa1').")]
    public Transform puntoInicio;
    [Tooltip("Segundos del fundido desde negro al empezar.")]
    public float duracionFundido = 1.5f;

    [Header("Tiempo")]
    [Tooltip("Segundos de partida.")]
    public float tiempoInicial = 60f;

    [Header("Puntuación")]
    [Tooltip("Puntos del contador de tiempo por cada segundo que aguantas.")]
    public int puntosPorSegundo = 10;
    [Tooltip("Puntos del contador de arañas por cada araña que matas.")]
    public int puntosPorArana = 10;

    [Header("Interfaz")]
    [Tooltip("Tamaño del texto del tiempo (para una pantalla de 1080 de alto).")]
    public int tamanoTexto = 40;

    [Tooltip("Segundos entre que estalla el cristal del arma (al quedarte sin balas) y la pantalla de derrota.")]
    public float retrasoSinBalas = 1.5f;

    enum Estado { Jugando, Perdida }
    int aranasMatadas;

    /// <summary>Arañas matadas en esta partida.</summary>
    public int AranasMatadas => aranasMatadas;

    /// <summary>Una araña matada por el jugador.</summary>
    public void ContarArana() { if (estado == Estado.Jugando) aranasMatadas++; }

    Estado estado;
    float tiempo;
    float velocidadReferencia;   // velocidad base máxima del principio: a esta velocidad el tiempo no corre
    float velocidadBase;         // velocidad base máxima actual (baja con los golpes de las arañas)
    float fundido = 1f;          // 1 = negro
    float ritmo;                 // a qué ritmo corre el tiempo ahora (0..1)
    float segundosAguantados;    // segundos de partida jugados (para la puntuación)
    string motivo;
    FirstPersonController jugador;
    CrystalWeapon arma;
    GUIStyle estiloTiempo, estiloInfo, estiloGrande, estiloIzq, estiloIzqGrande;
    static Texture2D negro;

    /// <summary>Segundos que quedan.</summary>
    public float TiempoRestante => tiempo;
    public bool Perdida => estado == Estado.Perdida;

    // Puntuación: dos contadores separados que se multiplican.
    /// <summary>Contador de tiempo: <see cref="puntosPorSegundo"/> por cada segundo aguantado.</summary>
    public int PuntosTiempo => Mathf.FloorToInt(segundosAguantados) * puntosPorSegundo;
    /// <summary>Contador de arañas: <see cref="puntosPorArana"/> por cada araña matada.</summary>
    public int PuntosAranas => aranasMatadas * puntosPorArana;
    /// <summary>Puntuación total = contador de tiempo × contador de arañas.</summary>
    public long Puntuacion => (long)PuntosTiempo * PuntosAranas;

    static readonly System.Globalization.CultureInfo es = new System.Globalization.CultureInfo("es-ES");
    static string Num(long n) => n.ToString("N0", es);

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void Auto()
    {
        if (FindAnyObjectByType<Partida>() == null) new GameObject("Partida").AddComponent<Partida>();
    }

    void Awake() => Instancia = this;
    void OnDestroy() { if (Instancia == this) Instancia = null; }

    void Start()
    {
        Time.timeScale = 1f;
        jugador = FindAnyObjectByType<FirstPersonController>();
        arma = FindAnyObjectByType<CrystalWeapon>();
        velocidadReferencia = velocidadBase = jugador != null ? jugador.walkSpeed : 4f;
        tiempo = tiempoInicial;
        fundido = 1f;
        if (empezarEnElCentro && jugador != null) ColocarJugador();
    }

    // ------------------------------------------------------------------ Inicio

    void ColocarJugador()
    {
        Vector3 centro;
        if (puntoInicio != null) centro = puntoInicio.position;
        else
        {
            var mapa = GameObject.Find("Mapa1");
            if (mapa == null) return;
            centro = mapa.transform.position;
        }

        // El suelo debajo del centro (lo primero sólido que no sea el jugador).
        Vector3 suelo = centro;
        float mejor = float.MaxValue;
        foreach (var h in Physics.RaycastAll(centro + Vector3.up * 0.5f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(jugador.transform) || h.normal.y < 0.6f) continue;
            if (h.distance < mejor) { mejor = h.distance; suelo = h.point; }
        }

        // El pie de la cápsula sobre el suelo.
        var cc = jugador.GetComponent<CharacterController>();
        var t = jugador.transform;
        Vector3 pie = cc != null ? t.TransformVector(cc.center - Vector3.up * cc.height * 0.5f) : Vector3.zero;
        if (cc != null) cc.enabled = false;
        t.position = suelo - pie + Vector3.up * 0.01f;
        if (cc != null) cc.enabled = true;
    }

    // ------------------------------------------------------------------ Bucle

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (estado == Estado.Jugando)
        {
            fundido = Mathf.MoveTowards(fundido, 0f, dt / Mathf.Max(0.01f, duracionFundido));
            avisoTiempo = Mathf.Max(0f, avisoTiempo - dt);

            // El tiempo corre según lo despacio que vas respecto a tu velocidad base máxima del principio.
            float v = 0f;
            if (jugador != null) { var vel = jugador.Velocity; v = new Vector2(vel.x, vel.z).magnitude; }
            ritmo = Mathf.Clamp01(1f - v / Mathf.Max(0.01f, velocidadReferencia));
            tiempo = Mathf.Max(0f, tiempo - ritmo * Time.deltaTime);

            if (!perdiendo) segundosAguantados += Time.deltaTime;

            if (perdiendo) { }
            else if (tiempo <= 0f) Perder("Se acabó el tiempo");
            // Sin balas (y sin ninguna bala aún en el aire, que podría darle a una araña y volver al cargador).
            else if (arma != null && arma.Balas <= 0 && !arma.Ocupada && FindAnyObjectByType<CrystalShard>() == null)
            {
                // Sin balas: el cristal del arma estalla y, al momento, se pierde la partida.
                arma.Explotar();
                PerderTras("Sin balas", retrasoSinBalas);
            }
        }
        else
        {
            fundido = Mathf.MoveTowards(fundido, 0.75f, dt / 0.6f);
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if ((kb != null && kb.rKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame)) Reiniciar();
        }
    }

    float avisoTiempo; // segundos que queda visible el "−X s"
    float ultimoQuitado;

    /// <summary>Quita segundos del reloj (y lo avisa bajo el tiempo).</summary>
    public void QuitarTiempo(float segundos)
    {
        if (estado != Estado.Jugando || segundos <= 0f) return;
        tiempo = Mathf.Max(0f, tiempo - segundos);
        ultimoQuitado = segundos;
        avisoTiempo = 1.6f;
    }

    /// <summary>Quita una fracción (0.05 = 5 %) de la velocidad base máxima del jugador.</summary>
    public void PenalizarVelocidad(float fraccion)
    {
        if (jugador == null || estado != Estado.Jugando) return;
        velocidadBase *= Mathf.Clamp01(1f - fraccion);
        jugador.walkSpeed = velocidadBase;
    }

    bool perdiendo;

    /// <summary>Pierde dentro de unos segundos (para que se vea lo que ha pasado antes de la pantalla de derrota).</summary>
    public void PerderTras(string porQue, float segundos)
    {
        if (perdiendo || estado == Estado.Perdida) return;
        perdiendo = true;
        StartCoroutine(PerderLuego(porQue, segundos));
    }

    System.Collections.IEnumerator PerderLuego(string porQue, float segundos)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, segundos));
        Perder(porQue);
    }

    void Perder(string porQue)
    {
        if (estado != Estado.Jugando) return;
        MostrarDerrota(porQue);
    }

    void MostrarDerrota(string porQue)
    {
        if (estado == Estado.Perdida) return;
        estado = Estado.Perdida;
        motivo = porQue;
        if (jugador != null) jugador.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    void Reiniciar()
    {
        Time.timeScale = 1f;
        var escena = SceneManager.GetActiveScene();
        SceneManager.LoadScene(escena.buildIndex >= 0 ? escena.buildIndex : 0);
    }

    // ------------------------------------------------------------------ Interfaz

    void OnGUI()
    {
        float esc = Screen.height / 1080f;
        if (estiloTiempo == null)
        {
            estiloTiempo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            estiloInfo = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
            estiloGrande = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            estiloIzq = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft };
            estiloIzqGrande = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
        }
        estiloTiempo.fontSize = Mathf.RoundToInt(tamanoTexto * esc);
        estiloInfo.fontSize = Mathf.RoundToInt(tamanoTexto * 0.45f * esc);
        estiloGrande.fontSize = Mathf.RoundToInt(tamanoTexto * 1.8f * esc);

        // Tiempo arriba en el centro: blanco si no corre, amarillo si corre, rojo en los últimos 10 s.
        int min = Mathf.FloorToInt(tiempo / 60f);
        float seg = tiempo - min * 60f;
        Color c = tiempo < 10f ? new Color(1f, 0.35f, 0.3f) : Color.Lerp(Color.white, new Color(1f, 0.85f, 0.3f), ritmo);
        Sombra(new Rect(0, 20f * esc, Screen.width, estiloTiempo.fontSize * 1.4f), $"{min}:{seg:00.0}", estiloTiempo, c, esc);
        string info = (ritmo <= 0.001f ? "el tiempo está parado" : $"el tiempo corre ×{ritmo:0.00}")
                      + (velocidadBase < velocidadReferencia - 1e-4f ? $"   ·   velocidad máx {velocidadBase / velocidadReferencia * 100f:0}%" : "");
        Sombra(new Rect(0, 20f * esc + estiloTiempo.fontSize * 1.3f, Screen.width, estiloInfo.fontSize * 1.6f), info, estiloInfo, new Color(1f, 1f, 1f, 0.85f), esc);

        // Puntuación arriba a la izquierda (contador de tiempo × contador de arañas).
        if (estado == Estado.Jugando)
        {
            float x = 28f * esc, y = 20f * esc, ancho = Screen.width * 0.4f;
            estiloIzq.fontSize = Mathf.RoundToInt(tamanoTexto * 0.45f * esc);
            Sombra(new Rect(x, y, ancho, estiloIzq.fontSize * 1.5f), "PUNTUACIÓN", estiloIzq, new Color(1f, 1f, 1f, 0.75f), esc);
            y += estiloIzq.fontSize * 1.3f;
            estiloIzqGrande.fontSize = Mathf.RoundToInt(tamanoTexto * 0.9f * esc);
            Sombra(new Rect(x, y, ancho, estiloIzqGrande.fontSize * 1.4f), Num(Puntuacion), estiloIzqGrande, Color.white, esc);
        }

        // Aviso de tiempo perdido.
        if (avisoTiempo > 0f)
        {
            float a = Mathf.Clamp01(avisoTiempo / 0.5f);
            var r = new Rect(0, 20f * esc + estiloTiempo.fontSize * 1.3f + estiloInfo.fontSize * 1.6f, Screen.width, estiloTiempo.fontSize * 1.2f);
            Sombra(r, "-" + ultimoQuitado.ToString("0") + " s", estiloTiempo, new Color(1f, 0.3f, 0.25f, a), esc);
        }

        // Fundido.
        if (fundido > 0.001f)
        {
            if (negro == null) { negro = new Texture2D(1, 1); negro.SetPixel(0, 0, Color.black); negro.Apply(); }
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fundido);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), negro);
            GUI.color = prev;
        }

        if (estado == Estado.Perdida)
        {
            float y = Screen.height * 0.3f;
            Sombra(new Rect(0, y, Screen.width, estiloGrande.fontSize * 1.4f), "Has perdido", estiloGrande, Color.white, esc);
            y += estiloGrande.fontSize * 1.4f;
            Sombra(new Rect(0, y, Screen.width, estiloInfo.fontSize * 1.6f), motivo, estiloInfo, new Color(1f, 1f, 1f, 0.9f), esc);
            y += estiloInfo.fontSize * 2.6f;

            // Puntuación final.
            Sombra(new Rect(0, y, Screen.width, estiloInfo.fontSize * 1.6f), "PUNTUACIÓN", estiloInfo, new Color(1f, 1f, 1f, 0.75f), esc);
            y += estiloInfo.fontSize * 1.4f;
            Sombra(new Rect(0, y, Screen.width, estiloTiempo.fontSize * 1.4f), Num(Puntuacion), estiloTiempo, new Color(1f, 0.85f, 0.3f), esc);
            y += estiloTiempo.fontSize * 1.4f + estiloInfo.fontSize * 1.4f;
            Sombra(new Rect(0, y, Screen.width, estiloInfo.fontSize * 1.6f), "R o clic para volver a empezar", estiloInfo, new Color(1f, 1f, 1f, 0.9f), esc);
        }
    }

    static void Sombra(Rect r, string texto, GUIStyle estilo, Color color, float esc)
    {
        var s = r; s.x += 2f * esc; s.y += 2f * esc;
        estilo.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(s, texto, estilo);
        estilo.normal.textColor = color;
        GUI.Label(r, texto, estilo);
    }
}

using UnityEngine;

/// <summary>
/// En cada pieza de cristal rota (<see cref="FracturePiece"/>): primero cae al suelo; cuando lleva un rato quieta, sube
/// despacio, girando, hacia el agujero del centro del mapa y, si llega, te quita 10 s del reloj y estalla un 15 % de los cubos de cristal del mapa.
/// Mientras sube, una línea la une con el agujero para que se vea que está siendo atraída.
/// Mientras sube se puede atraer con Shift (<see cref="AtraerFragmentos"/>), que la suelta de la absorción.
/// Se ajusta en <see cref="ConfigAbsorcion"/>.
/// </summary>
public class Absorcion : MonoBehaviour
{
    FracturePiece pieza;
    Rigidbody rb;
    float quieta;
    Vector3 ejeGiro;

    /// <summary>Está subiendo hacia el agujero.</summary>
    public bool Subiendo { get; private set; }

    static ConfigAbsorcion C => ConfigAbsorcion.Cargar();

    void Awake()
    {
        pieza = GetComponent<FracturePiece>();
        rb = GetComponent<Rigidbody>();
        ejeGiro = Random.onUnitSphere;
    }

    void Update()
    {
        if (rb == null || pieza == null || !C.activa) return;
        // Cogida, en la mano o siendo atraída por el jugador: nada de absorción.
        if (pieza.sujeta || GetComponent<FragmentoAtraido>() != null) { if (Subiendo) Detener(); quieta = 0f; return; }

        float dt = Time.deltaTime;
        if (!Subiendo)
        {
            // Primero cae: espera a que esté quieta en el suelo.
            if (!rb.isKinematic && rb.linearVelocity.sqrMagnitude < 0.0025f && rb.angularVelocity.sqrMagnitude < 0.25f) quieta += dt;
            else quieta = 0f;
            if (quieta >= C.tiempoEnSuelo) EmpezarSubida();
            return;
        }

        Vector3 agujero = Agujero();
        Vector3 p = rb.worldCenterOfMass;
        Vector3 hacia = agujero - p;
        if (hacia.magnitude <= C.radioAgujero) { Llegar(agujero); return; }
        Vector3 mov = hacia.normalized * Mathf.Min(C.velocidadSubida * dt, hacia.magnitude);
        transform.position += mov;
        transform.Rotate(ejeGiro, C.giroSubida * dt, Space.World);
        ActualizarLinea(agujero);
    }

    void EmpezarSubida()
    {
        Subiendo = true;
        CrearLinea();
        rb.isKinematic = true;   // sube sin física (atraviesa lo que haya)
        pieza.lifetime = 0f;     // no desaparece mientras sube
        pieza.Rejuvenecer();
    }

    /// <summary>Deja de subir y vuelve a tener física (p.ej. al atraerla el jugador).</summary>
    public void Detener()
    {
        if (!Subiendo) return;
        Subiendo = false;
        QuitarLinea();
        quieta = 0f;
        if (rb != null) { rb.isKinematic = false; rb.useGravity = true; rb.WakeUp(); }
    }

    void Llegar(Vector3 agujero)
    {
        Castigo();
        // El cristal absorbido también se rompe en trocitos.
        var r = GetComponent<Renderer>();
        if (r != null) CrystalBurst.Spawn(r, rb != null ? rb.worldCenterOfMass : transform.position, Vector3.down, C.trocitosCristal, 1.2f, 0.6f, 0.4f);
        Destroy(gameObject);
    }

    /// <summary>Ha llegado al agujero: te quita tiempo del reloj y estalla una parte de los cubos de cristal del mapa.</summary>
    void Castigo()
    {
        if (Partida.Instancia != null) Partida.Instancia.QuitarTiempo(C.segundosQueQuita);
        var cubos = new System.Collections.Generic.List<PyramidCube>();
        foreach (var c in FindObjectsByType<PyramidCube>(FindObjectsSortMode.None)) if (c.isActiveAndEnabled) cubos.Add(c);
        int n = Mathf.Min(cubos.Count, Mathf.Max(1, Mathf.CeilToInt(cubos.Count * C.porcentajeCubos)));
        if (C.porcentajeCubos <= 0f) n = 0;
        Vector3 agujero = Agujero();
        for (int i = 0; i < n; i++)
        {
            int k = Random.Range(i, cubos.Count); // al azar, sin repetir
            (cubos[i], cubos[k]) = (cubos[k], cubos[i]);
            cubos[i].Fragmentar(agujero, C.velocidadFragmentos, C.vidaFragmentos, C.maxFragmentosPorCubo);
        }
    }

    // ------------------------------------------------------------------ Línea hacia el agujero

    LineRenderer linea;
    static Material materialLinea;

    void CrearLinea()
    {
        if (!C.mostrarLinea || linea != null) return;
        var go = new GameObject("Línea al agujero");
        go.transform.SetParent(transform, false);
        linea = go.AddComponent<LineRenderer>();
        linea.useWorldSpace = true;
        linea.positionCount = 2;
        linea.numCapVertices = 2;
        linea.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        linea.receiveShadows = false;
        if (materialLinea == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            materialLinea = new Material(sh) { name = "Línea al agujero" };
        }
        linea.sharedMaterial = materialLinea;
        ActualizarLinea(Agujero());
    }

    void ActualizarLinea(Vector3 agujero)
    {
        if (linea == null) return;
        if (!C.mostrarLinea) { QuitarLinea(); return; }
        if (materialLinea.HasProperty("_BaseColor")) materialLinea.SetColor("_BaseColor", C.colorLinea);
        materialLinea.color = C.colorLinea;
        linea.startWidth = C.grosorLinea;
        linea.endWidth = C.grosorLineaAgujero;
        linea.SetPosition(0, rb != null ? rb.worldCenterOfMass : transform.position);
        linea.SetPosition(1, agujero);
    }

    void QuitarLinea()
    {
        if (linea != null) Destroy(linea.gameObject);
        linea = null;
    }

    // ------------------------------------------------------------------ El agujero

    static Vector3 agujeroCache;
    static int escenaCache = -1;

    /// <summary>
    /// El punto de absorción: el <see cref="PuntoAbsorcion"/> de la escena si lo hay; si no, la posición fija de la
    /// configuración; si no, encima del centro de "Mapa1", a la altura de lo más alto del mapa.
    /// </summary>
    public static Vector3 Agujero()
    {
        if (PuntoAbsorcion.Actual != null) return PuntoAbsorcion.Actual.transform.position;
        if (C.usarPosicionFija) return C.posicionAgujero;
        return AgujeroAutomatico();
    }

    /// <summary>Encima del centro de "Mapa1", a la altura de lo más alto del mapa (más el ajuste de la configuración).</summary>
    public static Vector3 AgujeroAutomatico()
    {
        int escena = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (escena == escenaCache) return agujeroCache + Vector3.up * C.ajusteAlturaAgujero;
        var mapa = GameObject.Find("Mapa1");
        Vector3 centro = mapa != null ? mapa.transform.position : Vector3.zero;
        float alto = centro.y + 2f;
        if (mapa != null)
        {
            bool hay = false;
            Bounds b = default;
            foreach (var r in mapa.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds);
            }
            if (hay) alto = b.max.y;
        }
        agujeroCache = new Vector3(centro.x, alto, centro.z);
        escenaCache = escena;
        return agujeroCache + Vector3.up * C.ajusteAlturaAgujero;
    }
}

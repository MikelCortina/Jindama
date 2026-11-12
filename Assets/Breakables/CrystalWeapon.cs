using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Arma de cristal en primera persona (ArmaCristal.fbx), colgada de la cámara abajo a la derecha.
/// Funciona como un revólver:
/// - El arma gira sin parar sobre su eje (el que va del cuerpo a la punta).
/// - En reposo "respira" (shape <see cref="idleShape"/>); al disparar se mezcla el shape <see cref="shootShape"/>.
/// - Siempre dispara el cono de la punta. Después se consume uno de los 5 conos laterales (el cargador): se encoge
///   hacia la punta, el anillo de laterales gira 72° en sentido contrario para colocar el siguiente, y la punta vuelve
///   a crecer. Los 5 laterales son las 5 balas (la punta no cuenta): tras el quinto disparo se recarga.
/// Los proyectiles (<see cref="CrystalShard"/>) rompen los <see cref="PyramidCube"/> en los que impactan.
///
/// Para añadirla: GameObject > Arma de cristal en la cámara.
/// </summary>
public class CrystalWeapon : MonoBehaviour
{
    [Header("Referencias")]
    public Camera cam;
    [Tooltip("Detectar cuerpo, punta y laterales al empezar (recomendado: así sigue funcionando si cambias el modelo).")]
    public bool autoDetectParts = true;
    [Tooltip("Añadir el sway (balanceo con el movimiento del jugador) si el arma no lo tiene. Se ajusta en el componente WeaponSway.")]
    public bool addSway = true;
    [Tooltip("Cuerpo con los shape keys.")]
    public SkinnedMeshRenderer body;
    [Tooltip("Cono de la punta: es el que se dispara.")]
    public Transform tip;
    [Tooltip("Conos laterales (el cargador), en orden alrededor del anillo.")]
    public List<Transform> sides = new List<Transform>();

    [Header("Giro")]
    [Tooltip("Grados por segundo que gira el arma sobre su eje.")]
    public float spinSpeed = 60f;
    [Tooltip("Eje de giro en el espacio local del arma (Y = el eje verde de Unity, el de simetría del modelo).")]
    public Vector3 spinAxis = Vector3.up;
    [Tooltip("Tiempo que tarda el anillo de laterales en girar 72° tras cada disparo.")]
    public float ringTurnTime = 0.15f;

    [Header("Respiración (shape 1)")]
    public string idleShape = "Key 1";
    [Tooltip("Respiraciones por segundo.")]
    public float breathSpeed = 0.5f;
    [Range(0f, 100f)] public float breathAmount = 100f;

    [Header("Respiración de los conos de munición")]
    [Tooltip("Shape key de respiración de cada cono lateral.")]
    public string ammoBreathShape = "Key 1";
    [Tooltip("Respiraciones por segundo de los conos.")]
    public float ammoBreathSpeed = 0.6f;
    [Range(0f, 100f)] public float ammoBreathAmount = 100f;
    [Tooltip("Diferencia de ritmo entre conos (0 = mismo ritmo, solo desfasados).")]
    [Range(0f, 0.5f)] public float ammoBreathVariation = 0.15f;

    [Header("Disparo (shape 2)")]
    public string shootShape = "Key 2";
    [Tooltip("Segundos hasta llegar al shape de disparo.")]
    public float shootIn = 0.05f;
    [Tooltip("Segundos para volver a respirar.")]
    public float shootOut = 0.35f;
    [Tooltip("Tiempo mínimo entre disparos.")]
    public float fireInterval = 0.2f;

    [Header("Proyectil")]
    public float shardSpeed = 12f;
    [Tooltip("Al rebotar en el suelo, cuánto se corrige la diagonal hacia la izquierda con la que sale la bala (el arma está a la derecha de la mira). 0 = nada (sigue hacia la izquierda), 1 = del todo (sale recta hacia donde miras).")]
    [Range(0f, 1.5f)] public float correccionRebote = 0.5f;
    [Tooltip("Grados extra al rebotar (positivo = derecha, negativo = izquierda).")]
    public float extraRebote = 0f;
    public float maxDistance = 60f;
    [Tooltip("Tamaño final del proyectil respecto al cono de la punta (1 = no crece).")]
    public float shardMaxScale = 3f;
    [Tooltip("Segundos que tarda en alcanzar el tamaño final.")]
    public float shardGrowTime = 0.4f;
    public LayerMask hitLayers = ~0;

    [Header("Cargador")]
    [Tooltip("Espera entre que estalla la bala gastada y empieza a crecer la punta.")]
    public float feedTime = 0.1f;

    [Header("Estallido (bala gastada y punto de impacto)")]
    [Tooltip("Al impactar en un cubo de cristal, los trocitos son así de grandes (y rápidos) respecto a lo normal, y del material del cubo.")]
    public float crystalImpactScale = 2f;
    [Tooltip("Número de trocitos en los que se rompe.")]
    public int burstPieces = 16;
    [Tooltip("Velocidad de los trocitos (unidades/s).")]
    public float burstSpeed = 0.6f;
    [Tooltip("Segundos hasta que los trocitos desaparecen.")]
    public float burstLifetime = 0.35f;
    [Tooltip("Tamaño de los trocitos respecto a la bala.")]
    [Range(0.05f, 0.6f)] public float burstPieceSize = 0.25f;
    [Tooltip("Lo que tarda la punta en volver a crecer.")]
    public float tipRegrowTime = 0.12f;
    [Tooltip("Espera tras el último disparo antes de recargar los laterales.")]
    public float reloadDelay = 0.8f;
    [Tooltip("Lo que tarda cada lateral en crecer al recargar.")]
    public float sideRegrowTime = 0.15f;

    int idleIndex = -1, shootIndex = -1;
    float shotTime = -1f;   // segundos desde el último disparo (-1 = no disparando)
    float nextFire;

    Quaternion baseRotation;
    Vector3 axis;           // eje de giro en el espacio local del arma (pasa por su pivote)
    float spinAngle, ringAngle, ringTarget;

    // Pose original de cada lateral (en el espacio local del arma) y animación de consumo.
    Quaternion[] sideRot;
    Vector3[] sidePos, sideScale, sideCenter, feedOffset;
    Vector3 tipScale, tipCenter;
    // Respiración de cada cono lateral: su renderer con shape keys, el índice del shape, su desfase y su ritmo.
    SkinnedMeshRenderer[] sideSkin;
    int[] sideBreathIndex;
    float[] sidePhase, sideRate;
    bool tipLoaded = true;
    int nextSide;           // laterales gastados en este cargador
    bool[] spent;           // qué laterales están gastados
    bool busy;              // consumiendo / recargando

    /// <summary>Balas que quedan: los laterales que no se han consumido (el cono central no cuenta).</summary>
    public int ShotsLeft => Balas;

    /// <summary>Balas que quedan: los laterales sin gastar más la punta (cargada o creciendo). Empieza con 6 (5 + la punta).</summary>
    public int Balas => (sides.Count - nextSide - Reservados) + (tipLoaded || tipPendiente ? 1 : 0);

    /// <summary>
    /// Balas que se enseñan en la interfaz: las que quedan más las que están en el aire (aún no se han perdido: si le
    /// dan a una araña vuelven). Solo baja cuando una bala se pierde de verdad (ver <see cref="BalaPerdida"/>).
    /// </summary>
    public int BalasVisibles => Balas + Reservados + ultimasEnVuelo;

    /// <summary>
    /// Se ha perdido una bala de verdad (es la vida): un disparo que no le da a nadie, un golpe de un enemigo o que
    /// estallen todas. Disparar por sí solo no la lanza. El parámetro es cuántas.
    /// </summary>
    public event System.Action<int> BalaPerdida;

    int ultimasEnVuelo; // balas "última" (la punta sin laterales) que están en el aire

    // Laterales reservados por balas en el aire: se rompen cuando la bala se rompe, y no se rompen nunca si la bala
    // le da a una araña o a su cristal.
    bool[] reservado;
    int Reservados { get { int r = 0; if (reservado != null) foreach (var x in reservado) if (x) r++; return r; } }
    /// <summary>Balas máximas: los laterales más la punta.</summary>
    public int MaxBalas => sides.Count + 1;
    /// <summary>Ocupada disparando, cargando o perdiendo una bala (las balas aún pueden cambiar).</summary>
    public bool Ocupada => busy;

    bool tipPendiente;     // la punta va a volver a crecer (cuenta como bala)
    int balasPorRecuperar; // balas devueltas mientras el arma estaba ocupada

    void Awake()
    {
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        if (autoDetectParts || body == null || tip == null || sides.Count == 0) AutoSetup();
        if (addSway && GetComponent<WeaponSway>() == null) gameObject.AddComponent<WeaponSway>();

        if (body != null && body.sharedMesh != null)
        {
            idleIndex = body.sharedMesh.GetBlendShapeIndex(idleShape);
            shootIndex = body.sharedMesh.GetBlendShapeIndex(shootShape);
            // Si los nombres no coinciden, el primero y el segundo shape.
            int count = body.sharedMesh.blendShapeCount;
            if (idleIndex < 0 && count > 0) idleIndex = 0;
            if (shootIndex < 0 && count > 1) shootIndex = 1;
        }

        baseRotation = transform.localRotation;
        // Eje de giro: el Y local del arma, que pasa por su pivote (es el eje de simetría del modelo).
        tipCenter = tip != null ? transform.InverseTransformPoint(MeshCenter(tip)) : Vector3.up;
        tipScale = tip != null ? tip.localScale : Vector3.one;
        axis = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : Vector3.up;

        int n = sides.Count;
        sideRot = new Quaternion[n];
        sidePos = new Vector3[n];
        sideScale = new Vector3[n];
        sideCenter = new Vector3[n];
        feedOffset = new Vector3[n];
        spent = new bool[n];
        reservado = new bool[n];
        for (int i = 0; i < n; i++)
        {
            sideRot[i] = sides[i].localRotation;
            sidePos[i] = sides[i].localPosition;
            sideScale[i] = sides[i].localScale;
            sideCenter[i] = transform.InverseTransformPoint(MeshCenter(sides[i]));
        }

        // Cada cono respira en un punto distinto del ciclo (repartidos) y a un ritmo un poco distinto.
        sideSkin = new SkinnedMeshRenderer[n];
        sideBreathIndex = new int[n];
        sidePhase = new float[n];
        sideRate = new float[n];
        for (int i = 0; i < n; i++)
        {
            sideSkin[i] = sides[i].GetComponent<SkinnedMeshRenderer>();
            var m = sideSkin[i] != null ? sideSkin[i].sharedMesh : null;
            sideBreathIndex[i] = m != null ? m.GetBlendShapeIndex(ammoBreathShape) : -1;
            if (sideBreathIndex[i] < 0 && m != null && m.blendShapeCount > 0) sideBreathIndex[i] = 0;
            sidePhase[i] = (float)i / Mathf.Max(1, n) + Random.Range(-0.05f, 0.05f);
            sideRate[i] = 1f + Random.Range(-ammoBreathVariation, ammoBreathVariation);
        }
    }

    /// <summary>
    /// Centro de la geometría de una pieza en el mundo. Se usa la malla y no Renderer.bounds, que es una caja alineada
    /// con el mundo y se descentra cuando la pieza está girada.
    /// </summary>
    public static Vector3 MeshCenter(Transform t)
    {
        Mesh mesh = null;
        if (t.TryGetComponent<SkinnedMeshRenderer>(out var smr)) mesh = smr.sharedMesh;
        else if (t.TryGetComponent<MeshFilter>(out var mf)) mesh = mf.sharedMesh;
        if (mesh == null) return t.position;
        return t.TransformPoint(mesh.bounds.center);
    }

    /// <summary>
    /// Busca el cuerpo (el renderer con shape keys más grande), la punta (la pieza del centro) y los laterales
    /// (el resto), ordenados alrededor del anillo.
    /// </summary>
    public void AutoSetup()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        body = null;
        float biggest = -1f;
        foreach (var r in renderers)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
            {
                float s = smr.sharedMesh.bounds.size.sqrMagnitude;
                if (s > biggest) { biggest = s; body = smr; }
            }
        }

        var pieces = new List<Transform>();
        foreach (var r in renderers)
            if (r != body) pieces.Add(r.transform);
        sides = new List<Transform>();
        tip = null;
        if (pieces.Count == 0) return;
        if (pieces.Count == 1) { tip = pieces[0]; return; }

        // La punta es la pieza más cercana al eje de giro (los laterales están en un anillo alrededor).
        Vector3 ax = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : Vector3.up;
        var centers = new List<Vector3>();
        foreach (var t in pieces) centers.Add(transform.InverseTransformPoint(MeshCenter(t)));
        float DistToAxis(Vector3 c) => (c - ax * Vector3.Dot(c, ax)).magnitude;

        int middle = 0;
        for (int i = 1; i < pieces.Count; i++)
            if (DistToAxis(centers[i]) < DistToAxis(centers[middle])) middle = i;
        tip = pieces[middle];

        // Ordenar los laterales por ángulo alrededor del eje.
        Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 w = Vector3.Cross(ax, u);

        var order = new List<int>();
        for (int i = 0; i < pieces.Count; i++) if (i != middle) order.Add(i);
        order.Sort((a, b) =>
        {
            Vector3 da = centers[a], db = centers[b];
            return Mathf.Atan2(Vector3.Dot(da, w), Vector3.Dot(da, u)).CompareTo(Mathf.Atan2(Vector3.Dot(db, w), Vector3.Dot(db, u)));
        });
        foreach (int i in order) sides.Add(pieces[i]);
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) Fire();
        if (balasPorPerder > 0) TryPerderBala();
        if (balasPorRecuperar > 0) TryRecuperarBala();

        // Giro constante de todo el arma sobre su eje.
        spinAngle = Mathf.Repeat(spinAngle + spinSpeed * Time.deltaTime, 360f);
        transform.localRotation = baseRotation * Quaternion.AngleAxis(spinAngle, axis);

        // Anillo de laterales: gira hacia su siguiente posición del pentágono.
        float k = ringTurnTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime * 4f / ringTurnTime) : 1f;
        ringAngle = Mathf.Lerp(ringAngle, ringTarget, k);
        var ring = Quaternion.AngleAxis(ringAngle, axis);
        for (int i = 0; i < sides.Count; i++)
        {
            sides[i].localRotation = ring * sideRot[i];
            sides[i].localPosition = ring * sidePos[i] + feedOffset[i];
        }

        UpdateShapes();
    }

    void UpdateShapes()
    {
        // Conos de munición: cada uno con su desfase y su ritmo.
        for (int i = 0; i < sides.Count; i++)
        {
            if (sideSkin[i] == null || sideBreathIndex[i] < 0) continue;
            float cycle = Time.time * ammoBreathSpeed * sideRate[i] + sidePhase[i];
            sideSkin[i].SetBlendShapeWeight(sideBreathIndex[i], (Mathf.Sin(cycle * Mathf.PI * 2f) * 0.5f + 0.5f) * ammoBreathAmount);
        }

        if (body == null) return;

        // Disparo: sube rápido y baja más despacio.
        float shoot = 0f;
        if (shotTime >= 0f)
        {
            shoot = shotTime < shootIn
                ? shotTime / Mathf.Max(shootIn, 1e-4f)
                : 1f - (shotTime - shootIn) / Mathf.Max(shootOut, 1e-4f);
            shoot = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(shoot));
            shotTime += Time.deltaTime;
            if (shotTime > shootIn + shootOut) shotTime = -1f;
        }

        // Respiración: suave, y se apaga mientras dura el disparo.
        float breath = (Mathf.Sin(Time.time * breathSpeed * Mathf.PI * 2f) * 0.5f + 0.5f) * breathAmount;
        if (idleIndex >= 0) body.SetBlendShapeWeight(idleIndex, breath * (1f - shoot));
        if (shootIndex >= 0) body.SetBlendShapeWeight(shootIndex, shoot * 100f);
    }

    // ------------------------------------------------------------------ Disparo

    /// <summary>Se lanza cada vez que sale un disparo de verdad (lo usa la mano derecha, ManoJugador).</summary>
    public event System.Action Fired;

    public void Fire()
    {
        // Dispara la punta, que vuelve a crecer al momento si queda algún lateral: ese lateral queda reservado y solo se
        // rompe cuando la bala se rompe (si le da a una araña o a su cristal, no se rompe nunca). Sin laterales libres, la
        // punta era la última bala (6 en total: 5 laterales + la punta). No hay recarga.
        if (Time.time < nextFire || busy || !tipLoaded || tip == null || cam == null) return;
        nextFire = Time.time + fireInterval;
        shotTime = 0f;
        Fired?.Invoke();

        var bala = LaunchShard(tip);
        tip.gameObject.SetActive(false);
        tipLoaded = false;

        if (sides.Count == 0) { StartCoroutine(RegrowTipRoutine()); return; } // arma sin cargador: la punta vuelve siempre
        int lado = SideInFeedSlot();
        if (lado >= 0)
        {
            reservado[lado] = true;
            bala.ladoReservado = lado;
            tipPendiente = true;
            StartCoroutine(RegrowTipRoutine());
        }
        else { bala.eraLaUltima = true; ultimasEnVuelo++; } // la punta no vuelve, salvo que esta bala le dé a una araña
    }

    /// <summary>Estallan todas las balas (los laterales y la punta, también las que estaban reservadas): se queda sin ninguna.</summary>
    bool estalladas; // tras estallar todo, nada vuelve (ni las balas que estuvieran en el aire)

    public void EstallarTodas()
    {
        int antes = BalasVisibles;
        ultimasEnVuelo = 0;
        estalladas = true;
        StopAllCoroutines(); // nada de volver a crecer
        busy = false;
        balasPorPerder = 0;
        balasPorRecuperar = 0;
        for (int j = 0; j < sides.Count; j++)
        {
            if (sides[j].gameObject.activeSelf)
            {
                Vector3 c = MeshCenter(sides[j]);
                CrystalBurst.Spawn(sides[j].GetComponent<Renderer>(), c, c - transform.position, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null);
                sides[j].gameObject.SetActive(false);
            }
            if (!spent[j]) { spent[j] = true; nextSide++; }
            reservado[j] = false;
        }
        if (tip != null && tip.gameObject.activeSelf)
        {
            Vector3 c = MeshCenter(tip);
            CrystalBurst.Spawn(tip.GetComponent<Renderer>(), c, c - transform.position, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null);
            tip.gameObject.SetActive(false);
        }
        tipLoaded = false;
        tipPendiente = false;
        if (antes > 0) BalaPerdida?.Invoke(antes);
    }

    /// <summary>
    /// Una bala ha terminado. <paramref name="consumida"/> = se ha roto contra algo o ha llegado a su alcance: se rompe
    /// el lateral que tenía reservado. Si le ha dado a una araña o a su cristal no se consume: el lateral se queda (o, si
    /// era la última bala, la punta vuelve a crecer).
    /// </summary>
    public void BalaTerminada(CrystalShard bala, bool consumida)
    {
        if (bala == null) return;
        if (bala.eraLaUltima && ultimasEnVuelo > 0) ultimasEnVuelo--;
        if (estalladas) return;
        int i = bala.ladoReservado;
        if (i >= 0 && reservado != null && i < reservado.Length && reservado[i])
        {
            reservado[i] = false;
            if (consumida) { RomperLado(i); BalaPerdida?.Invoke(1); } // no le ha dado a nadie: bala perdida
        }
        else if (bala.eraLaUltima && consumida) BalaPerdida?.Invoke(1); // la última, y ha fallado
        else if (bala.eraLaUltima && !consumida && !tipLoaded && !tipPendiente && tip != null)
        {
            tipPendiente = true;
            StartCoroutine(RegrowTip());
        }
    }

    /// <summary>Rompe un lateral (estalla en trocitos) y gira el anillo para traer el siguiente.</summary>
    void RomperLado(int i)
    {
        if (spent[i]) return;
        spent[i] = true;
        nextSide++;
        var side = sides[i];
        ringTarget -= Mathf.Sign(spinSpeed == 0f ? 1f : spinSpeed) * 360f / Mathf.Max(1, sides.Count);
        Vector3 center = MeshCenter(side);
        Vector3 axisPoint = transform.TransformPoint(Vector3.Project(transform.InverseTransformPoint(center), axis));
        CrystalBurst.Spawn(side.GetComponent<Renderer>(), center, center - axisPoint, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null);
        side.gameObject.SetActive(false);
        feedOffset[i] = Vector3.zero;
        side.localScale = sideScale[i];
    }

    int balasPorPerder; // golpes recibidos mientras el arma estaba ocupada: se cobran en cuanto termina

    /// <summary>
    /// Destruye una bala del cargador como al disparar (el lateral estalla y gira el anillo), pero sin proyectil.
    /// La usa <see cref="AtraerFragmentos"/> cuando un trozo atraído golpea al jugador.
    /// </summary>
    public void PerderBala()
    {
        if (Balas - balasPorPerder <= 0) return;
        balasPorPerder++;
        BalaPerdida?.Invoke(1);
        TryPerderBala();
    }

    void TryPerderBala()
    {
        if (balasPorPerder <= 0 || busy) return;
        balasPorPerder--;
        if (SideInFeedSlot() >= 0) StartCoroutine(PerderBalaRoutine());
        else if (tipLoaded && tip != null)
        {
            // Sin laterales: se rompe la punta (era la última bala).
            Vector3 c = MeshCenter(tip);
            CrystalBurst.Spawn(tip.GetComponent<Renderer>(), c, c - transform.position, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null);
            tip.gameObject.SetActive(false);
            tipLoaded = false;
        }
    }

    /// <summary>
    /// Devuelve una bala (p.ej. al dar a una araña o a su cristal): si falta la punta, vuelve a crecer; si no, vuelve
    /// a crecer un lateral gastado. Nunca pasa de <see cref="MaxBalas"/>.
    /// </summary>
    public void RecuperarBala()
    {
        if (Balas + balasPorRecuperar >= MaxBalas) return;
        balasPorRecuperar++;
        TryRecuperarBala();
    }

    void TryRecuperarBala()
    {
        if (balasPorRecuperar <= 0 || busy) return;
        balasPorRecuperar--;
        if (!tipLoaded && !tipPendiente && tip != null) { tipPendiente = true; StartCoroutine(RegrowTipRoutine()); return; }
        for (int j = sides.Count - 1; j >= 0; j--)
        {
            if (!spent[j]) continue;
            spent[j] = false;
            nextSide--;
            feedOffset[j] = Vector3.zero;
            sides[j].gameObject.SetActive(true);
            StartCoroutine(Grow(sides[j], sideScale[j], sideRegrowTime));
            return;
        }
    }

    IEnumerator PerderBalaRoutine()
    {
        busy = true;
        int i = SideInFeedSlot();
        spent[i] = true;
        nextSide++;
        var side = sides[i];
        ringTarget -= Mathf.Sign(spinSpeed == 0f ? 1f : spinSpeed) * 360f / Mathf.Max(1, sides.Count);

        Vector3 center = MeshCenter(side);
        Vector3 axisPoint = transform.TransformPoint(Vector3.Project(transform.InverseTransformPoint(center), axis));
        CrystalBurst.Spawn(side.GetComponent<Renderer>(), center, center - axisPoint, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null); // en el espacio de la cámara: van con el arma
        side.gameObject.SetActive(false);
        feedOffset[i] = Vector3.zero;
        side.localScale = sideScale[i];

        // La punta sigue cargada. No hay recarga.
        if (ringTurnTime > 0f) yield return new WaitForSeconds(ringTurnTime);
        busy = false;
    }

    /// <summary>Lanza una copia de la pieza hacia donde apunta la cámara.</summary>
    CrystalShard LaunchShard(Transform piece)
    {
        // A dónde apunta: el centro de la pantalla si el cursor está bloqueado (FPS), si no, el ratón.
        Ray ray = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
            ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Vector3 target = Physics.Raycast(ray, out var hit, maxDistance, hitLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(maxDistance);

        // Las piezas del modelo tienen el pivote en el origen del arma: la copia va dentro de un contenedor
        // centrado en la pieza, para que vuele y gire sobre sí misma.
        Vector3 start = MeshCenter(piece);
        var shard = new GameObject("Proyectil de cristal");
        shard.transform.position = start;
        var copy = Instantiate(piece.gameObject, piece.position, piece.rotation);
        copy.transform.localScale = piece.lossyScale;
        copy.transform.SetParent(shard.transform, true);
        copy.SetActive(true);
        foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
        var projectile = shard.AddComponent<CrystalShard>();
        // Mismos trocitos que al gastar una bala, en el punto de impacto.
        projectile.impactPieces = burstPieces;
        projectile.impactSpeed = burstSpeed;
        projectile.impactLifetime = burstLifetime;
        projectile.impactPieceSize = burstPieceSize;
        projectile.crystalImpactScale = crystalImpactScale;
        projectile.arma = this;
        // Al rebotar en el suelo: corregir la diagonal con la que sale (el arma está a la derecha de la mira) y un poco más.
        Vector3 haciaVista = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        Vector3 haciaBala = Vector3.ProjectOnPlane(target - start, Vector3.up);
        float diagonal = haciaVista.sqrMagnitude > 1e-6f && haciaBala.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(haciaBala, haciaVista, Vector3.up) : 0f;
        projectile.giroPrimerRebote = diagonal * correccionRebote + extraRebote;
        projectile.Launch((target - start).normalized, shardSpeed, maxDistance, hitLayers, shardMaxScale, shardGrowTime);
        return projectile;
    }

    /// <summary>
    /// Gasta el lateral que está en la posición de carga (estalla en trocitos), gira el anillo 72° en sentido
    /// contrario al arma para traer el siguiente, y hace crecer de nuevo la punta.
    /// </summary>
    IEnumerator FeedNextSide()
    {
        busy = true;
        // Se gasta el lateral que está ahora en la posición de carga (donde estaba el primero al empezar).
        int i = SideInFeedSlot();
        spent[i] = true;
        nextSide++;
        var side = sides[i];

        ringTarget -= Mathf.Sign(spinSpeed == 0f ? 1f : spinSpeed) * 360f / Mathf.Max(1, sides.Count);

        // La bala gastada estalla: trocitos hacia fuera del eje del arma.
        Vector3 center = MeshCenter(side);
        Vector3 axisPoint = transform.TransformPoint(Vector3.Project(transform.InverseTransformPoint(center), axis));
        CrystalBurst.Spawn(side.GetComponent<Renderer>(), center, center - axisPoint, burstPieces, burstSpeed, burstLifetime, burstPieceSize, cam != null ? cam.transform : null); // en el espacio de la cámara: van con el arma
        side.gameObject.SetActive(false);
        feedOffset[i] = Vector3.zero;
        side.localScale = sideScale[i];

        if (feedTime > 0f) yield return new WaitForSeconds(feedTime);

        // El lateral gastado se convierte en la nueva punta. No hay recarga.
        yield return RegrowTip();
        busy = false;
    }

    /// <summary>Lateral sin gastar más cercano a la posición de carga, con el anillo ya en su giro de destino.</summary>
    int SideInFeedSlot()
    {
        var ring = Quaternion.AngleAxis(ringTarget, axis);
        int best = -1;
        float bestD = float.MaxValue;
        for (int j = 0; j < sides.Count; j++)
        {
            if (spent[j] || (reservado != null && reservado[j])) continue;
            float d = (ring * sideCenter[j] - sideCenter[0]).sqrMagnitude;
            if (d < bestD) { bestD = d; best = j; }
        }
        return best;
    }

    IEnumerator RegrowTipRoutine()
    {
        busy = true;
        yield return RegrowTip();
        busy = false;
    }

    /// <summary>Cargador vacío: espera, vuelven a crecer los 5 laterales y después la punta.</summary>
    IEnumerator Reload()
    {
        yield return new WaitForSeconds(reloadDelay);

        for (int i = 0; i < sides.Count; i++)
        {
            var side = sides[i];
            feedOffset[i] = Vector3.zero;
            side.gameObject.SetActive(true);
            yield return Grow(side, sideScale[i], sideRegrowTime);
        }
        nextSide = 0;
        for (int i = 0; i < spent.Length; i++) spent[i] = false;

        if (!tipLoaded) yield return RegrowTip(); // (al perder una bala por un golpe la punta sigue puesta)
    }

    IEnumerator RegrowTip()
    {
        tip.gameObject.SetActive(true);
        yield return Grow(tip, tipScale, tipRegrowTime);
        tipLoaded = true;
        tipPendiente = false;
    }

    static IEnumerator Grow(Transform piece, Vector3 fullScale, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            piece.localScale = fullScale * Mathf.SmoothStep(0f, 1f, t / duration);
            yield return null;
        }
        piece.localScale = fullScale;
    }
}

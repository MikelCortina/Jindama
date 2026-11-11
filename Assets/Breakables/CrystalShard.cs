using UnityEngine;

/// <summary>
/// Proyectil de cristal disparado por <see cref="CrystalWeapon"/>. Vuela en línea recta girando y va creciendo;
/// si choca con un <see cref="PyramidCube"/> lo rompe en ese punto. Se encoge y desaparece al chocar o al llegar a su alcance.
/// </summary>
public class CrystalShard : MonoBehaviour
{
    const float VanishTime = 0.12f;

    Vector3 direction;
    float speed, range, travelled;
    LayerMask mask;
    Vector3 spinAxis;
    float vanish = -1f;
    float vanishFrom = 1f;

    float maxScale = 1f;  // tamaño final respecto al de salida
    float growTime = 0.5f;
    float age;
    float baseRadius;     // radio del proyectil al salir (para el choque)
    float scale = 1f;
    Material ownMaterial;

    /// <summary>El arma que lo ha disparado (para devolverle la bala si da a una araña).</summary>
    [System.NonSerialized] public CrystalWeapon arma;
    /// <summary>Lateral del cargador reservado para esta bala (-1 = ninguno). Se rompe cuando la bala se rompe.</summary>
    [System.NonSerialized] public int ladoReservado = -1;
    /// <summary>Era la última bala (la punta sin laterales detrás).</summary>
    [System.NonSerialized] public bool eraLaUltima;
    bool terminada;

    /// <summary>Avisa al arma de que la bala ha terminado (una sola vez). consumida = false si le ha dado a una araña.</summary>
    void Terminar(bool consumida)
    {
        if (terminada) return;
        terminada = true;
        if (arma != null) arma.BalaTerminada(this, consumida);
    }

    void OnDestroy() { if (gameObject.scene.isLoaded) Terminar(true); } // no al cerrar la escena
    /// <summary>Grados (alrededor del eje vertical, positivo = a la derecha) que gira en el primer rebote en el suelo.</summary>
    [System.NonSerialized] public float giroPrimerRebote;
    bool yaRebotado;

    /// <summary>Tag de los suelos en los que rebotan los proyectiles.</summary>
    public const string TagSuelo = "Floor";

    // Se compara el nombre (y no CompareTag) para que no dé error si el tag aún no existe en el proyecto.
    static bool EsSuelo(Collider c) => c.gameObject.tag == TagSuelo;

    [Header("Estallido al impactar (lo rellena el arma)")]
    public int impactPieces = 16;
    public float impactSpeed = 0.6f;
    public float impactLifetime = 0.35f;
    public float impactPieceSize = 0.25f;
    [Tooltip("Al dar a un cubo de cristal, los trocitos son así de grandes (y salen así de rápido) respecto a lo normal.")]
    public float crystalImpactScale = 2f;

    public void Launch(Vector3 dir, float speed, float range, LayerMask mask, float maxScale = 1f, float growTime = 0.5f)
    {
        direction = dir.sqrMagnitude > 1e-8f ? dir.normalized : transform.forward;
        this.speed = speed;
        this.range = range;
        this.mask = mask;
        this.maxScale = Mathf.Max(0.01f, maxScale);
        this.growTime = Mathf.Max(0.01f, growTime);
        spinAxis = direction;

        var r = GetComponentInChildren<Renderer>();
        baseRadius = r != null ? r.bounds.extents.magnitude * 0.5f : 0f;
        ownMaterial = r != null ? r.sharedMaterial : null;
    }

    void Update()
    {
        if (vanish >= 0f)
        {
            vanish += Time.deltaTime;
            transform.localScale = Vector3.one * (vanishFrom * Mathf.Clamp01(1f - vanish / VanishTime));
            if (vanish >= VanishTime) Destroy(gameObject);
            return;
        }

        // Crece desde su tamaño de salida hasta maxScale.
        age += Time.deltaTime;
        scale = Mathf.Lerp(1f, maxScale, Mathf.SmoothStep(0f, 1f, age / growTime));
        transform.localScale = Vector3.one * scale;

        float step = speed * Time.deltaTime;
        // Choque en el tramo de este frame, con el grosor actual del proyectil (no atraviesa nada aunque vaya rápido).
        float radius = baseRadius * scale;
        bool hitSomething = radius > 1e-4f
            ? Physics.SphereCast(transform.position, radius, direction, out var hit, step, mask, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(transform.position, direction, out hit, step, mask, QueryTriggerInteraction.Ignore);
        if (hitSomething && EsSuelo(hit.collider))
        {
            // Suelo (tag "Floor"): rebota y sigue en la dirección en la que iba, sin romperse.
            transform.position += direction * hit.distance + hit.normal * 0.001f;
            travelled += hit.distance;
            direction = Vector3.Reflect(direction, hit.normal).normalized;
            if (!yaRebotado) { direction = Quaternion.AngleAxis(giroPrimerRebote, Vector3.up) * direction; yaRebotado = true; }
            spinAxis = direction;
            if (travelled >= range) StartVanish();
            return;
        }
        if (hitSomething)
        {
            transform.position += direction * hit.distance;
            var cube = hit.collider.GetComponentInParent<PyramidCube>();
            if (cube != null) cube.Hit(hit, direction);
            // Ciempiés: solo le hace daño a la cola; darle a la cola no gasta la bala, darle a otro segmento sí.
            var segmento = hit.collider.GetComponent<SegmentoCiempies>();
            if (segmento != null && segmento.RecibirDisparo()) Terminar(false);

            // Arañas: primero su cristal protector; sin él, la araña.
            var escudo = hit.collider.GetComponent<CristalEscudo>();
            var arana = hit.collider.GetComponentInParent<Arana>();
            if (escudo != null) escudo.RecibirDisparo();
            else if (arana != null) arana.RecibirDisparo();
            // Dar al cristal de una araña, o a una araña sin cristal, no gasta la bala (el lateral del cargador no se rompe).
            // Dar al cuerpo de una araña que aún tiene su cristal sí la gasta: se rompe como contra cualquier otra cosa.
            bool aranaConEscudo = escudo == null && arana != null && arana.TieneEscudo;
            if ((escudo != null || arana != null) && !aranaConEscudo) Terminar(false);
            ImpactBurst(hit, cube);
            StartVanish();
            return;
        }

        transform.position += direction * step;
        transform.Rotate(spinAxis, 720f * Time.deltaTime, Space.World);
        travelled += step;
        if (travelled >= range) StartVanish();
    }

    /// <summary>
    /// Trocitos en el punto de impacto, saliendo de la superficie. Contra un cubo de cristal son más grandes
    /// y del material del cubo; contra lo demás, del material del proyectil.
    /// </summary>
    void ImpactBurst(RaycastHit hit, PyramidCube cube)
    {
        Material material = ownMaterial;
        float radius = baseRadius * 2f * scale; // tamaño actual del proyectil
        float speedMul = 1f;
        if (cube != null)
        {
            var cubeRenderer = cube.GetComponent<Renderer>();
            if (cubeRenderer != null && cubeRenderer.sharedMaterial != null) material = cubeRenderer.sharedMaterial;
            radius *= crystalImpactScale;
            speedMul = crystalImpactScale;
        }
        int layer = cube != null ? cube.gameObject.layer : gameObject.layer;
        CrystalBurst.Spawn(material, layer, hit.point, hit.normal, radius, impactPieces,
                           impactSpeed * speedMul, impactLifetime, impactPieceSize);
    }

    void StartVanish()
    {
        Terminar(true); // si no le ha dado a una araña, se rompe su lateral del cargador
        vanish = 0f;
        vanishFrom = scale;
    }
}

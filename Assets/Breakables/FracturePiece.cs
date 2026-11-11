using UnityEngine;

/// <summary>
/// Pieza suelta de un <see cref="PyramidCube"/>: pasado su tiempo de vida encoge y se destruye
/// (con lifetime 0 se queda para siempre). Destruye también su malla, que se crea en tiempo de juego.
/// </summary>
public class FracturePiece : MonoBehaviour
{
    const float ShrinkTime = 0.6f;
    const float MinIgnoreTime = 0.15f; // mínimo sin chocar con el bloque, aunque ya no lo toque
    const float MaxIgnoreTime = 3f;    // por si se queda dentro: después vuelve a chocar sí o sí

    public float lifetime = 8f;
    /// <summary>Mientras es true no envejece (lo usa AtraerFragmentos cuando la pieza está en la mano).</summary>
    [System.NonSerialized] public bool sujeta;

    /// <summary>Vuelve a empezar su tiempo de vida (y recupera el tamaño si estaba encogiendo).</summary>
    public void Rejuvenecer()
    {
        if (age > lifetime && lifetime > 0f) transform.localScale = startScale;
        age = 0f;
    }

    float age;
    Vector3 startScale;
    Collider own;
    Collider ignored;

    void Start()
    {
        startScale = transform.localScale;
        // Al caer al suelo, al rato sube hacia el agujero del centro del mapa (ver Absorcion).
        if (GetComponent<Absorcion>() == null) gameObject.AddComponent<Absorcion>();
    }

    /// <summary>
    /// No chocar con <paramref name="block"/> (el bloque del que sale) hasta haber salido de él.
    /// </summary>
    public void IgnoreUntilClear(Collider self, Collider block)
    {
        if (self == null || block == null) return;
        own = self;
        ignored = block;
        Physics.IgnoreCollision(own, ignored, true);
    }

    void FixedUpdate()
    {
        if (ignored == null || own == null) return;

        // El bloque reconstruye su collider en cada rotura, así que se vuelve a aplicar mientras dure.
        bool blockEmpty = ignored is MeshCollider block && block.sharedMesh == null;
        bool stillInside = !blockEmpty && Physics.ComputePenetration(
            own, transform.position, transform.rotation,
            ignored, ignored.transform.position, ignored.transform.rotation,
            out _, out _);

        if (age >= MaxIgnoreTime || (age >= MinIgnoreTime && !stillInside))
        {
            Physics.IgnoreCollision(own, ignored, false);
            ignored = null;
        }
        else
        {
            Physics.IgnoreCollision(own, ignored, true);
        }
    }

    void Update()
    {
        if (sujeta) return;
        age += Time.deltaTime;
        if (lifetime <= 0f) return;
        float shrink = (age - lifetime) / ShrinkTime;
        if (shrink >= 1f) Destroy(gameObject);
        else if (shrink > 0f) transform.localScale = startScale * (1f - shrink);
    }

    void OnDestroy()
    {
        if (TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) Destroy(mf.sharedMesh);
    }
}

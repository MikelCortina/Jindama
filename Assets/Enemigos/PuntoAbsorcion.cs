using UnityEngine;

/// <summary>
/// Marca el punto exacto al que suben los cristales absorbidos (<see cref="Absorcion"/>). Colócalo en la escena donde
/// quieras (Tools > Absorción > Crear punto de absorción lo crea en el punto que se calcula solo). Si hay uno, manda
/// sobre la posición de <see cref="ConfigAbsorcion"/>.
/// </summary>
public class PuntoAbsorcion : MonoBehaviour
{
    /// <summary>El punto de absorción activo de la escena (null si no hay).</summary>
    public static PuntoAbsorcion Actual { get; private set; }

    [Tooltip("Radio de la esfera que se dibuja en la vista Scene.")]
    public float radioGizmo = 0.1f;

    void OnEnable() => Actual = this;
    void OnDisable() { if (Actual == this) Actual = null; }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 1f, 0.95f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radioGizmo);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * radioGizmo * 4f);
    }
}

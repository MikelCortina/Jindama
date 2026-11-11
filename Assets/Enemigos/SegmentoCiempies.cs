using UnityEngine;

/// <summary>Un segmento de un <see cref="Ciempies"/>: recibe los disparos (solo la cola se rompe).</summary>
public class SegmentoCiempies : MonoBehaviour
{
    [System.NonSerialized] public Ciempies ciempies;

    /// <summary>Le ha dado un disparo. Devuelve true si era la cola (el único segmento que se puede romper).</summary>
    public bool RecibirDisparo() => ciempies != null && ciempies.RecibirDisparo(transform);
}

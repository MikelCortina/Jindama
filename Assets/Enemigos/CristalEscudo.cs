using UnityEngine;

/// <summary>
/// El cristal que flota atado encima de una <see cref="Arana"/>. Mientras exista, los disparos no le hacen nada a la
/// araña: hay que romperlo primero. Lo crea y lo mueve la propia araña.
/// </summary>
public class CristalEscudo : MonoBehaviour
{
    [System.NonSerialized] public Arana arana;

    /// <summary>Le ha dado un disparo.</summary>
    public void RecibirDisparo()
    {
        if (arana != null) arana.DanarEscudo();
    }
}

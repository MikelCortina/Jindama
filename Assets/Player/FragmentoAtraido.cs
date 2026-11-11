using UnityEngine;

/// <summary>
/// Marca puesta por <see cref="AtraerFragmentos"/> en un trozo que vuela hacia el jugador:
/// avisa de si ha chocado con él (a partir de ahí ya no se puede atrapar). Se quita sola.
/// </summary>
public class FragmentoAtraido : MonoBehaviour
{
    [System.NonSerialized] public Transform jugador;
    [System.NonSerialized] public bool choco;

    void OnCollisionEnter(Collision c)
    {
        if (jugador != null && c.collider != null && c.collider.transform.IsChildOf(jugador)) choco = true;
    }
}

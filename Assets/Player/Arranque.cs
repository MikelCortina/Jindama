using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Añade los sistemas del juego que no están puestos a mano en la escena (puertas, partida, atraer cristales y
/// generadores de enemigos) cada vez que se carga una escena: al darle a Play y también al volver a empezar tras perder.
/// (RuntimeInitializeOnLoadMethod solo se ejecuta una vez, al empezar; por eso se hace aquí con sceneLoaded.)
/// </summary>
static class Arranque
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Registrar()
    {
        SceneManager.sceneLoaded -= AlCargar;
        SceneManager.sceneLoaded += AlCargar;
    }

    // La primera escena (al darle a Play en el editor no siempre llega por sceneLoaded).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Primera() => Anadir();

    static void AlCargar(Scene escena, LoadSceneMode modo)
    {
        if (modo == LoadSceneMode.Single) Anadir();
    }

    /// <summary>Añade los que falten (cada uno comprueba si ya está, así que no se duplican).</summary>
    static void Anadir()
    {
        PuertasPilones.Auto();
        Partida.Auto();
        AtraerFragmentos.AnadirAlJugador();
        GeneradorAranas.Auto();
        GeneradorCiempies.Auto();
        HUDBalas.Auto();
        EfectoGolpe.Auto();
    }
}

using UnityEngine;

/// <summary>
/// Cada <see cref="ConfigCiempies.puertasPorCiempies"/> puertas abiertas (<see cref="PuertasPilones.PuertaAbierta"/>)
/// entra un <see cref="Ciempies"/> por una de las ventanas (las de los haces de luz, <see cref="LightShaft"/>), hasta un
/// máximo a la vez. Si no hay ninguno en la escena, se añade solo al empezar la partida.
/// </summary>
public class GeneradorCiempies : MonoBehaviour
{
    [Tooltip("Configuración (vacía = la del proyecto, Assets/Enemigos/Resources/ConfigCiempies).")]
    public ConfigCiempies config;

    ConfigCiempies C => config != null ? config : (config = ConfigCiempies.Cargar());
    int puertasAbiertas;

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void Auto()
    {
        if (FindAnyObjectByType<GeneradorCiempies>() == null) new GameObject("Generador de ciempiés").AddComponent<GeneradorCiempies>();
    }

    void OnEnable() => PuertasPilones.PuertaAbierta += AlAbrirse;
    void OnDisable() => PuertasPilones.PuertaAbierta -= AlAbrirse;

    void AlAbrirse(Transform puerta, Vector3 posicion, Vector3 salida)
    {
        puertasAbiertas++;
        if (puertasAbiertas % Mathf.Max(1, C.puertasPorCiempies) != 0) return;
        if (Ciempies.Todos.Count >= C.maximo) return;
        Sacar(puerta);
    }

    /// <summary>Hace entrar un ciempiés por una ventana al azar.</summary>
    [ContextMenu("Sacar un ciempiés ahora")]
    public void Sacar() => Sacar(null);

    void Sacar(Transform puerta)
    {
        var cfg = C;
        var ventanas = FindObjectsByType<LightShaft>(FindObjectsSortMode.None);
        Vector3 ventana, haciaDentro;
        if (ventanas.Length > 0)
        {
            var v = ventanas[Random.Range(0, ventanas.Length)];
            ventana = v.transform.position;
            haciaDentro = v.LightDirectionWorld;
        }
        else
        {
            // Sin ventanas: entra desde arriba del centro del mapa.
            var mapa = GameObject.Find("Mapa1");
            ventana = (mapa != null ? mapa.transform.position : Vector3.zero) + Vector3.up * 2f;
            haciaDentro = Vector3.down;
        }

        // Materiales: los del asset; si no, el de los cubos de cristal y el de las puertas.
        Material segmento = null, patas = null;
        var cubo = FindAnyObjectByType<PyramidCube>();
        var rc = cubo != null ? cubo.GetComponent<Renderer>() : null;
        if (rc != null) segmento = rc.sharedMaterial;
        var rp = puerta != null ? puerta.GetComponent<Renderer>() : null;
        if (rp != null) patas = rp.sharedMaterial;

        // Aparece en la ventana y se agarra a la pared de al lado; el cuerpo va saliendo detrás de la cabeza.
        Ciempies.Crear(ventana, haciaDentro, cfg, segmento, segmento, patas);
    }
}

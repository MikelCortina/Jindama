using UnityEngine;

/// <summary>
/// Hace entrar libélulas (<see cref="Libelula"/>) por las ventanas (<see cref="LightShaft"/>): cada vez que se abre una
/// puerta, con la probabilidad de <see cref="ConfigLibelulas"/>, entra una por una ventana al azar (hasta el máximo).
/// Si no hay ningún generador en la escena, se añade solo al empezar la partida.
/// </summary>
public class GeneradorLibelulas : MonoBehaviour
{
    [Tooltip("Configuración (vacía = la del proyecto, Assets/Enemigos/Resources/ConfigLibelulas).")]
    public ConfigLibelulas config;

    ConfigLibelulas C => config != null ? config : (config = ConfigLibelulas.Cargar());

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void Auto()
    {
        if (FindAnyObjectByType<GeneradorLibelulas>() == null) new GameObject("Generador de libélulas").AddComponent<GeneradorLibelulas>();
    }

    void OnEnable() => PuertasPilones.PuertaAbierta += AlAbrirse;
    void OnDisable() => PuertasPilones.PuertaAbierta -= AlAbrirse;

    void AlAbrirse(Transform puerta, Vector3 posicion, Vector3 salida)
    {
        if (Random.value > C.probabilidad) return;
        Sacar(puerta);
    }

    /// <summary>Hace entrar una libélula por una ventana al azar.</summary>
    [ContextMenu("Sacar una libélula ahora")]
    public void Sacar() => Sacar(null);

    void Sacar(Transform puerta)
    {
        var cfg = C;
        if (Libelula.Todas.Count >= cfg.maximo) return;

        var ventanas = FindObjectsByType<LightShaft>(FindObjectsSortMode.None);
        Vector3 ventana, haciaDentro;
        if (ventanas.Length > 0)
        {
            var v = ventanas[Random.Range(0, ventanas.Length)];
            ventana = v.transform.position;
            haciaDentro = v.LightDirectionWorld.normalized;
        }
        else
        {
            // Sin ventanas: entra desde arriba del centro del mapa.
            var mapa = GameObject.Find("Mapa1");
            ventana = (mapa != null ? mapa.transform.position : Vector3.zero) + Vector3.up * 2f;
            haciaDentro = Vector3.down;
        }

        // Materiales: los del asset; si no, el de los cubos de cristal (cuerpo) y el de las puertas (alas).
        Material cuerpo = null, alas = null;
        var cubo = FindAnyObjectByType<PyramidCube>();
        var rc = cubo != null ? cubo.GetComponent<Renderer>() : null;
        if (rc != null) cuerpo = rc.sharedMaterial;
        var rp = puerta != null ? puerta.GetComponent<Renderer>() : null;
        if (rp != null) alas = rp.sharedMaterial;

        float e = Mathf.Max(0.01f, cfg.escala);
        Vector3 inicio = ventana - haciaDentro * cfg.distanciaEntrada * e;
        Vector3 dentro = ventana + haciaDentro * cfg.distanciaDentro * e;
        Libelula.Crear(inicio, dentro, cfg, cuerpo, alas);
    }
}

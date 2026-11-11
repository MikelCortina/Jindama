using UnityEngine;

/// <summary>
/// Configuración del ciempiés (asset del proyecto: Assets/Enemigos/Resources/ConfigCiempies.asset; Tools > Ciempiés >
/// Configuración). Los cambios valen también en Play (los de forma, para los que salgan después).
/// Medidas en unidades del mundo (multiplicadas por <see cref="escala"/>).
/// </summary>
[CreateAssetMenu(fileName = "ConfigCiempies", menuName = "Ciempiés/Configuración")]
public class ConfigCiempies : ScriptableObject
{
    public const string RutaResources = "ConfigCiempies";

    [Header("Aparición")]
    [Tooltip("Sale un ciempiés cada este número de puertas abiertas.")]
    [Min(1)] public int puertasPorCiempies = 2;
    [Tooltip("Ciempiés a la vez como máximo.")]
    [Min(1)] public int maximo = 1;

    [Header("Aspecto")]
    [Tooltip("Material de la cabeza (vacío = el de los segmentos).")]
    public Material materialCabeza;
    [Tooltip("Material de los segmentos (vacío = el de los cubos de cristal).")]
    public Material materialSegmento;
    [Tooltip("Material del segmento que se puede romper (la cola). Vacío = el de los segmentos, solo con el latido.")]
    public Material materialVulnerable;
    [Tooltip("Material de las patas (vacío = el de las puertas).")]
    public Material materialPatas;

    [Header("Cuerpo")]
    [Tooltip("Multiplica todas las medidas.")]
    public float escala = 1f;
    [Tooltip("Segmentos del cuerpo sin contar la cabeza.")]
    [Range(1, 40)] public int segmentos = 10;
    public float tamanoCabeza = 0.11f;
    public float tamanoSegmento = 0.085f;
    [Tooltip("Distancia entre segmentos.")]
    public float separacion = 0.12f;
    [Tooltip("Parejas de patas por segmento.")]
    [Range(0, 3)] public int parejasDePatas = 1;
    public float largoPata = 0.09f;
    public float grosorPata = 0.012f;
    [Tooltip("Pasos por segundo de las patas.")]
    public float ritmoPatas = 4f;
    [Tooltip("Grados que oscilan las patas.")]
    public float aperturaPasos = 30f;

    [Header("Movimiento")]
    public float velocidad = 1.1f;
    [Tooltip("Grados por segundo que gira la cabeza.")]
    public float giro = 160f;
    [Tooltip("Grados que serpentea a los lados.")]
    public float serpenteo = 20f;
    [Tooltip("Serpenteos por segundo.")]
    public float ritmoSerpenteo = 1.2f;
    [Tooltip("Materiales por los que puede andar (en mallas con varios materiales, el del triángulo que pisa). Vacío = el de la pared a la que se agarra al entrar por la ventana (la pared blanca).")]
    public System.Collections.Generic.List<Material> materialesPared = new System.Collections.Generic.List<Material>();
    [Tooltip("Solo anda por superficies casi verticales: cuánto puede mirar la superficie hacia arriba o hacia abajo (0 = solo paredes totalmente verticales, 1 = también suelos y techos).")]
    [Range(0f, 1f)] public float inclinacionMaxima = 0.55f;
    [Tooltip("Altura de la cabeza y los segmentos sobre la pared (respecto a su tamaño).")]
    public float alturaSobrePared = 0.6f;
    [Tooltip("Cada cuántos segundos cambia de rumbo mientras se pasea por las paredes.")]
    public float cambioDeRumbo = 6f;

    [Header("Romper cristales")]
    [Tooltip("Segundos paseándose por las paredes entre un cristal y el siguiente.")]
    public float intervaloCristales = 15f;
    [Tooltip("Golpes que da a cada cristal de la pared antes de ir a por otro (cada golpe suelta una pieza).")]
    [Min(1)] public int golpesPorCristal = 2;
    [Tooltip("Segundos entre golpes.")]
    public float intervaloGolpes = 0.6f;
    [Tooltip("Distancia al cristal a la que empieza a golpearlo.")]
    public float alcanceGolpe = 0.35f;

    [Header("Vida")]
    [Tooltip("Disparos que aguanta cada segmento.")]
    [Min(1)] public int vidaSegmento = 1;
    [Tooltip("Disparos que aguanta la cabeza (la última).")]
    [Min(1)] public int vidaCabeza = 2;
    [Tooltip("Cuánto late la cola (el segmento que se puede romper).")]
    public float latidoCola = 0.18f;
    public int trocitosAlRomper = 16;

    static ConfigCiempies cargada;

    public static ConfigCiempies Cargar()
    {
        if (cargada == null) cargada = Resources.Load<ConfigCiempies>(RutaResources);
        if (cargada == null) cargada = CreateInstance<ConfigCiempies>();
        return cargada;
    }
}

using UnityEngine;

/// <summary>
/// Configuración de los cristales absorbidos (asset del proyecto:
/// Assets/Enemigos/Resources/ConfigAbsorcion.asset; Tools > Absorción > Configuración).
/// Las piezas de cristal rotas caen al suelo, al rato suben despacio hacia el agujero del centro del mapa y, al llegar,
/// te quita 10 s del reloj y estalla un 15 % de los cubos de cristal del mapa. Medidas en unidades del mundo.
/// </summary>
[CreateAssetMenu(fileName = "ConfigAbsorcion", menuName = "Absorción/Configuración")]
public class ConfigAbsorcion : ScriptableObject
{
    public const string RutaResources = "ConfigAbsorcion";

    [Header("Absorción de los cristales")]
    [Tooltip("Las piezas rotas suben al agujero del centro del mapa.")]
    public bool activa = true;
    [Tooltip("Segundos quietas en el suelo antes de empezar a subir.")]
    public float tiempoEnSuelo = 1.5f;
    [Tooltip("Velocidad de subida (unidades por segundo).")]
    public float velocidadSubida = 0.25f;
    [Tooltip("Grados por segundo que giran mientras suben.")]
    public float giroSubida = 90f;
    [Tooltip("Usar Posicion Agujero (coordenadas del mundo) como punto de absorción. Si hay un PuntoAbsorcion en la escena, manda ese.")]
    public bool usarPosicionFija = false;
    [Tooltip("Punto de absorción en coordenadas del mundo (solo con Usar Posicion Fija).")]
    public Vector3 posicionAgujero = new Vector3(10.612f, 2.6f, 4.827f);
    [Tooltip("Ajuste de la altura del agujero calculado solo (encima del centro de Mapa1, a la altura de lo más alto del mapa).")]
    public float ajusteAlturaAgujero = 0f;
    [Tooltip("Distancia al agujero a la que cuenta que ha llegado.")]
    public float radioAgujero = 0.1f;

    [Header("Línea hacia el agujero")]
    [Tooltip("Mientras sube, una línea une el trozo con el agujero.")]
    public bool mostrarLinea = true;
    [ColorUsage(true, true)] public Color colorLinea = new Color(0.55f, 1f, 0.95f, 1f);
    [Tooltip("Grosor de la línea en el trozo (unidades del mundo).")]
    public float grosorLinea = 0.006f;
    [Tooltip("Grosor de la línea en el agujero (más fina = se ve que tira de él).")]
    public float grosorLineaAgujero = 0.0015f;

    [Header("Al llegar un cristal al agujero")]
    [Tooltip("Segundos que te quita del reloj.")]
    public float segundosQueQuita = 10f;
    [Tooltip("Parte de los cubos de cristal que quedan en el mapa que estalla (0.15 = un 15 %, al menos uno).")]
    [Range(0f, 1f)] public float porcentajeCubos = 0.15f;
    [Tooltip("Velocidad con la que salen despedidas las pirámides de cada cubo al romperse.")]
    public float velocidadFragmentos = 1.2f;
    [Tooltip("Segundos que tardan en desaparecer.")]
    public float vidaFragmentos = 1.5f;
    [Tooltip("Pirámides que se muestran por cubo como máximo (0 = todas; muchas cargan la escena).")]
    [Min(0)] public int maxFragmentosPorCubo = 80;
    [Tooltip("Trocitos del cristal absorbido al romperse en el agujero.")]
    public int trocitosCristal = 14;

    static ConfigAbsorcion cargada;

    public static ConfigAbsorcion Cargar()
    {
        if (cargada == null) cargada = Resources.Load<ConfigAbsorcion>(RutaResources);
        if (cargada == null) cargada = CreateInstance<ConfigAbsorcion>();
        return cargada;
    }
}

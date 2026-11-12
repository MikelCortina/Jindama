using UnityEngine;

/// <summary>
/// Configuración de las libélulas (asset del proyecto: Assets/Enemigos/Resources/ConfigLibelulas.asset;
/// Tools > Libélulas > Configuración). Medidas en unidades del mundo (multiplicadas por <see cref="escala"/>).
/// </summary>
[CreateAssetMenu(fileName = "ConfigLibelulas", menuName = "Libélulas/Configuración")]
public class ConfigLibelulas : ScriptableObject
{
    public const string RutaResources = "ConfigLibelulas";

    [Header("Aparición")]
    [Tooltip("Probabilidad de que entre una libélula por una ventana cada vez que se abre una puerta (0..1).")]
    [Range(0f, 1f)] public float probabilidad = 0.5f;
    [Tooltip("Libélulas a la vez como máximo.")]
    [Min(0)] public int maximo = 4;
    [Tooltip("Desde cuánto por fuera de la ventana empieza a entrar.")]
    public float distanciaEntrada = 1f;
    [Tooltip("Hasta dónde entra por la ventana antes de ir a por ti.")]
    public float distanciaDentro = 0.8f;

    [Header("Aspecto")]
    [Tooltip("Material del cuerpo (vacío = el de los cubos de cristal).")]
    public Material materialCuerpo;
    [Tooltip("Material de las alas (vacío = el del cuerpo).")]
    public Material materialAlas;

    [Header("Tamaño")]
    [Tooltip("Multiplica todas las medidas.")]
    public float escala = 1f;
    [Tooltip("Largo del cuerpo.")]
    public float largoCuerpo = 0.16f;
    [Tooltip("Grosor del cuerpo.")]
    public float grosorCuerpo = 0.035f;
    public float largoAla = 0.13f;
    public float anchoAla = 0.035f;

    [Header("Alas")]
    [Tooltip("Aleteos por segundo.")]
    public float aleteos = 14f;
    [Tooltip("Grados que suben y bajan las alas.")]
    public float aperturaAleteo = 35f;

    [Header("Vuelo")]
    [Tooltip("Velocidad volando (unidades por segundo).")]
    public float velocidad = 1.2f;
    [Tooltip("Grados por segundo que gira.")]
    public float giro = 220f;
    [Tooltip("Altura por encima de tus ojos a la que revolotea.")]
    public float alturaSobreJugador = 0.25f;
    [Tooltip("Distancia a ti a la que revolotea antes de lanzarse.")]
    public float distanciaRevoloteo = 0.9f;
    [Tooltip("Cuánto se mece arriba y abajo al revolotear.")]
    public float balanceo = 0.05f;
    [Tooltip("Distancia a la que esquiva paredes.")]
    public float distanciaEsquivar = 0.35f;

    [Header("Ataque")]
    [Tooltip("Desde qué distancia se lanza contra ti.")]
    public float alcanceAtaque = 1.1f;
    [Tooltip("Velocidad al lanzarse.")]
    public float velocidadAtaque = 3.5f;
    [Tooltip("Segundos que vuela recto al lanzarse (si no te da, se frena y vuelve a revolotear).")]
    public float duracionAtaque = 0.7f;
    [Tooltip("Segundos sin volver a lanzarse tras fallar.")]
    public float esperaTrasAtaque = 2f;
    [Tooltip("Distancia a la que cuenta que te ha dado.")]
    public float radioGolpe = 0.08f;
    [Tooltip("Fracción de tu velocidad base máxima que te quita si te da (0.05 = 5 %).")]
    [Range(0f, 1f)] public float penalizacionVelocidad = 0.05f;
    [Tooltip("Balas que pierdes si te da.")]
    [Min(0)] public int balasQuePierdes = 0;

    [Header("Vida")]
    [Tooltip("Disparos que aguanta.")]
    [Min(1)] public int vida = 1;
    public int trocitosAlMorir = 18;

    static ConfigLibelulas cargada;

    public static ConfigLibelulas Cargar()
    {
        if (cargada == null) cargada = Resources.Load<ConfigLibelulas>(RutaResources);
        if (cargada == null) cargada = CreateInstance<ConfigLibelulas>();
        return cargada;
    }
}

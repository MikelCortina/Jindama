using UnityEngine;

/// <summary>
/// Configuración de las arañas (asset del proyecto: Assets/Enemigos/Resources/ConfigAranas.asset).
/// Todo lo que se puede ajustar de su tamaño, movimiento, comportamiento y aparición. Los cambios valen también en Play.
/// Medidas en unidades del mundo (multiplicadas por <see cref="escala"/>).
/// </summary>
[CreateAssetMenu(fileName = "ConfigAranas", menuName = "Arañas/Configuración")]
public class ConfigAranas : ScriptableObject
{
    public const string RutaResources = "ConfigAranas";

    [Header("Aparición")]
    [Tooltip("Arañas a la vez como máximo.")]
    public int maximo = 6;
    [Tooltip("Probabilidad de que salga una araña cada vez que se abre una puerta (0..1).")]
    [Range(0f, 1f)] public float probabilidad = 1f;
    [Tooltip("Arañas que salen por cada puerta que se abre.")]
    [Min(1)] public int porPuerta = 1;
    [Tooltip("Cuánto por detrás de la puerta aparece (luego sale andando).")]
    public float atras = 0.12f;
    [Tooltip("Lo que anda recto hacia fuera al salir de la puerta antes de ir a por ti.")]
    public float distanciaSalida = 0.3f;

    [Header("Aspecto")]
    [Tooltip("Material del cuerpo (vacío = el de los cubos de cristal).")]
    public Material materialCuerpo;
    [Tooltip("Material de las patas (vacío = el de la puerta).")]
    public Material materialPatas;

    [Header("Tamaño")]
    [Tooltip("Multiplica todas las medidas de la araña (cuerpo, patas, pasos y alcances de cerca).")]
    public float escala = 1f;
    [Tooltip("Radio del cuerpo.")]
    public float tamano = 0.06f;
    [Tooltip("Altura del cuerpo sobre el suelo.")]
    public float alturaCuerpo = 0.055f;
    public float largoMuslo = 0.085f;
    public float largoTibia = 0.1f;
    public float grosorPata = 0.012f;
    [Tooltip("Lo lejos del cuerpo que apoya los pies.")]
    public float aperturaPatas = 0.11f;
    [Tooltip("Patas a cada lado (4 = 8 patas, como una araña). Vale para las arañas que salgan a partir de ahora.")]
    [Range(1, 6)] public int patasPorLado = 4;

    [Header("Andar")]
    [Tooltip("Velocidad andando (unidades por segundo).")]
    public float velocidad = 0.7f;
    [Tooltip("Grados por segundo que gira.")]
    public float giro = 300f;
    [Tooltip("Distancia que se aleja un pie de su sitio antes de dar un paso.")]
    public float distanciaPaso = 0.06f;
    public float tiempoPaso = 0.11f;
    public float alturaPaso = 0.035f;

    [Header("Escalar")]
    [Tooltip("Pueden subir por paredes cuando estás más alto que ellas (p.ej. encima de una caja) y bajar por ellas.")]
    public bool escalarParedes = true;
    [Tooltip("Cuánto más alto que la araña tienen que estar tus pies para que escale.")]
    public float alturaParaEscalar = 0.08f;

    [Header("Cristales de camino (ya no se usa: los cristales suben solos al agujero)")]
    [Tooltip("Coger y lanzar cristales que encuentre de camino hacia ti.")]
    public bool lanzarCristales = true;
    [Tooltip("Distancia a la que ve un cristal del suelo y se desvía a por él.")]
    public float radioCristales = 0.9f;
    [Tooltip("Desde qué distancia lo coge (lo atrae hacia su boca).")]
    public float alcanceCoger = 0.25f;
    public float tiempoCoger = 0.25f;
    [Tooltip("Segundos que lo sujeta apuntándote antes de lanzarlo.")]
    public float tiempoApuntar = 0.5f;
    [Tooltip("Segundos sin coger otro cristal después de lanzar uno.")]
    public float esperaTrasLanzar = 2f;

    [Header("Ataque")]
    [Tooltip("Desde qué distancia a ti salta.")]
    public float alcanceSalto = 0.9f;
    [Tooltip("Duración del salto hasta ti.")]
    public float tiempoSalto = 0.45f;
    [Tooltip("Segundos sin volver a saltar tras fallar.")]
    public float esperaTrasSalto = 1.5f;
    [Tooltip("Distancia a la que cuenta que te ha dado.")]
    public float radioGolpe = 0.1f;
    [Tooltip("Fracción de tu velocidad base máxima que te quita si te da al saltar (0.05 = un 5 %). Con menos velocidad, el tiempo corre aunque te muevas.")]
    [Range(0f, 1f)] public float penalizacionVelocidad = 0.05f;
    [Tooltip("Balas que pierdes si te da al saltar (además de la velocidad).")]
    [Min(0)] public int balasQuePierdes = 0;

    [Header("Cristal protector")]
    [Tooltip("Cada araña lleva un cristal atado encima; mientras exista, los disparos no le hacen nada.")]
    public bool cristalProtector = true;
    [Tooltip("Altura media del cristal sobre el cuerpo.")]
    public float alturaCristal = 0.2f;
    [Tooltip("Cuánto sube y baja respecto a esa altura.")]
    public float oscilacionCristal = 0.035f;
    [Tooltip("Subidas y bajadas por segundo.")]
    public float velocidadOscilacion = 0.8f;
    [Tooltip("Grados por segundo que gira sobre sí mismo.")]
    public float giroCristal = 90f;
    [Tooltip("Tamaño (radio) del cristal.")]
    public float tamanoCristal = 0.035f;
    [Tooltip("Disparos que aguanta el cristal.")]
    [Min(1)] public int vidaCristal = 1;
    [Tooltip("Grosor de la cuerda que lo ata.")]
    public float grosorCuerda = 0.004f;
    [Tooltip("Material del cristal (vacío = el del cuerpo).")]
    public Material materialCristal;
    [Tooltip("Material de la cuerda (vacío = el de las patas).")]
    public Material materialCuerda;
    [Tooltip("Al romperse, el cristal suelta un trozo de cristal (como los de los cubos: se puede atraer, atrapar o devolver).")]
    public bool soltarCristal = true;
    [Tooltip("Tamaño (largo, en unidades del mundo) de la esquirla que queda al romperse. 0 = como los trozos de las paredes.")]
    public float tamanoCristalSuelto = 0f;
    [Tooltip("Segundos que dura el trozo suelto antes de encoger y desaparecer (0 = para siempre).")]
    public float vidaCristalSuelto = 8f;

    [Header("Vida")]
    [Tooltip("Disparos que aguanta.")]
    [Min(1)] public int vida = 1;
    [Tooltip("Trocitos de cristal al romperse.")]
    public int trocitosAlMorir = 18;

    static ConfigAranas cargada;

    /// <summary>La configuración del proyecto (o una con los valores por defecto si no existe el asset).</summary>
    public static ConfigAranas Cargar()
    {
        if (cargada == null) cargada = Resources.Load<ConfigAranas>(RutaResources);
        if (cargada == null) cargada = CreateInstance<ConfigAranas>();
        return cargada;
    }
}

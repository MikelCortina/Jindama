using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Al perder una bala (<see cref="CrystalWeapon.BalaPerdida"/>) la pantalla se pone roja por los bordes y los colores
/// se separan (aberración cromática), como cuando te dan. Con una sola bala, el borde rojo late un poco.
/// Usa un Volume global propio (viñeta + aberración cromática del URP) cuyo peso sube y baja, así que no pisa los
/// ajustes de tus otros Volumes cuando no está activo. Activa el post-procesado de la cámara principal si estaba apagado.
/// Lo añade <see cref="Arranque"/> solo si no está en la escena.
/// </summary>
public class EfectoGolpe : MonoBehaviour
{
    public static void Auto()
    {
        if (FindAnyObjectByType<EfectoGolpe>() == null) new GameObject("EfectoGolpe").AddComponent<EfectoGolpe>();
    }

    [Header("Borde rojo")]
    public Color colorBorde = new Color(0.75f, 0f, 0.02f);
    [Range(0f, 1f)] public float intensidadBorde = 0.55f;
    [Range(0.01f, 1f)] public float suavidadBorde = 0.75f;

    [Header("Aberración cromática")]
    [Range(0f, 1f)] public float intensidadAberracion = 1f;

    [Header("Tiempos")]
    [Tooltip("Segundos que tarda en desaparecer el efecto tras perder una bala.")]
    public float duracion = 0.7f;
    [Tooltip("Cuánto sube el efecto por cada bala perdida a la vez.")]
    public float porBala = 1f;

    [Header("Con una bala")]
    [Tooltip("Peso del borde rojo cuando solo te queda una bala (0 = nada).")]
    [Range(0f, 1f)] public float pesoUltimaBala = 0.35f;
    public float velocidadLatido = 4f;

    [Tooltip("Activar el post-procesado de la cámara principal si está apagado (si no, el efecto no se vería).")]
    public bool activarPostProcesado = true;

    CrystalWeapon arma;
    Volume volumen;
    VolumeProfile perfil;
    Vignette vineta;
    ChromaticAberration aberracion;
    float golpe;

    void Start()
    {
        perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        perfil.name = "EfectoGolpe (auto)";
        vineta = perfil.Add<Vignette>(true);
        aberracion = perfil.Add<ChromaticAberration>(true);

        var go = new GameObject("EfectoGolpe_Volume");
        go.transform.SetParent(transform, false);
        volumen = go.AddComponent<Volume>();
        volumen.isGlobal = true;
        volumen.priority = 1000f; // por encima de los demás
        volumen.sharedProfile = perfil;
        volumen.weight = 0f;

        Aplicar();
        Buscar();
    }

    void Buscar()
    {
        if (arma == null)
        {
            arma = FindAnyObjectByType<CrystalWeapon>();
            if (arma != null) arma.BalaPerdida += AlPerder;
        }
        if (activarPostProcesado && Camera.main != null)
        {
            var datos = Camera.main.GetUniversalAdditionalCameraData();
            if (datos != null && !datos.renderPostProcessing) datos.renderPostProcessing = true;
        }
    }

    void OnDestroy()
    {
        if (arma != null) arma.BalaPerdida -= AlPerder;
        if (perfil != null) Destroy(perfil);
    }

    void AlPerder(int cuantas) => golpe = Mathf.Clamp01(golpe + porBala * Mathf.Max(1, cuantas));

    /// <summary>Lanza el efecto a mano (por si lo quieres usar para otras cosas).</summary>
    public void Golpe(float fuerza = 1f) => golpe = Mathf.Clamp01(golpe + fuerza);

    void Aplicar()
    {
        vineta.color.Override(colorBorde);
        vineta.intensity.Override(intensidadBorde);
        vineta.smoothness.Override(suavidadBorde);
        vineta.rounded.Override(false);
        aberracion.intensity.Override(intensidadAberracion);
    }

    void Update()
    {
        Buscar();
        if (volumen == null) return;
        float dt = Time.unscaledDeltaTime;
        golpe = Mathf.Max(0f, golpe - dt / Mathf.Max(0.05f, duracion));

        // Al golpe: entra de golpe y se va suave.
        float peso = golpe * golpe * (3f - 2f * golpe);

        // Con una bala, el borde late (sin aberración).
        float latido = 0f;
        if (arma != null && arma.BalasVisibles == 1 && pesoUltimaBala > 0f)
            latido = pesoUltimaBala * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * velocidadLatido));

        Aplicar();
        // El latido solo usa la viñeta: la aberración va proporcional al golpe.
        float total = Mathf.Max(peso, latido);
        volumen.weight = total;
        aberracion.intensity.Override(total > 0.0001f ? intensidadAberracion * peso / total : 0f);
    }
}

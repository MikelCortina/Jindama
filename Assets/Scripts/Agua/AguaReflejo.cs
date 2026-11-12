using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reflejo planar para el agua (shader "Jindama/AguaSangre"): una segunda cámara, colocada como el reflejo de la
/// principal bajo la superficie, pinta la escena en una textura que el shader mezcla con el agua.
/// La cámara del reflejo no es un espejo (para no tener que invertir el culling): da la imagen volteada en horizontal
/// y el shader la vuelve a voltear al leerla.
/// Si no pones este script, el agua refleja el cielo / las reflection probes.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class AguaReflejo : MonoBehaviour
{
    [Tooltip("Resolución del reflejo respecto a la de la pantalla (más bajo = más rápido y más pixelado).")]
    [Range(0.1f, 1f)] public float escala = 0.5f;
    [Tooltip("Qué capas se reflejan. La capa del propio agua se quita sola. Quita aquí la de las manos/arma si se ven raras.")]
    public LayerMask capas = ~0;
    [Tooltip("Recorta lo que está justo bajo la superficie (evita que se cuele en el reflejo).")]
    public float desplazamientoPlano = 0.03f;
    public bool sombrasEnReflejo = false;
    [Tooltip("Filtro de la textura del reflejo: Point queda más pixel-art.")]
    public FilterMode filtro = FilterMode.Point;

    static readonly int idTex = Shader.PropertyToID("_AguaReflejoTex");
    static readonly int idActivo = Shader.PropertyToID("_AguaReflejoActivo");

    Renderer rend;
    MaterialPropertyBlock mpb;
    Camera camReflejo;
    RenderTexture rt;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        if (gameObject.layer == 0)
        {
            int agua = LayerMask.NameToLayer("Water");
            if (agua >= 0) gameObject.layer = agua;
        }
    }

    void OnEnable() => RenderPipelineManager.beginContextRendering += AntesDeRenderizar;

    void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= AntesDeRenderizar;
        if (camReflejo != null) Destroy(camReflejo.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
        camReflejo = null; rt = null;
        if (rend != null)
        {
            rend.GetPropertyBlock(mpb);
            mpb.SetFloat(idActivo, 0f);
            rend.SetPropertyBlock(mpb);
        }
    }

    void AntesDeRenderizar(ScriptableRenderContext contexto, List<Camera> camaras)
    {
        Camera cam = Camera.main;
        if (cam == null || !Application.isPlaying) return;
        if (camReflejo == null) CrearCamara();

        // Solo si el agua se ve (la cámara del reflejo no la ve, así que esto depende de la principal).
        camReflejo.enabled = rend.isVisible;
        if (!camReflejo.enabled) return;

        // Textura del tamaño adecuado.
        int w = Mathf.Max(16, Mathf.RoundToInt(cam.pixelWidth * escala));
        int h = Mathf.Max(16, Mathf.RoundToInt(cam.pixelHeight * escala));
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 16, RenderTextureFormat.Default) { name = "ReflejoAgua", filterMode = filtro };
            camReflejo.targetTexture = rt;
        }
        rt.filterMode = filtro;

        // Copiar ajustes de la principal.
        camReflejo.fieldOfView = cam.fieldOfView;
        camReflejo.nearClipPlane = cam.nearClipPlane;
        camReflejo.farClipPlane = cam.farClipPlane;
        camReflejo.aspect = cam.aspect;
        camReflejo.clearFlags = cam.clearFlags;
        camReflejo.backgroundColor = cam.backgroundColor;
        camReflejo.depth = cam.depth - 10;
        camReflejo.cullingMask = capas & ~(1 << gameObject.layer);

        // Posición y orientación reflejadas respecto al plano del agua.
        float altura = rend.bounds.center.y;
        Vector3 p = cam.transform.position;
        Vector3 f = cam.transform.forward, u = cam.transform.up;
        camReflejo.transform.SetPositionAndRotation(
            new Vector3(p.x, 2f * altura - p.y, p.z),
            Quaternion.LookRotation(new Vector3(f.x, -f.y, f.z), new Vector3(u.x, -u.y, u.z)));

        // Plano de recorte oblicuo: no se dibuja nada de lo que esté bajo el agua.
        camReflejo.ResetProjectionMatrix();
        Matrix4x4 vista = camReflejo.worldToCameraMatrix;
        Vector3 puntoCam = vista.MultiplyPoint(new Vector3(0f, altura + desplazamientoPlano, 0f));
        Vector3 normalCam = vista.MultiplyVector(Vector3.up).normalized;
        camReflejo.projectionMatrix = camReflejo.CalculateObliqueMatrix(
            new Vector4(normalCam.x, normalCam.y, normalCam.z, -Vector3.Dot(puntoCam, normalCam)));

        rend.GetPropertyBlock(mpb);
        mpb.SetTexture(idTex, rt);
        mpb.SetFloat(idActivo, 1f);
        rend.SetPropertyBlock(mpb);
    }

    void CrearCamara()
    {
        var go = new GameObject("ReflejoAgua_" + name) { hideFlags = HideFlags.HideAndDontSave };
        camReflejo = go.AddComponent<Camera>();
        var datos = camReflejo.GetUniversalAdditionalCameraData();
        datos.renderPostProcessing = false;
        datos.renderShadows = sombrasEnReflejo;
        datos.requiresColorOption = CameraOverrideOption.Off;
        datos.requiresDepthOption = CameraOverrideOption.Off;
        datos.antialiasing = AntialiasingMode.None;
    }
}

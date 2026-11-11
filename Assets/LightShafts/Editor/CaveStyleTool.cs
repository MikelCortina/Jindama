using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Aplica a la escena abierta el estilo "cueva oscura con haces de luz":
/// ambiente y niebla, luces, motas flotantes y post-procesado.
/// </summary>
public class CaveStyleTool : EditorWindow
{
    const string ParticlesName = "Motas de luz";
    const string SpotName = "Luz del haz";
    const string MotesMaterialPath = "Assets/LightShafts/Motas.mat";
    const string VolumeProfilePath = "Assets/LightShafts/EstiloCueva_Volume.asset";

    // Ambiente y niebla
    Color background = new Color32(0x0F, 0x2B, 0x2B, 0xFF);
    Color ambient = new Color32(0x0E, 0x2A, 0x2A, 0xFF);
    bool autoFogDistance = true;
    float fogStart = 2f, fogEnd = 12f;

    // Luces
    float sunIntensity = 0.25f;
    bool spotUnderShafts = true;
    Color spotColor = new Color(0.85f, 1f, 0.9f);
    float spotIntensity = 3f;

    // Motas
    int moteCount = 50;
    Color moteColor = new Color(0.55f, 0.9f, 0.72f, 0.6f);

    // Post-procesado
    float vignette = 0.35f;
    float contrast = 15f;
    float saturation = -15f;

    [MenuItem("Tools/Haces de luz/Estilo de la escena (ambiente, niebla, motas, post)")]
    static void Open() => GetWindow<CaveStyleTool>("Estilo de la escena");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Ambiente y niebla", EditorStyles.boldLabel);
        background = EditorGUILayout.ColorField("Fondo y niebla", background);
        ambient = EditorGUILayout.ColorField("Luz ambiente", ambient);
        autoFogDistance = EditorGUILayout.Toggle(new GUIContent("Niebla según tamaño del mapa", "Calcula inicio/fin de la niebla a partir del tamaño de la escena."), autoFogDistance);
        using (new EditorGUI.DisabledScope(autoFogDistance))
        {
            fogStart = EditorGUILayout.FloatField("Inicio niebla", fogStart);
            fogEnd = EditorGUILayout.FloatField("Fin niebla", fogEnd);
        }
        if (GUILayout.Button("Aplicar ambiente y niebla")) ApplyEnvironment();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Luces", EditorStyles.boldLabel);
        sunIntensity = EditorGUILayout.Slider("Intensidad Directional", sunIntensity, 0f, 2f);
        spotUnderShafts = EditorGUILayout.Toggle(new GUIContent("Spot Light en cada haz", "Ilumina el charco del suelo bajo cada haz (para materiales Lit/toon). Proyecta sombras de las piedras."), spotUnderShafts);
        spotColor = EditorGUILayout.ColorField("Color spots", spotColor);
        spotIntensity = EditorGUILayout.FloatField("Intensidad spots", spotIntensity);
        if (GUILayout.Button("Aplicar luces")) ApplyLights();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Motas flotantes", EditorStyles.boldLabel);
        moteCount = EditorGUILayout.IntSlider("Cantidad", moteCount, 5, 200);
        moteColor = EditorGUILayout.ColorField("Color", moteColor);
        if (GUILayout.Button("Crear / actualizar motas")) ApplyParticles();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Post-procesado", EditorStyles.boldLabel);
        vignette = EditorGUILayout.Slider("Viñeta", vignette, 0f, 1f);
        contrast = EditorGUILayout.Slider("Contraste", contrast, -100f, 100f);
        saturation = EditorGUILayout.Slider("Saturación", saturation, -100f, 100f);
        if (GUILayout.Button("Aplicar post-procesado")) ApplyPostProcessing();

        EditorGUILayout.Space();
        if (GUILayout.Button("APLICAR TODO", GUILayout.Height(34)))
        {
            ApplyEnvironment();
            ApplyLights();
            ApplyParticles();
            ApplyPostProcessing();
        }
    }

    // ------------------------------------------------------------------ Ambiente y niebla

    void ApplyEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0f;

        if (autoFogDistance && TryGetSceneBounds(out var b))
        {
            float size = b.extents.magnitude;
            fogStart = size * 0.3f;
            fogEnd = size * 2.2f;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = background;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;

        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(cam, "Fondo de cámara");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
        }

        MarkDirty();
        Debug.Log($"Estilo: ambiente y niebla aplicados (niebla {fogStart:0.##} → {fogEnd:0.##}).");
    }

    // ------------------------------------------------------------------ Luces

    void ApplyLights()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            Undo.RecordObject(l, "Intensidad del sol");
            l.intensity = sunIntensity;
        }

        foreach (var shaft in Object.FindObjectsByType<LightShaft>(FindObjectsSortMode.None))
        {
            var existing = shaft.transform.Find(SpotName);
            if (!spotUnderShafts)
            {
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
                continue;
            }

            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(SpotName);
                Undo.RegisterCreatedObjectUndo(go, "Crear luz del haz");
                go.transform.SetParent(shaft.transform, false);
            }

            if (!go.TryGetComponent<Light>(out var light)) light = go.AddComponent<Light>();
            Undo.RecordObject(light, "Luz del haz");
            Undo.RecordObject(go.transform, "Luz del haz");

            // Distancia hasta donde cae la luz (suelo, piedras).
            Vector3 dir = shaft.LightDirectionWorld;
            Vector3 origin = shaft.transform.position + dir * Mathf.Max(shaft.startOffset, 0.02f);
            float dist = Physics.Raycast(origin, dir, out var hit, shaft.maxDistance, shaft.obstacles, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : Mathf.Min(shaft.maxDistance, 10f);
            dist = Mathf.Max(dist, 0.1f);

            // Tamaño de la ventana en mundo.
            var corners = shaft.GetCorners();
            float radius = 0f;
            foreach (var c in corners) radius = Mathf.Max(radius, Vector3.Scale(c, shaft.transform.lossyScale).magnitude);

            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(dir, shaft.transform.up);

            light.type = LightType.Spot;
            light.color = spotColor;
            light.intensity = spotIntensity;
            light.range = dist * 1.4f;
            light.spotAngle = Mathf.Clamp(2f * Mathf.Atan(radius / dist) * Mathf.Rad2Deg, 1f, 170f);
            light.innerSpotAngle = light.spotAngle * 0.85f; // borde duro, como el haz
            light.shadows = LightShadows.Hard;
        }

        MarkDirty();
        Debug.Log("Estilo: luces aplicadas.");
    }

    // ------------------------------------------------------------------ Motas

    void ApplyParticles()
    {
        if (!TryGetSceneBounds(out var b)) b = new Bounds(Vector3.zero, Vector3.one * 5f);
        float diag = b.size.magnitude;

        var go = GameObject.Find(ParticlesName);
        if (go == null)
        {
            go = new GameObject(ParticlesName);
            Undo.RegisterCreatedObjectUndo(go, "Crear motas");
        }
        go.transform.position = b.center;
        go.transform.rotation = Quaternion.identity;

        if (!go.TryGetComponent<ParticleSystem>(out var ps)) ps = go.AddComponent<ParticleSystem>();
        Undo.RecordObject(ps, "Motas");

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
        main.startSpeed = diag * 0.01f;
        main.startSize = new ParticleSystem.MinMaxCurve(diag * 0.006f, diag * 0.015f);
        main.startRotation = 45f * Mathf.Deg2Rad; // cuadrado girado 45° = rombo
        main.startColor = moteColor;
        main.maxParticles = moteCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = moteCount / 11f; // vida media ~11 s

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = b.size * 0.9f;
        shape.randomDirectionAmount = 1f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = diag * 0.01f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.1f;

        // Aparecer y desaparecer suavemente.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = GetOrCreateMotesMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        MarkDirty();
        Debug.Log("Estilo: motas creadas.");
    }

    static Material GetOrCreateMotesMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MotesMaterialPath);
        if (mat != null) return mat;

        // Mismo shader unlit que los haces: color de vértice (el de la partícula) y niebla.
        mat = new Material(Shader.Find("Custom/LightShaft")) { name = "Motas" };
        mat.SetColor("_Color", Color.white);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        AssetDatabase.CreateAsset(mat, MotesMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // ------------------------------------------------------------------ Post-procesado

    void ApplyPostProcessing()
    {
        Volume volume = null;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal) { volume = v; break; }

        if (volume == null)
        {
            var go = new GameObject("Global Volume");
            Undo.RegisterCreatedObjectUndo(go, "Crear Global Volume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
        }

        var profile = volume.sharedProfile;
        if (profile == null)
        {
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }
            Undo.RecordObject(volume, "Perfil de volumen");
            volume.sharedProfile = profile;
        }

        var vig = GetOrAdd<Vignette>(profile);
        vig.intensity.Override(vignette);
        vig.smoothness.Override(0.45f);
        vig.color.Override(Color.black);

        var ca = GetOrAdd<ColorAdjustments>(profile);
        ca.contrast.Override(contrast);
        ca.saturation.Override(saturation);

        // Sin bloom: estilo limpio.
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.intensity.Override(0f);
            bloom.active = false;
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            var data = cam.GetUniversalAdditionalCameraData();
            Undo.RecordObject(data, "Post-procesado de cámara");
            data.renderPostProcessing = true;
        }

        MarkDirty();
        Debug.Log($"Estilo: post-procesado aplicado en el perfil '{profile.name}'.");
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp))
        {
            comp = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        comp.active = true;
        Undo.RecordObject(comp, "Post-procesado");
        return comp;
    }

    // ------------------------------------------------------------------ Utilidades

    static bool TryGetSceneBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.GetComponent<LightShaft>() != null) continue;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        return any;
    }

    static void MarkDirty() => EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
}

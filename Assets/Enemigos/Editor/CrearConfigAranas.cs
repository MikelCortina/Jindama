using UnityEditor;
using UnityEngine;

/// <summary>
/// Crea los assets de configuración de los enemigos si no existen (al compilar) y añade menús para abrirlos:
/// Tools > Arañas > Configuración y Tools > Ciempiés > Configuración.
/// </summary>
static class CrearConfigAranas
{
    const string Carpeta = "Assets/Enemigos/Resources";

    [InitializeOnLoadMethod]
    static void AlCompilar() => EditorApplication.delayCall += () =>
    {
        Asegurar<ConfigAranas>(ConfigAranas.RutaResources, false);
        Asegurar<ConfigCiempies>(ConfigCiempies.RutaResources, false);
        Asegurar<ConfigAbsorcion>(ConfigAbsorcion.RutaResources, false);
        Asegurar<ConfigLibelulas>(ConfigLibelulas.RutaResources, false);
    };

    [MenuItem("Tools/Arañas/Configuración")]
    static void AbrirAranas() => Asegurar<ConfigAranas>(ConfigAranas.RutaResources, true);

    [MenuItem("Tools/Ciempiés/Configuración")]
    static void AbrirCiempies() => Asegurar<ConfigCiempies>(ConfigCiempies.RutaResources, true);

    [MenuItem("Tools/Absorción/Configuración")]
    static void AbrirAbsorcion() => Asegurar<ConfigAbsorcion>(ConfigAbsorcion.RutaResources, true);

    [MenuItem("Tools/Libélulas/Configuración")]
    static void AbrirLibelulas() => Asegurar<ConfigLibelulas>(ConfigLibelulas.RutaResources, true);

    static void Asegurar<T>(string nombre, bool seleccionar) where T : ScriptableObject
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode && !seleccionar) return;
        string ruta = Carpeta + "/" + nombre + ".asset";
        var cfg = AssetDatabase.LoadAssetAtPath<T>(ruta);
        if (cfg == null)
        {
            if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Enemigos", "Resources");
            cfg = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(cfg, ruta);
            AssetDatabase.SaveAssets();
            Debug.Log("Creada la configuración en " + ruta, cfg);
        }
        if (seleccionar)
        {
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }
    }
}

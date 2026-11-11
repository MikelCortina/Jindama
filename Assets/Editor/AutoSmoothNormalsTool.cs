using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class AutoSmoothNormalsTool : EditorWindow
{
    [Tooltip("Lista de materiales de FlatKit que triggeran el suavizado.")]
    public List<Material> materialesObjetivo = new List<Material>();

    private Vector2 scrollPos;
    private const string CARPETA_ORIGINAL = "Assets/Meshes_Sin_Smooth";
    private const string CARPETA_SUAVIZADA = "Assets/Smoothed_Meshes";

    // Caché de seguridad para no generar dos veces la misma malla en la misma ejecución
    private Dictionary<Mesh, Mesh> cacheMallasProcesadas = new Dictionary<Mesh, Mesh>();

    [MenuItem("Tools/FlatKit/Auto Smooth Normals Project-Wide")]
    public static void MostrarVentana()
    {
        GetWindow<AutoSmoothNormalsTool>("Auto Smooth Normals");
    }

    private void OnGUI()
    {
        GUILayout.Label("Suavizado de Normales Inteligente (Escena + Proyecto)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        GUILayout.Label("Añade aquí los materiales de FlatKit que usan Outline:");

        ScriptableObject target = this;
        SerializedObject so = new SerializedObject(target);
        SerializedProperty stringsProperty = so.FindProperty("materialesObjetivo");

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        EditorGUILayout.PropertyField(stringsProperty, true);
        EditorGUILayout.EndScrollView();

        so.ApplyModifiedProperties();

        GUILayout.Space(20);

        if (GUILayout.Button("Procesar TODO (Escena y Prefabs)", GUILayout.Height(40)))
        {
            if (materialesObjetivo.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Debes añadir al menos un material a la lista.", "OK");
                return;
            }
            EjecutarProcesamientoMasivo();
        }
    }

    private void EjecutarProcesamientoMasivo()
    {
        if (!AssetDatabase.IsValidFolder(CARPETA_ORIGINAL)) AssetDatabase.CreateFolder("Assets", "Meshes_Sin_Smooth");
        if (!AssetDatabase.IsValidFolder(CARPETA_SUAVIZADA)) AssetDatabase.CreateFolder("Assets", "Smoothed_Meshes");

        cacheMallasProcesadas.Clear();
        int objetosModificados = 0;

        // 1. PROCESAR OBJETOS EN LA ESCENA ACTUAL
        Renderer[] renderersEscena = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        objetosModificados += FiltrarYProcesarRenderers(renderersEscena);

        // 2. PROCESAR PREFABS EN TODO EL PROYECTO
        string[] prefabsGuids = AssetDatabase.FindAssets("t:Prefab");
        List<Renderer> renderersProyecto = new List<Renderer>();

        foreach (string guid in prefabsGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                renderersProyecto.AddRange(prefab.GetComponentsInChildren<Renderer>(true));
            }
        }
        objetosModificados += FiltrarYProcesarRenderers(renderersProyecto.ToArray());

        // Guardar todos los cambios realizados en los assets y refrescar
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Proceso Completado",
            $"Se han actualizado {objetosModificados} objetos/prefabs en total.\n\n" +
            $"Las mallas únicas se han guardado de forma segura, reemplazando las antiguas si cambiaron.", "Genial");
    }

    private int FiltrarYProcesarRenderers(Renderer[] renderers)
    {
        int cambios = 0;

        foreach (Renderer r in renderers)
        {
            // Solo nos interesan mallas normales o mallas con huesos
            if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

            // Comprobar si tiene alguno de los materiales objetivo
            bool tieneMaterialObjetivo = false;
            foreach (Material mat in r.sharedMaterials)
            {
                if (mat != null && materialesObjetivo.Contains(mat))
                {
                    tieneMaterialObjetivo = true;
                    break;
                }
            }

            if (!tieneMaterialObjetivo) continue;

            Mesh mallaOriginal = null;
            if (r is MeshRenderer)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null) mallaOriginal = mf.sharedMesh;
            }
            else if (r is SkinnedMeshRenderer smr)
            {
                mallaOriginal = smr.sharedMesh;
            }

            if (mallaOriginal == null) continue;

            // SISTEMA DE SEGURIDAD 1: Si la malla ya está guardada en nuestra carpeta de suavizadas, la ignoramos para procesar.
            // PERO la usaremos si no hay cambios.
            string pathMallaOriginal = AssetDatabase.GetAssetPath(mallaOriginal);
            if (pathMallaOriginal.Contains(CARPETA_SUAVIZADA)) continue;

            // Conseguir la malla suavizada (de caché, de disco, o generándola nueva/reemplazando)
            Mesh mallaSuavizada = ObtenerOGenerarMallaSegura(mallaOriginal, pathMallaOriginal);

            // Reasignar al objeto si es necesario
            if (mallaSuavizada != null && mallaOriginal != mallaSuavizada)
            {
                Undo.RecordObject(r.gameObject, "Assign Smoothed Mesh");

                if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    Undo.RecordObject(mf, "Assign Smoothed Mesh");
                    mf.sharedMesh = mallaSuavizada;
                    EditorUtility.SetDirty(mf);
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    Undo.RecordObject(smr, "Assign Smoothed Mesh");
                    smr.sharedMesh = mallaSuavizada;
                    EditorUtility.SetDirty(smr);
                }
                cambios++;
            }
        }

        return cambios;
    }

    private Mesh ObtenerOGenerarMallaSegura(Mesh mallaOriginal, string originalPath)
    {
        // SISTEMA DE SEGURIDAD 2: ¿Ya la hemos procesado en el bucle actual? Usamos la memoria RAM.
        if (cacheMallasProcesadas.ContainsKey(mallaOriginal))
        {
            return cacheMallasProcesadas[mallaOriginal];
        }

        // Creamos un nombre hiper-específico (NombreArchivoFBX_NombreMalla)
        string nombreArchivoOriginal = Path.GetFileNameWithoutExtension(originalPath);
        string nombreMallaLimpio = mallaOriginal.name.Replace(":", "_").Replace("/", "_");
        string nombreUnico = $"{nombreArchivoOriginal}_{nombreMallaLimpio}";

        string rutaEsperadaSuavizada = $"{CARPETA_SUAVIZADA}/{nombreUnico}_Smoothed.asset";

        // SISTEMA DE SEGURIDAD 3: ¿Ya existe en el disco duro?
        Mesh mallaExistenteEnDisco = AssetDatabase.LoadAssetAtPath<Mesh>(rutaEsperadaSuavizada);

        if (mallaExistenteEnDisco != null)
        {
            // NUEVO: Comprobar si la malla original ha cambiado (comparando número de vértices)
            if (mallaOriginal.vertexCount == mallaExistenteEnDisco.vertexCount)
            {
                // Suponemos que es la misma. Devuelve la existente.
                cacheMallasProcesadas[mallaOriginal] = mallaExistenteEnDisco;
                return mallaExistenteEnDisco;
            }
            else
            {
                // Han cambiado los vértices. La malla original ha sido editada. 
                // Sobrescribimos el asset existente.
                Debug.Log($"Actualizando malla suavizada: {nombreUnico}");

                Mesh nuevaMallaSuavizada = SuavizarNormalesMatematicamente(mallaOriginal);

                // Limpiar la malla vieja y copiar los datos de la nueva
                mallaExistenteEnDisco.Clear();
                EditorUtility.CopySerialized(nuevaMallaSuavizada, mallaExistenteEnDisco);

                EditorUtility.SetDirty(mallaExistenteEnDisco);
                AssetDatabase.SaveAssets();

                DestroyImmediate(nuevaMallaSuavizada); // Limpiamos la instancia temporal

                cacheMallasProcesadas[mallaOriginal] = mallaExistenteEnDisco;
                return mallaExistenteEnDisco;
            }
        }

        // Si llegamos aquí, la malla NUNCA se ha suavizado. Procedemos a hacer el Backup y procesarla.

        // 1. Guardar el Backup (Solo si el archivo original no estaba ya en la carpeta de backups)
        if (!originalPath.Contains(CARPETA_ORIGINAL))
        {
            string rutaBackup = $"{CARPETA_ORIGINAL}/{nombreUnico}_Backup.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(rutaBackup) == null)
            {
                Mesh backup = Instantiate(mallaOriginal);
                AssetDatabase.CreateAsset(backup, AssetDatabase.GenerateUniqueAssetPath(rutaBackup));
            }
        }

        // 2. Generar el suavizado de FlatKit
        Mesh mallaNuevaCreada = SuavizarNormalesMatematicamente(mallaOriginal);

        // 3. Guardarla en disco
        string rutaFinal = AssetDatabase.GenerateUniqueAssetPath(rutaEsperadaSuavizada);
        AssetDatabase.CreateAsset(mallaNuevaCreada, rutaFinal);

        // Guardar en la caché por si otro objeto pide la misma malla en 2 segundos
        cacheMallasProcesadas[mallaOriginal] = mallaNuevaCreada;

        return mallaNuevaCreada;
    }

    // =========================================================================
    // LÓGICA DE SUAVIZADO (EQUIVALENTE AL BOTÓN DE FLATKIT)
    // =========================================================================
    private Mesh SuavizarNormalesMatematicamente(Mesh meshOriginal)
    {
        Mesh smoothedMesh = Instantiate(meshOriginal);
        smoothedMesh.name = meshOriginal.name + "_Smoothed";

        Vector3[] vertices = smoothedMesh.vertices;
        Vector3[] normals = smoothedMesh.normals;

        Dictionary<Vector3, Vector3> normalAverages = new Dictionary<Vector3, Vector3>();

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 pos = vertices[i];
            if (!normalAverages.ContainsKey(pos))
            {
                normalAverages.Add(pos, normals[i]);
            }
            else
            {
                normalAverages[pos] += normals[i];
            }
        }

        List<Vector4> uvs2 = new List<Vector4>(vertices.Length);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 pos = vertices[i];
            Vector3 smoothedNormal = normalAverages[pos].normalized;
            uvs2.Add(new Vector4(smoothedNormal.x, smoothedNormal.y, smoothedNormal.z, 0));
        }

        // Flatkit lee el UV2 o UV3 dependiendo de la configuración. Rellenamos ambos por seguridad.
        smoothedMesh.SetUVs(2, uvs2);
        smoothedMesh.SetUVs(1, uvs2);

        return smoothedMesh;
    }
}
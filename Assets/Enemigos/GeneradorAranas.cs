using UnityEngine;

/// <summary>
/// Saca arañas (<see cref="Arana"/>) por las puertas: se crean detrás de la puerta al empezar a abrirse (tapadas por ella) y salen cuando ha bajado.
/// Cuántas, con qué probabilidad, su tamaño, velocidad, alcances, etc. se ajustan en <see cref="ConfigAranas"/>
/// (Tools > Arañas > Configuración). Si no hay ningún generador en la escena, se añade solo al empezar la partida.
/// </summary>
public class GeneradorAranas : MonoBehaviour
{
    [Tooltip("Configuración (vacía = la del proyecto, Assets/Enemigos/Resources/ConfigAranas).")]
    public ConfigAranas config;

    ConfigAranas C => config != null ? config : (config = ConfigAranas.Cargar());

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void Auto()
    {
        if (FindAnyObjectByType<GeneradorAranas>() == null) new GameObject("Generador de arañas").AddComponent<GeneradorAranas>();
    }

    // Arañas esperando detrás de cada puerta hasta que termine de bajar.
    readonly System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<Arana>> esperando =
        new System.Collections.Generic.Dictionary<Transform, System.Collections.Generic.List<Arana>>();

    void OnEnable()
    {
        PuertasPilones.PuertaVaAAbrirse += AlEmpezarAAbrirse;
        PuertasPilones.PuertaAbierta += AlAbrirse;
    }

    void OnDisable()
    {
        PuertasPilones.PuertaVaAAbrirse -= AlEmpezarAAbrirse;
        PuertasPilones.PuertaAbierta -= AlAbrirse;
    }

    /// <summary>La puerta ya ha bajado: salen las que esperaban detrás.</summary>
    void AlAbrirse(Transform puerta, Vector3 posicion, Vector3 salida)
    {
        if (!esperando.TryGetValue(puerta, out var lista)) return;
        foreach (var a in lista) if (a != null) a.Salir();
        esperando.Remove(puerta);
    }

    /// <summary>La puerta empieza a abrirse: se crean ya, detrás de ella (tapadas), para que no aparezcan de la nada.</summary>
    void AlEmpezarAAbrirse(Transform puerta, Vector3 posicion, Vector3 salida)
    {
        var cfg = C;
        if (Random.value > cfg.probabilidad) return;

        // Materiales de la escena por si los del asset de configuración están vacíos (la araña usa los del asset si los hay).
        Material cuerpo = null, patas = null;
        if (cuerpo == null)
        {
            var cubo = FindAnyObjectByType<PyramidCube>();
            var r = cubo != null ? cubo.GetComponent<Renderer>() : null;
            if (r != null) cuerpo = r.sharedMaterial;
        }
        var rp = puerta != null ? puerta.GetComponent<Renderer>() : null;
        if (patas == null && rp != null) patas = rp.sharedMaterial;
        if (cuerpo == null && rp != null && rp.sharedMaterials.Length > 1) cuerpo = rp.sharedMaterials[1];

        Vector3 lado = Vector3.Cross(Vector3.up, salida).normalized;
        float atras = cfg.atras;
        for (int i = 0; i < cfg.porPuerta && Arana.Todas.Count < cfg.maximo; i++)
        {
            // En el hueco de la puerta, un poco por detrás, sobre el suelo (varias, repartidas a lo ancho).
            float desplazamiento = cfg.porPuerta > 1 ? (i / (cfg.porPuerta - 1f) - 0.5f) * 0.2f * cfg.escala : 0f;
            Vector3 inicio = posicion - salida * atras + lado * desplazamiento;
            Vector3 punto = inicio, normal = Vector3.up;
            bool hay = false;
            foreach (var h in Physics.RaycastAll(inicio + Vector3.up * 0.4f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform == puerta || h.collider.GetComponent<FracturePiece>() != null) continue;
                if (h.normal.y < 0.6f) continue;
                if (!hay || h.point.y > punto.y) { punto = h.point; normal = h.normal; hay = true; } // el suelo más alto
            }
            var nueva = Arana.Crear(punto, normal, salida, cfg, cuerpo, patas);
            nueva.Esperar();
            if (!esperando.TryGetValue(puerta, out var lista)) esperando[puerta] = lista = new System.Collections.Generic.List<Arana>();
            lista.Add(nueva);
        }
    }
}

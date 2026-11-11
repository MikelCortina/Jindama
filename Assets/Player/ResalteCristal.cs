using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resalte de un trozo de cristal con un <see cref="MaterialPropertyBlock"/> (no toca el material compartido).
/// Lleva cada propiedad de <see cref="AtraerFragmentos.propiedadesEnRadio"/> desde el valor del material hasta el valor
/// configurado con un lerp rápido, y vuelve al original cuando deja de estar resaltado (entonces se quita solo).
/// Lo pone <see cref="AtraerFragmentos"/> cuando el trozo entra en el radio en el que se puede agarrar.
/// </summary>
public class ResalteCristal : MonoBehaviour
{
    [System.NonSerialized] public List<AtraerFragmentos.PropiedadResalte> propiedades;
    [System.NonSerialized] public float velocidad = 25f;
    [System.NonSerialized] public float objetivo;
    /// <summary>true: usa <see cref="objetivo"/> tal cual (lo marca la distancia); false: va hacia él con un lerp rápido.</summary>
    [System.NonSerialized] public bool directo;

    Renderer rend;
    MaterialPropertyBlock bloque;
    float k;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        bloque = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (rend == null || propiedades == null) return;
        if (directo) k = objetivo;
        else
        {
            k = Mathf.Lerp(k, objetivo, 1f - Mathf.Exp(-velocidad * Time.deltaTime));
            if (Mathf.Abs(k - objetivo) < 0.002f) k = objetivo;
        }

        if (!directo && objetivo <= 0f && k <= 0f) { Limpiar(); Destroy(this); return; }
        Aplicar();
    }

    void Aplicar()
    {
        var mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            var mat = mats[i];
            if (mat == null) continue;
            bloque.Clear();
            foreach (var p in propiedades)
            {
                if (p == null || string.IsNullOrEmpty(p.nombre) || !mat.HasProperty(p.nombre)) continue;
                int id = Shader.PropertyToID(p.nombre);
                switch (p.tipo)
                {
                    case AtraerFragmentos.TipoPropiedad.Color:
                        bloque.SetColor(id, Color.Lerp(mat.GetColor(id), p.color, k)); break;
                    case AtraerFragmentos.TipoPropiedad.Float:
                        bloque.SetFloat(id, Mathf.Lerp(mat.GetFloat(id), p.valor, k)); break;
                    case AtraerFragmentos.TipoPropiedad.Vector:
                        bloque.SetVector(id, Vector4.Lerp(mat.GetVector(id), p.vector, k)); break;
                }
            }
            rend.SetPropertyBlock(bloque, i);
        }
    }

    void Limpiar()
    {
        if (rend == null) return;
        for (int i = 0; i < rend.sharedMaterials.Length; i++) rend.SetPropertyBlock(null, i);
    }

    void OnDestroy() => Limpiar();
}

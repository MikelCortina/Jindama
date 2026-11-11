using UnityEngine;

/// <summary>
/// Pieza de cristal solo visual (sin física ni colisión): sale despedida, cae con gravedad, gira y se encoge hasta
/// desaparecer. La usa <see cref="PyramidCube.Fragmentar"/> para romper un cubo entero sin llenar la escena de física.
/// </summary>
public class FragmentoVisual : MonoBehaviour
{
    public Vector3 velocidad;
    public Vector3 ejeGiro;
    public float giro;
    public float vida = 1.5f;
    public float gravedad = 9.81f;

    float edad;
    Vector3 escala;

    void Start()
    {
        escala = transform.localScale;
        if (ejeGiro.sqrMagnitude < 1e-6f) ejeGiro = Random.onUnitSphere;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        edad += dt;
        velocidad += Vector3.down * gravedad * dt;
        transform.position += velocidad * dt;
        transform.Rotate(ejeGiro, giro * dt, Space.World);
        // Encoge en el último 40 % de su vida.
        float t = Mathf.InverseLerp(vida * 0.6f, vida, edad);
        transform.localScale = escala * (1f - t);
        if (edad >= vida) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) Destroy(mf.sharedMesh);
    }
}

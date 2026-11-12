using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cada <see cref="intervalo"/> segundos una de las puertas se abre: el pilón que tiene delante se recoge hacia dentro
/// (hacia la puerta), la puerta baja, se queda abajo <see cref="tiempoAbajo"/> segundos, vuelve a subir y el pilón sale.
/// Las puertas y pilones se buscan por nombre en la escena (son piezas del modelo del mapa) o se arrastran a mano.
/// Si no hay ninguno en la escena, se añade solo al empezar la partida.
/// </summary>
public class PuertasPilones : MonoBehaviour
{
    [System.Serializable]
    public class Puerta
    {
        [Tooltip("La puerta (vacío = se busca por nombre).")]
        public Transform puerta;
        [Tooltip("El pilón que tiene delante (vacío = se busca por nombre).")]
        public Transform pilon;
        public string nombrePuerta;
        public string nombrePilon;
        [System.NonSerialized] public Vector3 puertaArriba, pilonFuera, bajar, recoger;
        [System.NonSerialized] public bool ocupada;
        [System.NonSerialized] public Vector3 centroHueco; // centro del hueco con la puerta subida
    }

    public List<Puerta> puertas = new List<Puerta>();

    [Header("Ritmo")]
    [Tooltip("Cada cuántos segundos se abre una puerta.")]
    public float intervalo = 4f;
    [Tooltip("Segundos que la puerta se queda abajo.")]
    public float tiempoAbajo = 2f;
    [Tooltip("Elegir la puerta al azar (sin repetir la anterior). Si no, van en orden.")]
    public bool aleatorio = true;
    [Tooltip("Escribir en la consola qué se mueve y cuánto (en el mundo) en cada apertura.")]
    public bool depurar = true;

    [Header("Movimiento")]
    public float tiempoRecogerPilon = 0.35f;
    public float tiempoBajarPuerta = 0.5f;
    public float tiempoSubirPuerta = 0.5f;
    public float tiempoSacarPilon = 0.35f;
    [Tooltip("Cuánto baja la puerta por su eje Y local (verde), respecto a lo que mide en ese eje (1 = toda su altura, queda a ras del suelo).")]
    public float bajadaPuerta = 1.02f;
    [Tooltip("Invertir el sentido (sube en vez de bajar).")]
    public bool invertirBajada;
    [Tooltip("Lo que se mueve el pilón al recogerse, en su posición local (los mismos ejes y unidades que ves en el Inspector).")]
    public Vector3 desplazamientoPilon = new Vector3(0f, 0f, -0.000233f);

    /// <summary>Se lanza cuando una puerta ha terminado de bajar: la puerta, dónde estaba (arriba) y la dirección hacia la que se sale (hacia dentro del mapa).</summary>
    public static event System.Action<Transform, Vector3, Vector3> PuertaAbierta;
    /// <summary>Se lanza cuando una puerta empieza a abrirse (antes de que baje): mismos datos que <see cref="PuertaAbierta"/>.</summary>
    public static event System.Action<Transform, Vector3, Vector3> PuertaVaAAbrirse;

    int ultima = -1;

    /// <summary>Lo añade si no está en la escena. Lo llama <see cref="Arranque"/> cada vez que se carga una escena.</summary>
    public static void Auto()
    {
        if (FindAnyObjectByType<PuertasPilones>() != null) return;
        var p = new GameObject("Puertas y pilones").AddComponent<PuertasPilones>();
        p.puertas = Predeterminadas();
    }

    /// <summary>Las 4 puertas del mapa con sus pilones (nombres de las piezas de Mapa1).</summary>
    static List<Puerta> Predeterminadas() => new List<Puerta>
    {
        new Puerta { nombrePuerta = "Cylinder.001", nombrePilon = "Cylinder.013" },
        new Puerta { nombrePuerta = "Cylinder.008", nombrePilon = "Cylinder.012" },
        new Puerta { nombrePuerta = "Cylinder.009", nombrePilon = "Cylinder.002" },
        new Puerta { nombrePuerta = "Cylinder.011", nombrePilon = "Cylinder.014" },
    };

    void Start()
    {
        foreach (var p in puertas)
        {
            if (p.puerta == null && !string.IsNullOrEmpty(p.nombrePuerta)) p.puerta = Buscar(p.nombrePuerta);
            if (p.pilon == null && !string.IsNullOrEmpty(p.nombrePilon)) p.pilon = Buscar(p.nombrePilon);
            if (p.puerta == null) { Debug.LogWarning($"PuertasPilones: no encuentro la puerta '{p.nombrePuerta}'.", this); continue; }

            p.puertaArriba = p.puerta.position;
            p.centroHueco = p.puerta.GetComponent<Renderer>() is Renderer rh ? rh.bounds.center : p.puertaArriba;
            // Baja por su propio eje Y local (el verde del gizmo en modo Local), hacia abajo, tanto como mide en ese eje.
            Vector3 eje = p.puerta.up;
            if (Vector3.Dot(eje, Vector3.down) > 0f) eje = -eje; // "hacia abajo": el sentido que baja
            var mf = p.puerta.GetComponent<MeshFilter>();
            float alto = mf != null && mf.sharedMesh != null
                ? mf.sharedMesh.bounds.size.y * Mathf.Abs(p.puerta.lossyScale.y)
                : (p.puerta.GetComponent<Renderer>() is Renderer rp ? rp.bounds.size.y : 1f);
            p.bajar = -eje * alto * bajadaPuerta * (invertirBajada ? -1f : 1f);

            if (p.pilon != null)
            {
                // El pilón se mueve en su espacio local (el del Inspector).
                p.pilonFuera = p.pilon.localPosition;
                p.recoger = desplazamientoPilon;
            }
            HacerMovible(p.puerta);
            HacerMovible(p.pilon);
        }
        puertas.RemoveAll(p => p.puerta == null);
        if (puertas.Count > 0) StartCoroutine(Ciclo());
    }

    static Transform Buscar(string nombre)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == nombre) return t;
        return null;
    }

    // Las piezas del mapa suelen ser estáticas: para moverlas bien con colisión, collider con Rigidbody cinemático.
    static void HacerMovible(Transform t)
    {
        if (t == null) return;
        t.gameObject.isStatic = false;
        if (t.GetComponent<Collider>() != null && t.GetComponent<Rigidbody>() == null)
        {
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    IEnumerator Ciclo()
    {
        var espera = new WaitForSeconds(intervalo);
        while (true)
        {
            yield return espera;
            int i = Elegir();
            if (i >= 0) StartCoroutine(Abrir(puertas[i]));
        }
    }

    int Elegir()
    {
        var libres = new List<int>();
        for (int i = 0; i < puertas.Count; i++) if (!puertas[i].ocupada) libres.Add(i);
        if (libres.Count == 0) return -1;
        int elegida;
        if (aleatorio)
        {
            if (libres.Count > 1) libres.Remove(ultima);
            elegida = libres[Random.Range(0, libres.Count)];
        }
        else
        {
            elegida = libres[0];
            foreach (int i in libres) if (i > ultima) { elegida = i; break; }
        }
        ultima = elegida;
        return elegida;
    }

    IEnumerator Abrir(Puerta p)
    {
        p.ocupada = true;
        PuertaVaAAbrirse?.Invoke(p.puerta, p.puertaArriba, DireccionSalida(p));
        Vector3 puerta0 = p.puerta.position, pilon0 = p.pilon != null ? p.pilon.position : Vector3.zero;
        if (p.pilon != null) yield return Mover(p.pilon, p.pilonFuera, p.pilonFuera + p.recoger, tiempoRecogerPilon, true);
        // La puerta y el pilón (ya recogido) bajan juntos, y suben juntos.
        yield return Bajar(p, 0f, 1f, tiempoBajarPuerta);
        PuertaAbierta?.Invoke(p.puerta, p.puertaArriba, DireccionSalida(p));
        yield return new WaitForSeconds(tiempoAbajo);
        yield return Bajar(p, 1f, 0f, tiempoSubirPuerta);
        if (p.pilon != null) yield return Mover(p.pilon, p.pilonFuera + p.recoger, p.pilonFuera, tiempoSacarPilon, true);
        if (depurar)
        {
            Vector3 pilonRec = p.pilon != null && p.pilon.parent != null ? p.pilon.parent.TransformPoint(p.pilonFuera + p.recoger) : Vector3.zero;
            Debug.Log($"[Puertas] {p.puerta.name}: puerta baja {p.bajar.ToString("F3")} (mundo; su eje Y local es {p.puerta.up.ToString("F2")}). Pilón {(p.pilon ? p.pilon.name : "-")}: se recoge {(pilonRec - pilon0).ToString("F4")} (mundo). Vuelven a {p.puerta.position.ToString("F3")} / {(p.pilon ? p.pilon.position.ToString("F3") : "-")} (al empezar {puerta0.ToString("F3")} / {pilon0.ToString("F3")}).", p.puerta);
        }
        p.ocupada = false;
    }

    /// <summary>Hacia dónde se sale por la puerta: en horizontal, hacia el centro del mapa (el padre de las piezas).</summary>
    // ------------------------------------------------------------------ Fase final

    /// <summary>Abre las 4 puertas (pilón recogido y puerta abajo) y las deja abiertas: se para el ciclo de puertas.</summary>
    public void AbrirTodas()
    {
        StopAllCoroutines();
        foreach (var p in puertas) if (p.puerta != null) StartCoroutine(AbrirYQuedarse(p));
    }

    IEnumerator AbrirYQuedarse(Puerta p)
    {
        p.ocupada = true;
        if (p.pilon != null) yield return Mover(p.pilon, p.pilon.localPosition, p.pilonFuera + p.recoger, tiempoRecogerPilon, true);
        float u = p.bajar.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p.puerta.position - p.puertaArriba, p.bajar) / p.bajar.sqrMagnitude) : 1f;
        yield return Bajar(p, u, 1f, tiempoBajarPuerta * (1f - u));
    }

    /// <summary>Los huecos de las puertas: centro (con la puerta subida) y dirección hacia dentro del mapa.</summary>
    public List<(Vector3 centro, Vector3 haciaDentro)> Huecos()
    {
        var l = new List<(Vector3, Vector3)>();
        foreach (var p in puertas) if (p.puerta != null) l.Add((p.centroHueco, DireccionSalida(p)));
        return l;
    }

    static Vector3 DireccionSalida(Puerta p)
    {
        Vector3 centro = p.puerta.parent != null ? p.puerta.parent.position : Vector3.zero;
        Vector3 d = centro - p.puertaArriba;
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
    }

    /// <summary>Baja (o sube) la puerta y su pilón recogido a la vez: 0 = arriba, 1 = abajo.</summary>
    IEnumerator Bajar(Puerta p, float desde, float hasta, float duracion)
    {
        var rbPuerta = p.puerta.GetComponent<Rigidbody>();
        var rbPilon = p.pilon != null ? p.pilon.GetComponent<Rigidbody>() : null;
        Vector3 pilonRecogido = p.pilon != null && p.pilon.parent != null ? p.pilon.parent.TransformPoint(p.pilonFuera + p.recoger) : Vector3.zero;
        for (float k = 0f; ; k += Time.deltaTime)
        {
            float u = Mathf.Lerp(desde, hasta, Mathf.SmoothStep(0f, 1f, duracion > 0f ? Mathf.Clamp01(k / duracion) : 1f));
            Poner(p.puerta, rbPuerta, p.puertaArriba + p.bajar * u, false);
            if (p.pilon != null) Poner(p.pilon, rbPilon, pilonRecogido + p.bajar * u, false);
            if (k >= duracion) yield break;
            yield return null;
        }
    }

    /// <summary>Mueve con suavizado. Con <paramref name="local"/>, desde/hasta son posiciones locales (respecto al padre).</summary>
    static IEnumerator Mover(Transform t, Vector3 desde, Vector3 hasta, float duracion, bool local = false)
    {
        var rb = t.GetComponent<Rigidbody>();
        for (float k = 0f; k < duracion; k += Time.deltaTime)
        {
            Poner(t, rb, Vector3.Lerp(desde, hasta, Mathf.SmoothStep(0f, 1f, k / duracion)), local);
            yield return null;
        }
        Poner(t, rb, hasta, local);
    }

    static void Poner(Transform t, Rigidbody rb, Vector3 pos, bool local)
    {
        Vector3 mundo = local && t.parent != null ? t.parent.TransformPoint(pos) : pos;
        if (rb != null && rb.isKinematic) rb.MovePosition(mundo);
        else if (local) t.localPosition = pos;
        else t.position = mundo;
    }
}

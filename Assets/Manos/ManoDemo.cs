using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Prueba de las animaciones de la mano low poly.
/// 1 = golpe hacia abajo (vídeo 23.27.09), 2 = cerrar y abrir (vídeo 23.27.47).
/// Con "Repetir" activado, vuelve a lanzar la última animación cuando termina.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ManoDemo : MonoBehaviour
{
    public const string EstadoGolpe = "GolpeAbajo";
    public const string EstadoAbrir = "CerrarAbrir";

    public bool repetir = true;
    [Tooltip("Segundos de mezcla al cambiar o repetir la animación.")]
    public float mezcla = 0.25f;

    Animator anim;
    string actual = EstadoGolpe;

    void Awake() => anim = GetComponent<Animator>();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) Play(EstadoGolpe);
            if (kb.digit2Key.wasPressedThisFrame) Play(EstadoAbrir);
        }

        if (repetir && !anim.IsInTransition(0))
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.normalizedTime >= 1f) Play(actual);
        }
    }

    public void Play(string estado)
    {
        actual = estado;
        anim.CrossFadeInFixedTime(estado, mezcla, 0, 0f);
    }
}

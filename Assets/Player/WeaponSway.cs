using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sway del arma en primera persona (va en el arma, que es hija de la cámara):
/// - Al mirar, el arma se queda un poco atrás respecto a la cámara.
/// - Al moverse, se inclina al ir de lado y se retrasa al avanzar (más con el dash).
/// - Al andar, se balancea; al saltar y caer, sube y baja un poco.
/// Todo va en proporción a la velocidad de andar del jugador, así que funciona a cualquier escala de mapa.
///
/// Se aplica en LateUpdate encima de la rotación que pone el arma (su giro constante).
/// </summary>
public class WeaponSway : MonoBehaviour
{
    [Header("Al mirar")]
    [Tooltip("Grados de retraso por píxel de movimiento del ratón.")]
    public float lookSway = 0.03f;
    [Tooltip("Retraso máximo al mirar (grados).")]
    public float maxLookSway = 5f;

    [Header("Al moverse")]
    [Tooltip("Inclinación lateral (grados) al ir de lado a velocidad de andar.")]
    public float strafeTilt = 4f;
    [Tooltip("Desplazamiento del arma (unidades locales) al moverse a velocidad de andar.")]
    public float moveOffset = 0.012f;
    [Tooltip("Cuánto se nota la velocidad extra del dash (velocidad máxima considerada, en veces la de andar).")]
    public float maxSpeedFactor = 2.5f;

    [Header("Balanceo al andar")]
    public float bobAmount = 0.006f;
    [Tooltip("Ciclos por segundo andando.")]
    public float bobFrequency = 1.8f;

    [Header("Suavizado")]
    [Tooltip("Lo rápido que el arma sigue al sway (más = más pegada).")]
    public float smoothing = 10f;

    FirstPersonController player;
    Vector3 basePosition;
    Quaternion baseRotation;
    bool rotationDriven;  // el arma pone su propia rotación cada frame (CrystalWeapon); si no, se parte de la inicial
    Vector3 posOffset;
    Quaternion rotOffset = Quaternion.identity;
    float bobPhase;

    void Start()
    {
        player = GetComponentInParent<FirstPersonController>();
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        rotationDriven = GetComponent<CrystalWeapon>() != null;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // --- Al mirar: el arma va un poco por detrás del giro de la cámara.
        Vector2 look = Vector2.zero;
        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked) look += mouse.delta.ReadValue();
        var pad = Gamepad.current;
        if (pad != null) look += pad.rightStick.ReadValue() * 12f;
        float yaw = Mathf.Clamp(-look.x * lookSway, -maxLookSway, maxLookSway);
        float pitch = Mathf.Clamp(look.y * lookSway, -maxLookSway, maxLookSway);

        // --- Al moverse: velocidad en el espacio de la cámara, relativa a la de andar.
        Vector3 local = Vector3.zero;
        bool grounded = true;
        float walk = 1f;
        if (player != null)
        {
            walk = Mathf.Max(player.walkSpeed, 1e-4f);
            Vector3 v = transform.parent != null ? transform.parent.InverseTransformDirection(player.Velocity) : player.Velocity;
            local = Vector3.ClampMagnitude(v / walk, maxSpeedFactor);
            grounded = player.IsGrounded;
        }
        float roll = -local.x * strafeTilt;
        Vector3 targetPos = -local * moveOffset; // se queda atrás respecto al movimiento

        // --- Balanceo al andar (en forma de 8).
        float horizontalSpeed = new Vector2(local.x, local.z).magnitude;
        if (grounded && horizontalSpeed > 0.1f)
        {
            bobPhase += dt * bobFrequency * Mathf.PI * 2f * Mathf.Min(horizontalSpeed, 1.5f);
            targetPos += new Vector3(Mathf.Cos(bobPhase) * bobAmount, Mathf.Sin(bobPhase * 2f) * bobAmount * 0.5f, 0f);
        }

        Quaternion targetRot = Quaternion.Euler(pitch, yaw, roll);
        float k = 1f - Mathf.Exp(-smoothing * dt);
        posOffset = Vector3.Lerp(posOffset, targetPos, k);
        rotOffset = Quaternion.Slerp(rotOffset, targetRot, k);

        transform.localPosition = basePosition + posOffset;
        // Encima del giro que pone el arma en Update (o de la rotación inicial si nadie la cambia).
        transform.localRotation = rotOffset * (rotationDriven ? transform.localRotation : baseRotation);
    }
}

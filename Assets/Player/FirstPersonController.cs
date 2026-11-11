using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento en primera persona con CharacterController y cámara con ratón (o mando).
///
/// Teclado/ratón: WASD moverse, Shift dash, Espacio saltar, ratón mirar.
/// Mando: stick izquierdo moverse, stick derecho mirar, botón sur saltar, botón este (o pulsar el stick izquierdo) dash.
/// El dash va en la dirección en la que te mueves (W+D = diagonal); sin moverte, hacia delante.
/// El cursor se bloquea al hacer clic en el juego y se libera con Esc.
///
/// La cámara debe ser hija de este objeto (a la altura de los ojos). Para montarlo: GameObject > Jugador en primera persona.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Referencias")]
    public Camera cam;
    /// <summary>Si lo pone otro script (AtraerFragmentos), Shift lo usa primero él: si devuelve true, ese Shift no hace dash.</summary>
    [System.NonSerialized] public System.Func<bool> usarShift;

    [Header("Movimiento")]
    public float walkSpeed = 4f;
    [Tooltip("Lo rápido que alcanza la velocidad al empezar a moverse (más = más seco).")]
    public float acceleration = 12f;
    [Tooltip("Frenado: lo rápido que se para al soltar las teclas o al ir más rápido de lo normal (p.ej. tras un dash). Más = se para antes.")]
    public float deceleration = 10f;
    [Range(0f, 1f)] [Tooltip("Control en el aire respecto al suelo (afecta a acelerar y frenar).")]
    public float airControl = 0.3f;
    public float jumpHeight = 1f;
    public float gravity = -20f;

    [Header("Bunny hop y strafe (física de aire de Source / Counter-Strike)")]
    [Tooltip("Aceleración en el aire (sv_airaccelerate: 12 en CS:GO, 10 en CS 1.6). Más = se gana velocidad más fácil al hacer strafe.")]
    public float airAccelerate = 12f;
    [Tooltip("Tope de la velocidad deseada en el aire respecto a walkSpeed (en CS: 30 de 250 = 0.12). Es lo que permite ganar velocidad girando.")]
    [Range(0.01f, 1f)] public float airWishCap = 0.12f;
    [Tooltip("Velocidad máxima que se puede alcanzar con bunny hops y strafes, respecto a walkSpeed (1.3 = un 30 % más).")]
    public float maxBhopSpeed = 1.3f;
    [Tooltip("Margen para el bunny hop: un salto pulsado hasta estos segundos antes de tocar el suelo sale en el mismo fotograma del aterrizaje (sin frenar).")]
    [Range(0f, 0.15f)] public float bhopBuffer = 0.03f;
    [Tooltip("Saltar también con la rueda del ratón (como se suele hacer en CS para encadenar bunny hops).")]
    public bool jumpWithScroll = true;

    [Header("Surf (como en CS:GO)")]
    [Tooltip("Las superficies con más inclinación que Surf Min Angle se surfean: cuentas como en el aire (sin fricción ni salto), la velocidad se recorta contra la rampa y la gravedad te hace deslizar. Con strafe hacia la rampa te mantienes encima.")]
    public bool surf = true;
    [Tooltip("A partir de esta inclinación (grados) una superficie se surfea en vez de ser suelo. Casi 0 = solo el suelo plano es suelo (un pequeño margen evita que el suelo plano falle por imprecisión de las normales).")]
    [Range(0f, 89f)] public float surfMinAngle = 0.5f;
    [Tooltip("Solo se surfean las superficies con alguno de estos materiales (en mallas con varios materiales, el del triángulo que pisas). El resto es suelo normal. Vacío = no se surfea nada.")]
    public List<Material> surfMaterials = new List<Material>();
    [Tooltip("Capas que se pueden surfear.")]
    public LayerMask surfLayers = ~0;
    [Tooltip("En las zonas de surf, cada salto suma esta velocidad horizontal (m/s), como un bunny hop perfecto, sin depender del timing. Hasta el tope Max Bhop Speed.")]
    public float surfJumpSpeedGain = 0.4f;
    [Tooltip("Al saltar en una zona de surf, cuánto se gira la velocidad hacia donde quieres ir (teclas respecto a la vista; sin teclas, hacia donde miras). 0 = sigue la inercia, 1 = sale del todo hacia allí.")]
    [Range(0f, 1f)] public float surfJumpSteer = 0.85f;
    [Tooltip("Aceleración en el aire tras saltar desde una zona de surf (en los servidores de surf de CS se usa sv_airaccelerate 100-150). Más = el strafe con el ratón gira más la trayectoria. Vuelve a Air Accelerate al pisar suelo normal.")]
    public float surfAirAccelerate = 100f;
    [Tooltip("En el aire tras saltar desde una zona de surf: grados por segundo que gira tu velocidad hacia donde miras mientras pulsas teclas (sin perder rapidez). Con A o D se inclina además hacia ese lado. 0 = solo el strafe de CS.")]
    public float surfAirTurnRate = 300f;
    [Tooltip("Cuánto se inclina el giro hacia el lado de A o D (0 = hacia donde miras, 1 = 45° hacia ese lado).")]
    [Range(0f, 1f)] public float surfAirStrafeBias = 0.4f;
    [Tooltip("Velocidad máxima total (sv_maxvelocity de CS, 3500 u/s ≈ 66 m/s; aquí en m/s del mundo).")]
    public float maxVelocity = 40f;

    [Header("Velocímetro")]
    [Tooltip("Mostrar en pantalla la velocidad horizontal en m/s.")]
    public bool showSpeed = true;
    [Tooltip("Tamaño del texto (en píxeles, para una pantalla de 1080 de alto).")]
    public int speedFontSize = 28;
    [Tooltip("Mostrar también el estado del movimiento (SUELO, AIRE, SURF, ZONA SURF) debajo de la velocidad.")]
    public bool showMoveState = true;

    [Header("Dash")]
    [Tooltip("Distancia que recorre el dash.")]
    public float dashDistance = 2f;
    [Tooltip("Duración del dash en segundos.")]
    public float dashDuration = 0.15f;
    [Tooltip("Tiempo de espera entre dashes.")]
    public float dashCooldown = 0.6f;
    [Tooltip("Permitir el dash en el aire.")]
    public bool airDash = true;

    [Header("Cámara")]
    [Tooltip("Grados por píxel de movimiento del ratón.")]
    public float mouseSensitivity = 0.1f;
    [Tooltip("Grados por segundo con el stick derecho.")]
    public float gamepadSensitivity = 150f;
    public bool invertY;
    [Range(0f, 89f)] public float pitchLimit = 85f;
    [Tooltip("Suavizado de la cámara en segundos (0 = respuesta directa).")]
    [Range(0f, 0.2f)] public float lookSmoothing = 0.02f;

    [Header("Balanceo al andar")]
    public bool headBob = true;
    [Tooltip("Amplitud del balanceo (unidades de mundo).")]
    public float bobAmount = 0.03f;
    [Tooltip("Pasos por segundo andando.")]
    public float bobFrequency = 1.8f;

    CharacterController controller;
    Vector3 velocity;
    float yaw, pitch, targetYaw, targetPitch;
    Vector3 camBasePos;
    float bobPhase;
    Vector3 bobOffset;
    float dashTimeLeft;     // > 0 mientras dura el dash
    float nextDash;
    Vector3 dashVelocity;
    float jumpBufferedUntil = -1f;
    Vector3 surfNormal;
    int surfFrame = -10;
    int groundHitFrame = -10; // último fotograma que tocó suelo andable (manda sobre el surf)
    int surfZoneFrame = -10;  // último fotograma que tocó un material de surf (cualquier inclinación)
    bool fromSurf; // en el aire tras salir de una zona de surf (usa surfAirAccelerate)

    public bool IsDashing => dashTimeLeft > 0f;
    /// <summary>Velocidad actual del jugador en el mundo.</summary>
    public Vector3 Velocity => velocity;
    public bool IsGrounded => controller != null && controller.isGrounded && !IsSurfing;
    /// <summary>Tocando una rampa de surf (este fotograma o el anterior).</summary>
    public bool IsSurfing => surf && Time.frameCount - surfFrame <= 1 && Time.frameCount - groundHitFrame > 1;
    /// <summary>Tocando un material de surf (rampa o parte plana): ahí nunca se pierde el impulso.</summary>
    public bool OnSurfZone => surf && Time.frameCount - surfZoneFrame <= 1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cam == null) cam = GetComponentInChildren<Camera>();
    }

    void Start()
    {
        targetYaw = yaw = transform.eulerAngles.y;
        if (cam != null)
        {
            float x = cam.transform.localEulerAngles.x;
            targetPitch = pitch = x > 180f ? x - 360f : x;
            camBasePos = cam.transform.localPosition;
        }
        LockCursor(true);
    }

    void Update()
    {
        HandleCursor();
        Look();
        Move();
        HeadBob();
    }

    // ------------------------------------------------------------------ Cursor

    void HandleCursor()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ------------------------------------------------------------------ Cámara

    void Look()
    {
        Vector2 delta = Vector2.zero;
        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            delta += mouse.delta.ReadValue() * mouseSensitivity;
        var pad = Gamepad.current;
        if (pad != null)
            delta += pad.rightStick.ReadValue() * (gamepadSensitivity * Time.deltaTime);

        targetYaw += delta.x;
        targetPitch = Mathf.Clamp(targetPitch + (invertY ? delta.y : -delta.y), -pitchLimit, pitchLimit);

        float t = lookSmoothing > 0f ? 1f - Mathf.Exp(-Time.deltaTime / lookSmoothing) : 1f;
        yaw = Mathf.Lerp(yaw, targetYaw, t);
        pitch = Mathf.Lerp(pitch, targetPitch, t);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cam != null) cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // ------------------------------------------------------------------ Movimiento

    void Move()
    {
        var keyboard = Keyboard.current;
        var pad = Gamepad.current;

        Vector2 input = Vector2.zero;
        bool dash = false, jump = false;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            // Shift: si sirve para atraer un cristal (AtraerFragmentos), no hace dash.
            bool shift = keyboard.leftShiftKey.wasPressedThisFrame;
            if (shift && usarShift != null && usarShift()) shift = false;
            dash |= shift;
            jump |= keyboard.spaceKey.wasPressedThisFrame;
        }
        var mouseIn = Mouse.current;
        if (jumpWithScroll && mouseIn != null && Mathf.Abs(mouseIn.scroll.ReadValue().y) > 0.01f) jump = true;
        if (pad != null)
        {
            input += pad.leftStick.ReadValue();
            dash |= pad.buttonEast.wasPressedThisFrame || pad.leftStickButton.wasPressedThisFrame;
            jump |= pad.buttonSouth.wasPressedThisFrame;
        }
        input = Vector2.ClampMagnitude(input, 1f);

        bool grounded = IsGrounded; // en una rampa de surf no cuenta como suelo
        if (OnSurfZone) fromSurf = true;
        else if (grounded) fromSurf = false;
        // Salto con margen: si lo pulsas justo antes de aterrizar, salta en el fotograma en que toca el suelo.
        if (jump) jumpBufferedUntil = Time.time + bhopBuffer;
        bool jumpNow = jump || Time.time <= jumpBufferedUntil;
        // Bunny hop: el fotograma en que saltas desde el suelo no hay frenado del suelo (como en Source, que salta antes de aplicar la fricción).
        bool bhopFrame = grounded && jumpNow;
        Vector3 moveDir = transform.TransformDirection(new Vector3(input.x, 0f, input.y));
        Vector3 wish = moveDir * walkSpeed;

        // Dash: en la dirección de las teclas pulsadas (W+D = diagonal); sin teclas, hacia delante.
        if (dash && Time.time >= nextDash && (grounded || airDash) && dashDuration > 0f)
        {
            Vector3 dir = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : transform.forward;
            dashVelocity = dir * (dashDistance / dashDuration);
            dashTimeLeft = dashDuration;
            nextDash = Time.time + dashCooldown;
        }

        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        if (IsDashing)
        {
            // Velocidad fija durante el dash y sin caer; al terminar se frena con la deceleración.
            dashTimeLeft -= Time.deltaTime;
            horizontal = dashVelocity;
            velocity.y = Mathf.Max(velocity.y, 0f);
        }
        else if (grounded && !bhopFrame && !OnSurfZone)
        {
            // Suelo: igual que siempre. Acelerar hacia la velocidad deseada, o frenar si no hay teclas / va más rápido de lo normal.
            bool braking = wish.sqrMagnitude < 0.0001f || horizontal.sqrMagnitude > wish.sqrMagnitude;
            float rate = braking ? deceleration : acceleration;
            horizontal = Vector3.Lerp(horizontal, wish, 1f - Mathf.Exp(-rate * Time.deltaTime));
        }
        else
        {
            // Aire, fotograma de bunny hop o zona de surf (en la zona de surf siempre se mantiene el impulso, sin importar el timing).
            horizontal = AirMove(horizontal, wish, moveDir, OnSurfZone);
        }
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;

        if (grounded && velocity.y < 0f) velocity.y = -2f; // pegado al suelo en bajadas
        if ((grounded || IsSurfing) && jumpNow)
        {
            // También se puede saltar desde una rampa de surf (sale hacia arriba, sin perder la velocidad que lleves).
            float jumpSpeed = Mathf.Sqrt(2f * jumpHeight * -gravity);
            velocity.y = grounded ? jumpSpeed : Mathf.Max(velocity.y, 0f) + jumpSpeed;
            // Zona de surf: cada salto cuenta como bunny hop perfecto y suma velocidad (sin depender del timing).
            if (OnSurfZone || IsSurfing)
            {
                Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
                // Hacia dónde sale: girado hacia las teclas (respecto a la vista) o, sin teclas, hacia donde miras.
                Vector3 aim = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : transform.forward;
                Vector3 dir = h.sqrMagnitude > 0.01f ? Vector3.Slerp(h.normalized, aim, surfJumpSteer).normalized : aim;
                float mag = h.magnitude;
                float target = Mathf.Max(mag, Mathf.Min(mag + surfJumpSpeedGain, walkSpeed * maxBhopSpeed));
                h = dir * target;
                velocity.x = h.x;
                velocity.z = h.z;
            }
            jumpBufferedUntil = -1f;
            surfFrame = -10; // despega: no volver a pegarse a la rampa este fotograma
        }
        if (!IsDashing) velocity.y += gravity * Time.deltaTime;

        // Surf: la velocidad se recorta contra la rampa (ClipVelocity de Source) antes de moverse.
        if (IsSurfing) velocity = ClipVelocity(velocity, surfNormal);
        if (maxVelocity > 0f) velocity = Vector3.ClampMagnitude(velocity, maxVelocity);

        var flags = controller.Move(velocity * Time.deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f; // golpe con el techo
    }

    /// <summary>
    /// Movimiento en el aire. Por debajo de la velocidad normal se controla como siempre (airControl). Además se aplica
    /// la aceleración de aire de Source (PM_AirAccelerate): solo se acelera en la dirección de las teclas hasta un tope
    /// muy pequeño (airWishCap) de velocidad en ESA dirección, así que girando el ratón hacia el lado del strafe
    /// la dirección cambia y se va sumando velocidad. Sin teclas no se frena si llevas impulso (como en CS).
    /// </summary>
    Vector3 AirMove(Vector3 horizontal, Vector3 wish, Vector3 moveDir, bool keepMomentum = false)
    {
        float dt = Time.deltaTime;
        float before = horizontal.magnitude;
        bool hasInput = wish.sqrMagnitude > 0.0001f;

        // Control de siempre en el aire, solo en el régimen normal (sin impulso de bunny hop que perder).
        if (before <= walkSpeed + 0.01f)
        {
            bool braking = !hasInput || horizontal.sqrMagnitude > wish.sqrMagnitude;
            float rate = (braking ? deceleration : acceleration) * airControl;
            Vector3 steered = Vector3.Lerp(horizontal, wish, 1f - Mathf.Exp(-rate * dt));
            // En zona de surf nunca se frena: solo se acepta si no pierde velocidad.
            if (!keepMomentum || steered.sqrMagnitude >= horizontal.sqrMagnitude) horizontal = steered;
        }

        // Aceleración de aire de Source.
        if (hasInput)
        {
            Vector3 wishDir = moveDir.normalized;
            float wishSpeed = wish.magnitude;                       // 250 en CS
            float capped = Mathf.Min(wishSpeed, walkSpeed * airWishCap); // 30 en CS
            float current = Vector3.Dot(horizontal, wishDir);
            float add = capped - current;
            if (add > 0f)
            {
                float accel = Mathf.Min((fromSurf ? surfAirAccelerate : airAccelerate) * wishSpeed * dt, add);
                horizontal += wishDir * accel;
            }
        }

        // Tras saltar de una zona de surf: la velocidad gira hacia donde miras (y hacia el lado del strafe) sin perder rapidez.
        if (fromSurf && hasInput && surfAirTurnRate > 0f && horizontal.sqrMagnitude > 0.01f)
        {
            float fwd = Vector3.Dot(moveDir, transform.forward);
            float side = Vector3.Dot(moveDir, transform.right);
            if (fwd > -0.1f) // con S (hacia atrás) no se gira
            {
                Vector3 target = (transform.forward + transform.right * side * surfAirStrafeBias).normalized;
                float speed = horizontal.magnitude;
                horizontal = Vector3.RotateTowards(horizontal, target * speed, surfAirTurnRate * Mathf.Deg2Rad * dt, 0f);
                horizontal = horizontal.normalized * speed;
            }
        }

        // Tope: con bunny hops y strafes solo se gana hasta maxBhopSpeed (no recorta velocidad que ya tuvieras, p.ej. de un dash).
        float limit = Mathf.Max(walkSpeed * maxBhopSpeed, before);
        if (horizontal.magnitude > limit) horizontal = horizontal.normalized * limit;
        return horizontal;
    }

    // ------------------------------------------------------------------ Surf

    /// <summary>ClipVelocity de Source (overbounce 1): quita la parte de la velocidad que va contra la superficie.</summary>
    static Vector3 ClipVelocity(Vector3 v, Vector3 normal)
    {
        float into = Vector3.Dot(v, normal);
        return into < 0f ? v - normal * into : v;
    }

    /// <summary>¿Es de un material de surf lo que estás tocando? En mallas con varios materiales mira el del triángulo tocado.</summary>
    bool IsSurfMaterial(ControllerColliderHit hit)
    {
        if (surfMaterials == null || surfMaterials.Count == 0) return false;
        // El renderer puede estar en el mismo objeto que el collider, en un padre o en un hijo (según cómo se importe el modelo).
        var rend = hit.collider.GetComponent<Renderer>();
        if (rend == null) rend = hit.collider.GetComponentInParent<Renderer>();
        if (rend == null) rend = hit.collider.GetComponentInChildren<Renderer>();
        if (rend == null) return false;
        var mats = rend.sharedMaterials;
        if (mats.Length == 1) return surfMaterials.Contains(mats[0]);

        // Varios materiales: rayo corto contra el punto tocado para saber el triángulo y, de ahí, su submalla.
        if (hit.collider is MeshCollider mc && mc.sharedMesh != null && !mc.convex)
        {
            var ray = new Ray(hit.point + hit.normal * 0.05f, -hit.normal);
            if (mc.Raycast(ray, out var rh, 0.2f) && rh.triangleIndex >= 0)
            {
                var mesh = mc.sharedMesh;
                int tri = rh.triangleIndex * 3, start = 0;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    int count = (int)mesh.GetIndexCount(s);
                    if (tri < start + count) return surfMaterials.Contains(mats[s]);
                    start += count;
                }
            }
        }
        // Si no se puede saber el triángulo, vale cualquiera de sus materiales.
        foreach (var m in mats) if (surfMaterials.Contains(m)) return true;
        return false;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!surf || hit.collider == null || (surfLayers.value & (1 << hit.collider.gameObject.layer)) == 0) return;
        if (hit.collider.isTrigger || hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) return;
        float angle = Vector3.Angle(hit.normal, Vector3.up);
        bool surfable = IsSurfMaterial(hit);
        if (surfable) surfZoneFrame = Time.frameCount;
        // Suelo: lo plano, o cualquier superficie andable que no sea de un material de surf.
        if (angle <= surfMinAngle || (!surfable && angle <= controller.slopeLimit)) { groundHitFrame = Time.frameCount; return; }
        if (!surfable) return;
        // Rampa de surf (cualquier inclinación de un material de surf), salvo paredes verticales y techos.
        if (angle >= 89f) return;
        surfNormal = hit.normal;
        surfFrame = Time.frameCount;
        velocity = ClipVelocity(velocity, hit.normal);
    }

    // ------------------------------------------------------------------ Velocímetro

    GUIStyle speedStyle;

    void OnGUI()
    {
        if (!showSpeed) return;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        float scale = Screen.height / 1080f;
        if (speedStyle == null) speedStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        speedStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(speedFontSize * scale));
        // Blanco a velocidad normal; se vuelve verde según ganas velocidad con bunny hops (hasta el tope).
        float extra = Mathf.InverseLerp(walkSpeed, walkSpeed * Mathf.Max(1.0001f, maxBhopSpeed), speed);
        Color c = Color.Lerp(Color.white, new Color(0.4f, 1f, 0.5f), extra);
        string state = IsSurfing ? "SURF" : OnSurfZone ? "ZONA SURF" : IsGrounded ? "SUELO" : "AIRE";
        string text = speed.ToString("0.00") + " m/s" + (showMoveState ? "\n" + state : "");
        var rect = new Rect(0f, Screen.height * 0.62f, Screen.width, speedStyle.fontSize * (showMoveState ? 3.0f : 1.6f));
        var shadow = rect; shadow.x += 2f * scale; shadow.y += 2f * scale;
        speedStyle.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(shadow, text, speedStyle);
        speedStyle.normal.textColor = c;
        GUI.Label(rect, text, speedStyle);
    }

    // ------------------------------------------------------------------ Balanceo

    void HeadBob()
    {
        if (cam == null) return;

        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        Vector3 target = Vector3.zero;
        if (headBob && IsGrounded && !IsDashing && speed > walkSpeed * 0.1f)
        {
            bobPhase += Time.deltaTime * bobFrequency * Mathf.PI * (speed / Mathf.Max(walkSpeed, 0.01f));
            target = new Vector3(Mathf.Cos(bobPhase) * bobAmount * 0.5f, Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmount, 0f);
        }
        bobOffset = Vector3.Lerp(bobOffset, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        cam.transform.localPosition = camBasePos + bobOffset;
    }
}

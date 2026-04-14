using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class FreeRotateThenGlobeSpin : MonoBehaviour
{
    enum Mode { AutoSpin, Interacting, Waiting, Returning }

    [Header("Camera (leave empty to use Camera.main)")]
    public Transform viewCamera;

    [Header("Free rotate (while dragging)")]
    public float dragRotateSpeed = 0.2f;

    [Header("After release")]
    public float returnDelaySeconds = 2f;
    public float returnSpeed = 4f;          // how fast it settles upright

    [Header("Normal globe spin (idle)")]
    public float autoSpinDegPerSec = 12f;   // horizontal spin speed

    [Header("Zoom (optional)")]
    public bool enableZoom = true;
    public float zoomSpeedMouse = 0.15f;
    public float zoomSpeedPinch = 0.0025f;
    public float minScale = 0.6f;
    public float maxScale = 6.0f;

    Mode mode = Mode.AutoSpin;
    float stopTime = -999f;

    // Used in AutoSpin mode
    float yawAuto;

    // Drag state
    bool draggingMouse;
    Vector2 lastMousePos;

    // Pinch state
    float lastPinchDist;

    void Start()
    {
        if (!viewCamera && Camera.main) viewCamera = Camera.main.transform;

        // Initialize auto yaw from current rotation
        yawAuto = ExtractYawDegrees(transform.rotation);
        SetUprightYaw(yawAuto);
        mode = Mode.AutoSpin;
    }

    void Update()
    {
        bool interacted = HandleFreeRotate() | (enableZoom && HandleZoom());

        if (interacted)
        {
            mode = Mode.Interacting;
        }
        else
        {
            if (mode == Mode.Interacting)
            {
                mode = Mode.Waiting;
                stopTime = Time.time;
            }
        }

        if (mode == Mode.Waiting && (Time.time - stopTime) >= returnDelaySeconds)
        {
            mode = Mode.Returning;
        }

        if (mode == Mode.Returning)
        {
            // Return to upright, keeping current yaw
            float targetYaw = ExtractYawDegrees(transform.rotation);
            Quaternion target = Quaternion.Euler(0f, targetYaw, 0f);

            transform.rotation = Quaternion.Slerp(transform.rotation, target, returnSpeed * Time.deltaTime);

            // Once close enough, switch to autos pin
            if (Quaternion.Angle(transform.rotation, target) < 0.4f)
            {
                yawAuto = targetYaw;
                SetUprightYaw(yawAuto);
                mode = Mode.AutoSpin;
            }
        }

        if (mode == Mode.AutoSpin)
        {
            yawAuto += autoSpinDegPerSec * Time.deltaTime;
            SetUprightYaw(yawAuto);
        }
    }

    // --- Free rotation while dragging (no pole/equator constraint) ---
    bool HandleFreeRotate()
    {
        // Don’t rotate globe while interacting with UI (slider)
        if (IsPointerOverUI()) return false;

        if (!viewCamera && Camera.main) viewCamera = Camera.main.transform;
        if (!viewCamera) return false;

        bool used = false;

        // Mouse drag
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                lastMousePos = Mouse.current.position.ReadValue();
                draggingMouse = true;
                used = true;
            }

            if (draggingMouse && Mouse.current.leftButton.isPressed)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                Vector2 delta = pos - lastMousePos;
                lastMousePos = pos;

                ApplyDragRotation(delta);
                used = true;
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame)
                draggingMouse = false;
        }

        // 1-finger touch drag
        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;

            bool t0 = touches.Count > 0 && touches[0].press.isPressed;
            bool t1 = touches.Count > 1 && touches[1].press.isPressed;

            // Only rotate on single touch (so pinch doesn't fight rotation)
            if (t0 && !t1)
            {
                Vector2 delta = touches[0].delta.ReadValue();
                ApplyDragRotation(delta);
                used = true;
            }
        }

        return used;
    }

    void ApplyDragRotation(Vector2 delta)
    {
        float yaw = -delta.x * dragRotateSpeed;
        float pitch = delta.y * dragRotateSpeed;

        // Rotate around camera axes for natural trackball feel
        Quaternion qYaw = Quaternion.AngleAxis(yaw, viewCamera.up);
        Quaternion qPitch = Quaternion.AngleAxis(pitch, viewCamera.right);

        transform.rotation = qYaw * qPitch * transform.rotation;
    }

    // --- Zoom (optional) ---
    bool HandleZoom()
    {
        if (IsPointerOverUI()) return false;

        // Mouse wheel zoom
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                float factor = 1f + (scroll / 120f) * zoomSpeedMouse;
                SetScale(transform.localScale.x * factor);
                return true;
            }
        }

        // Pinch zoom
        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;

            bool t0 = touches.Count > 0 && touches[0].press.isPressed;
            bool t1 = touches.Count > 1 && touches[1].press.isPressed;

            if (t0 && t1)
            {
                Vector2 p0 = touches[0].position.ReadValue();
                Vector2 p1 = touches[1].position.ReadValue();
                float dist = Vector2.Distance(p0, p1);

                if (lastPinchDist == 0f)
                {
                    lastPinchDist = dist;
                    return true;
                }

                float delta = dist - lastPinchDist;
                lastPinchDist = dist;

                float factor = 1f + delta * zoomSpeedPinch;
                SetScale(transform.localScale.x * factor);
                return true;
            }
            else
            {
                lastPinchDist = 0f;
            }
        }

        return false;
    }

    void SetScale(float s)
    {
        s = Mathf.Clamp(s, minScale, maxScale);
        transform.localScale = new Vector3(s, s, s);
    }

    // --- Helpers ---
    void SetUprightYaw(float yawDeg)
    {
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    // Extract yaw from current rotation by projecting forward onto ground plane
    float ExtractYawDegrees(Quaternion rot)
    {
        Vector3 f = rot * Vector3.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f)
            return rot.eulerAngles.y;

        f.Normalize();
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

        // Mouse over UI
        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;

        // Touch over UI
        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (!touches[i].press.isPressed) continue;
                int id = touches[i].touchId.ReadValue();
                if (EventSystem.current.IsPointerOverGameObject(id))
                    return true;
            }
        }
        return false;
    }
}

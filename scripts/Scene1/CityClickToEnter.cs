using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CityClickToEnter : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public AQIOverlayFromJSON overlay;

    [Header("Load Scene")]
    public string citySceneName = "CityScene";

    [Header("Debug")]
    public bool debugLogs = true;

    // NEW: global guard (prevents double-trigger even if 2 scripts exist)
    private static bool s_loading = false;
    private static int s_instanceCount = 0;

    void Awake()
    {
        s_instanceCount++;
        if (s_instanceCount > 1 && debugLogs)
        {
            Debug.LogWarning(
                $"[CityClickToEnter] Multiple instances detected ({s_instanceCount}). " +
                $"You likely have this script on more than one GameObject."
            );
        }
    }

    void OnDestroy()
    {
        s_instanceCount = Mathf.Max(0, s_instanceCount - 1);
    }

    void Reset() => cam = Camera.main;

    void Update()
    {
        if (s_loading) return;

        if (cam == null) cam = Camera.main;
        if (cam == null || overlay == null) return;

        // 1) Get a tap/click position (works on phone + editor)
        if (!TryGetPointerDown(out Vector2 screenPos, out int pointerId))
            return;

        // 2) Don’t click through UI
        if (IsPointerOverUI(pointerId))
        {
            if (debugLogs) Debug.Log("Click blocked by UI.");
            return;
        }

        // 3) Raycast and ensure we hit the overlay object (not Earth)
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!TryGetOverlayHit(ray, out RaycastHit hit))
        {
            if (debugLogs) Debug.Log("Raycast did not hit PollutionOverlay.");
            return;
        }

        // 4) Prefer collider UV if available; otherwise compute UV from hit point
        Vector2 uv = hit.textureCoord;
        if (uv == Vector2.zero)
            uv = WorldPointToEquirectUV(hit.point, overlay.pollutionRenderer.transform);

        if (!overlay.TryGetCityAtUV(uv, out var city, out float aqi))
        {
            if (debugLogs) Debug.Log($"Hit overlay, but no city at UV {uv}. Try closer to a dot.");
            return;
        }

        // NEW: set the global guard BEFORE logging/saving (prevents 2nd instance same frame)
        if (s_loading) return;
        s_loading = true;

        if (debugLogs) Debug.Log($"City clicked: {city.id}  AQI={aqi}  dateIndex={overlay.CurrentDateIndex}");

        // 5) Save selection for next scene
        CitySelection.CityId = city.id;
        CitySelection.CityName = city.name;
        CitySelection.Lat = city.lat;
        CitySelection.Lon = city.lon;
        CitySelection.AQI = aqi;
        CitySelection.DateIndex = overlay.CurrentDateIndex;

        // 6) Load CityScene
        SceneManager.LoadScene(citySceneName, LoadSceneMode.Single);
    }

    bool TryGetOverlayHit(Ray ray, out RaycastHit overlayHit)
    {
        overlayHit = default;

        var targetGO = overlay.pollutionRenderer != null ? overlay.pollutionRenderer.gameObject : null;
        if (targetGO == null) return false;

        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f);
        if (hits == null || hits.Length == 0) return false;

        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var h in hits)
        {
            if (h.collider != null && h.collider.gameObject == targetGO)
            {
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    overlayHit = h;
                    found = true;
                }
            }
        }
        return found;
    }

    bool IsPointerOverUI(int pointerId)
    {
        if (EventSystem.current == null) return false;

        if (pointerId >= 0) return EventSystem.current.IsPointerOverGameObject(pointerId);
        return EventSystem.current.IsPointerOverGameObject();
    }

    static Vector2 WorldPointToEquirectUV(Vector3 worldPoint, Transform sphereTransform)
    {
        Vector3 local = sphereTransform.InverseTransformPoint(worldPoint);
        local.Normalize();

        float lon = Mathf.Atan2(local.x, local.z);
        float lat = Mathf.Asin(local.y);

        float u = (lon / (2f * Mathf.PI)) + 0.5f;
        float v = (lat / Mathf.PI) + 0.5f;

        u = Mathf.Repeat(u, 1f);
        v = Mathf.Clamp01(v);

        return new Vector2(u, v);
    }

    bool TryGetPointerDown(out Vector2 screenPos, out int pointerId)
    {
        screenPos = default;
        pointerId = -1;

#if ENABLE_INPUT_SYSTEM
        // Touch (Android)
        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;
            if (touches.Count > 0 && touches[0].press.wasPressedThisFrame)
            {
                screenPos = touches[0].position.ReadValue();
                pointerId = touches[0].touchId.ReadValue();
                return true;
            }
        }

        // Mouse (Editor)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            pointerId = -1;
            return true;
        }

        return false;
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            screenPos = Input.GetTouch(0).position;
            pointerId = Input.GetTouch(0).fingerId;
            return true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            pointerId = -1;
            return true;
        }

        return false;
#endif
    }
}

using UnityEngine;

/// <summary>
/// Adjusts a RectTransform's anchors to match the physical device's Screen.safeArea,
/// ensuring that critical UI elements (score, buttons, dialogs) never get clipped
/// by notches, punch-hole cameras, navigation bars, or rounded corners.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaHandler : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    void OnEnable()
    {
        ApplySafeArea();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea ||
            Screen.width != lastScreenSize.x ||
            Screen.height != lastScreenSize.y ||
            Screen.orientation != lastOrientation)
        {
            ApplySafeArea();
        }
    }

    void OnRectTransformDimensionsChange()
    {
        ApplySafeArea();
    }

    /// <summary>
    /// Applies normalized safe area anchors [0, 1] to the attached RectTransform.
    /// </summary>
    public void ApplySafeArea()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (rectTransform == null) return;

        lastSafeArea = Screen.safeArea;
        lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        lastOrientation = Screen.orientation;

        if (Screen.width <= 0 || Screen.height <= 0) return;

        // Convert safe area pixel coordinates to normalized 0..1 anchors
        Vector2 minAnchor = lastSafeArea.position;
        Vector2 maxAnchor = minAnchor + lastSafeArea.size;

        minAnchor.x /= Screen.width;
        minAnchor.y /= Screen.height;
        maxAnchor.x /= Screen.width;
        maxAnchor.y /= Screen.height;

        // Ensure valid range
        minAnchor.x = Mathf.Clamp01(minAnchor.x);
        minAnchor.y = Mathf.Clamp01(minAnchor.y);
        maxAnchor.x = Mathf.Clamp(maxAnchor.x, minAnchor.x, 1f);
        maxAnchor.y = Mathf.Clamp(maxAnchor.y, minAnchor.y, 1f);

        rectTransform.anchorMin = minAnchor;
        rectTransform.anchorMax = maxAnchor;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Static helper to get safe area normalized boundaries.
    /// </summary>
    public static Rect GetNormalizedSafeArea()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
            return new Rect(0, 0, 1, 1);

        Rect safe = Screen.safeArea;
        return new Rect(
            safe.x / Screen.width,
            safe.y / Screen.height,
            safe.width / Screen.width,
            safe.height / Screen.height
        );
    }
}

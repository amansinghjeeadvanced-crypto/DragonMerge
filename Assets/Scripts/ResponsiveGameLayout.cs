using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Master responsive layout controller for the portrait mobile dragon merge game.
/// Ensures the arena artwork preserves its aspect ratio without stretching,
/// scales the playable container (walls, ground, spawner, upper boundary) to perfectly align
/// with the visible fantasy arena on any mobile screen or aspect ratio (16:9, 18:9, 19.5:9, 20:9),
/// and guarantees that Score and Lifelines remain fully visible inside the device's Safe Area.
/// </summary>
[ExecuteAlways]
public class ResponsiveGameLayout : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("RectTransform of the arena background Image (Bg1.png)")]
    public RectTransform arenaImageRect;

    [Tooltip("RectTransform of the Board container where dragons play and walls are spawned")]
    public RectTransform boardRect;

    [Tooltip("DragonSpawner component attached to DragonSpawner")]
    public DragonSpawner spawner;

    [Header("UI Elements (Inside Safe Area)")]
    [Tooltip("RectTransform of the main gameplay score Text (TMP)")]
    public RectTransform scoreRect;

    [Tooltip("RectTransform of the LifelinesArea container")]
    public RectTransform lifelinesRect;

    // Artwork reference specifications
    public const float NativeArtworkWidth = 911f;
    public const float NativeArtworkHeight = 1727f;
    public const float NativeArtworkAspect = NativeArtworkWidth / NativeArtworkHeight; // ~0.5275043f

    // Proportional landmarks relative to reference design (1284 x 2778)
    public const float RefDesignWidth = 1284f;
    public const float RefDesignHeight = 2778f;

    public const float RatioContainerHalfWidth = 420f / RefDesignWidth;  // 0.327103f
    public const float RatioWallVisualWidth   = 180f / RefDesignWidth;  // 0.140187f
    public const float RatioScoreY             = 1193f / RefDesignHeight; // 0.429446f
    public const float RatioLifelinesY         = 930f / RefDesignHeight;  // 0.334773f
    public const float RatioSpawnerY           = 701f / RefDesignHeight;  // 0.252340f
    public const float RatioUpperBoundaryY     = 501f / RefDesignHeight;  // 0.180346f
    public const float RatioGroundY            = -1195f / RefDesignHeight;// -0.430166f

    private Vector2 lastParentSize = Vector2.zero;
    private Rect lastSafeArea = Rect.zero;

    void Awake()
    {
        AutoFindReferences();
        ApplyLayout();
    }

    void OnEnable()
    {
        AutoFindReferences();
        ApplyLayout();
    }

    void Update()
    {
        RectTransform parentRect = transform as RectTransform;
        if (parentRect == null) return;

        Vector2 curParentSize = parentRect.rect.size;
        Rect curSafe = Screen.safeArea;

        if (curParentSize != lastParentSize || curSafe != lastSafeArea)
        {
            ApplyLayout();
        }
    }

    void OnRectTransformDimensionsChange()
    {
        ApplyLayout();
    }

    public void AutoFindReferences()
    {
        if (arenaImageRect == null)
        {
            Transform imgT = transform.Find("Image");
            if (imgT != null) arenaImageRect = imgT as RectTransform;
        }

        if (boardRect == null)
        {
            Transform bT = transform.Find("Board");
            if (bT != null) boardRect = bT as RectTransform;
        }

        if (spawner == null && boardRect != null)
        {
            spawner = boardRect.GetComponentInChildren<DragonSpawner>(true);
        }

        if (scoreRect == null)
        {
            Transform sT = transform.Find("Text (TMP)");
            if (sT != null) scoreRect = sT as RectTransform;
        }

        if (lifelinesRect == null)
        {
            Transform lT = transform.Find("LifelinesArea");
            if (lT != null) lifelinesRect = lT as RectTransform;
        }
    }

    /// <summary>
    /// Computes and applies proportional, aspect-ratio-preserving sizes and positions
    /// across the arena artwork, gameplay boundaries, score, and lifeline buttons.
    /// </summary>
    public void ApplyLayout()
    {
        RectTransform parentRect = transform as RectTransform;
        if (parentRect == null) return;

        float screenW = parentRect.rect.width;
        float screenH = parentRect.rect.height;
        if (screenW <= 100f || screenH <= 100f) return;

        lastParentSize = new Vector2(screenW, screenH);
        lastSafeArea = Screen.safeArea;

        // 1. Calculate Arena size maintaining exact aspect ratio (Envelope mode to fill screen)
        float currentAspect = screenW / screenH;
        float arenaW, arenaH;

        if (currentAspect >= NativeArtworkAspect)
        {
            // Screen is wider than native artwork (e.g. 16:9): fill width, let height envelope
            arenaW = screenW;
            arenaH = screenW / NativeArtworkAspect;
        }
        else
        {
            // Screen is taller than native artwork (e.g. 20:9): fill height, let width envelope
            arenaH = screenH;
            arenaW = screenH * NativeArtworkAspect;
        }

        Vector2 arenaSize = new Vector2(arenaW, arenaH);

        // 2. Size and position the Background Arena Artwork
        if (arenaImageRect != null)
        {
            arenaImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            arenaImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            arenaImageRect.pivot = new Vector2(0.5f, 0.5f);
            arenaImageRect.anchoredPosition = Vector2.zero;
            arenaImageRect.sizeDelta = arenaSize;
        }

        // 3. Size and position the Board container to align with the visible arena
        if (boardRect != null)
        {
            boardRect.anchorMin = new Vector2(0.5f, 0.5f);
            boardRect.anchorMax = new Vector2(0.5f, 0.5f);
            boardRect.pivot = new Vector2(0.5f, 0.5f);
            boardRect.anchoredPosition = Vector2.zero;
            boardRect.sizeDelta = arenaSize;
        }

        // 4. Ensure UI elements (Lifelines, Score) render on top of Board with first raycast priority
        if (arenaImageRect != null) arenaImageRect.SetSiblingIndex(0);
        if (boardRect != null) boardRect.SetSiblingIndex(1);
        if (lifelinesRect != null) lifelinesRect.SetSiblingIndex(2);
        if (scoreRect != null) scoreRect.SetSiblingIndex(3);

        // 4. Safe Area boundaries in Canvas local units
        float safeTopNormalized = (Screen.height > 0) ? (Screen.safeArea.yMax / Screen.height) : 1f;
        float safeBottomNormalized = (Screen.height > 0) ? (Screen.safeArea.yMin / Screen.height) : 0f;

        float canvasTopY = screenH * 0.5f;
        float canvasBottomY = -screenH * 0.5f;
        float safeTopLocalY = Mathf.Lerp(canvasBottomY, canvasTopY, safeTopNormalized);
        float safeBottomLocalY = Mathf.Lerp(canvasBottomY, canvasTopY, safeBottomNormalized);

        // 5. Responsive Score position (inside golden header plaque, never past safe area top)
        if (scoreRect != null)
        {
            scoreRect.anchorMin = new Vector2(0.5f, 0.5f);
            scoreRect.anchorMax = new Vector2(0.5f, 0.5f);
            scoreRect.pivot = new Vector2(0.5f, 0.5f);

            float targetScoreY = arenaH * RatioScoreY;
            float maxScoreY = safeTopLocalY - (scoreRect.rect.height * 0.5f) - 8f;
            float finalScoreY = Mathf.Min(targetScoreY, maxScoreY);

            scoreRect.anchoredPosition = new Vector2(0f, finalScoreY);
        }

        // 6. Responsive LifelinesArea position (below score, above spawner)
        if (lifelinesRect != null)
        {
            lifelinesRect.anchorMin = new Vector2(0.5f, 0.5f);
            lifelinesRect.anchorMax = new Vector2(0.5f, 0.5f);
            lifelinesRect.pivot = new Vector2(0.5f, 0.5f);

            float targetLifelinesY = arenaH * RatioLifelinesY;

            // Ensure lifelines stay comfortably below score
            if (scoreRect != null)
            {
                float scoreBottom = scoreRect.anchoredPosition.y - (scoreRect.rect.height * 0.5f);
                float maxLifelinesY = scoreBottom - (lifelinesRect.rect.height * 0.5f) - 5f;
                targetLifelinesY = Mathf.Min(targetLifelinesY, maxLifelinesY);
            }

            lifelinesRect.anchoredPosition = new Vector2(0f, targetLifelinesY);

            // Responsive width clamping for 4 buttons on narrow devices
            float maxAvailWidth = Mathf.Min(screenW - 40f, 780f);
            lifelinesRect.sizeDelta = new Vector2(maxAvailWidth, 170f);
        }

        // 7. Update DragonSpawner boundaries aligned with current responsive arena
        if (spawner != null)
        {
            float halfWidth = arenaW * RatioContainerHalfWidth;
            float wallWidth = arenaW * RatioWallVisualWidth;
            float upperY = arenaH * RatioUpperBoundaryY;
            float groundY = arenaH * RatioGroundY;
            float spawnerY = arenaH * RatioSpawnerY;

            // Ensure ground stays above bottom safe area (home gesture indicator)
            float minGroundY = safeBottomLocalY + 30f;
            if (groundY < minGroundY && (safeBottomLocalY > canvasBottomY + 20f))
            {
                groundY = minGroundY;
            }

            spawner.UpdateResponsiveDimensions(halfWidth, wallWidth, upperY, groundY, spawnerY);
        }
    }
}

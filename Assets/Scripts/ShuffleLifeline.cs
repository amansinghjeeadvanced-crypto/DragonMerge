using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the "Shuffle" lifeline:
/// - Starts with 1 use per game (×1).
/// - Tapping Shuffle immediately activates it without requiring target selection.
/// - Finds all currently dropped Dragon objects inside the playable Board area.
/// - Does NOT affect the currently held/aiming dragon at the top.
/// - Temporarily pauses physics simulation ONLY for those dropped dragons so they can safely reposition.
/// - Randomly redistributes them within the playable container (inside left/right walls and above ground).
/// - Ensures dragons do not obviously overlap upon repositioning.
/// - Actually assigns the new positions to the dragons' Transform/RectTransform and Rigidbody2D.
/// - Resets Rigidbody2D linear and angular velocity.
/// - Restores normal Dynamic Rigidbody2D physics and simulation so gravity and collisions settle the board naturally.
/// - Zero score directly awarded; normal merges continue if matching dragons collide.
/// - Count updates: ×1 -> ×0. Button disables at ×0.
/// - Start Game & Restart Game reset use count to ×1.
/// - Game Over disables interaction.
/// </summary>
public class ShuffleLifeline : MonoBehaviour
{
    public static ShuffleLifeline Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("The existing ShuffleButton")]
    public Button shuffleButton;

    [Tooltip("Display text for remaining uses (×1, ×0)")]
    public TMP_Text countText;

    [Tooltip("Text label on the button (e.g. SHUFFLE)")]
    public TMP_Text labelText;

    [Tooltip("Target graphic for color pulse highlights")]
    public Image buttonImage;

    [Header("Configuration")]
    [Tooltip("Maximum number of uses per game session")]
    public int maxUses = 1;

    [Tooltip("Total duration of the shuffle animation in seconds")]
    public float shuffleAnimationDuration = 0.42f;

    // State
    public int remainingUses { get; private set; } = 1;
    public bool isShuffling { get; private set; } = false;

    private RectTransform boardRect;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        AutoFindReferences();
    }

    private void AutoFindReferences()
    {
        if (shuffleButton == null)
        {
            shuffleButton = GetComponent<Button>();
            if (shuffleButton == null)
            {
                GameObject btnObj = GameObject.Find("ShuffleButton");
                if (btnObj != null) shuffleButton = btnObj.GetComponent<Button>();
            }
        }

        if (shuffleButton != null)
        {
            if (buttonImage == null)
            {
                buttonImage = shuffleButton.GetComponent<Image>();
            }

            if (countText == null)
            {
                Transform countT = shuffleButton.transform.Find("CountText");
                if (countT != null)
                {
                    countText = countT.GetComponent<TMP_Text>();
                }
                else
                {
                    TMP_Text[] texts = shuffleButton.GetComponentsInChildren<TMP_Text>(true);
                    foreach (var t in texts)
                    {
                        if (t.name.Contains("Count") || t.text.StartsWith("×") || t.text.StartsWith("x"))
                        {
                            countText = t;
                            break;
                        }
                    }
                }
            }

            if (labelText == null)
            {
                Transform labelT = shuffleButton.transform.Find("Text (TMP)");
                if (labelT != null) labelText = labelT.GetComponent<TMP_Text>();
            }

            shuffleButton.onClick.RemoveListener(OnShuffleButtonClicked);
            shuffleButton.onClick.AddListener(OnShuffleButtonClicked);
        }

        EnsureBoardReference();

        remainingUses = maxUses;
        UpdateUI();
    }

    private void EnsureBoardReference()
    {
        if (boardRect == null)
        {
            GameObject boardObj = GameObject.Find("Board");
            if (boardObj != null)
            {
                boardRect = boardObj.GetComponent<RectTransform>();
            }
        }
    }

    void Start()
    {
        UpdateUI();
    }

    /// <summary>
    /// Called when the player clicks the existing ShuffleButton.
    /// Immediately activates the shuffle.
    /// </summary>
    public void OnShuffleButtonClicked()
    {
        if (remainingUses <= 0 || isShuffling) return;

        // Do not allow while game over screen is shown
        if (GameManager.Instance != null && GameManager.Instance.gameOverPanel != null && GameManager.Instance.gameOverPanel.activeInHierarchy)
        {
            return;
        }

        StartCoroutine(ExecuteShuffleRoutine());
    }

    /// <summary>
    /// Executes the full shuffle operation:
    /// - Collects all dropped board dragons (aiming dragon is excluded).
    /// - Plays shuffle audio once.
    /// - Spawns a magical swirl VFX.
    /// - Animates dragons (shrink/fade, smooth reposition, pop back up).
    /// - Sets transform and rb.position, calls Physics2D.SyncTransforms().
    /// - Restores normal Dynamic Rigidbody2D physics and wakes them up.
    /// </summary>
    private IEnumerator ExecuteShuffleRoutine()
    {
        isShuffling = true;
        EnsureBoardReference();

        // Cancel any active targeting lifelines so modes don't collide
        if (RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
        {
            RemoveDragonLifeline.Instance.SetRemoveMode(false);
        }
        if (DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
        {
            DragonBlastLifeline.Instance.SetBlastMode(false);
        }

        // 1. Find all currently dropped Dragon objects that are inside the playable Board area.
        // STRICT REQUIREMENT:
        // - Must NOT include the currently held/aiming dragon (d.isDropped == false).
        // - Must NOT include destroyed or inactive dragons.
        // - Must NOT include UI objects.
        // - Must NOT include Dragon prefabs/assets.
        List<Dragon> droppedDragons = new List<Dragon>();
        Dragon[] candidateDragons = (boardRect != null)
            ? boardRect.GetComponentsInChildren<Dragon>(false)
            : FindObjectsByType<Dragon>(FindObjectsInactive.Exclude);

        foreach (Dragon d in candidateDragons)
        {
            if (d == null || !d.gameObject.activeInHierarchy) continue;
            if (!d.gameObject.scene.isLoaded) continue; // Exclude prefabs/assets
            if (!d.isDropped) continue; // Strictly exclude currently held/aiming dragon
            if (d.isMerging) continue; // Exclude dragons in the middle of being merged

            droppedDragons.Add(d);
        }

        // REQUIRED DEBUG LOG: Number of dropped dragons found
        Debug.Log($"[ShuffleLifeline] Number of dropped dragons found: {droppedDragons.Count}");

        if (droppedDragons.Count == 0)
        {
            Debug.Log("[ShuffleLifeline] No dropped dragons on board to shuffle. Aborting.");
            isShuffling = false;
            yield break;
        }

        // 2. Play shuffle sound ONCE via AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayShuffle();
        }

        // 3. Temporarily pause physics simulation ONLY for these dropped dragons so they can safely reposition
        foreach (Dragon d in droppedDragons)
        {
            Rigidbody2D rb = d.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false; // Pause Box2D simulation
            }
        }

        // 4. Record old positions for animation & debugging
        List<Vector2> oldPositions = new List<Vector2>();
        foreach (Dragon d in droppedDragons)
        {
            RectTransform rt = d.GetComponent<RectTransform>();
            oldPositions.Add(rt != null ? rt.anchoredPosition : (Vector2)d.transform.localPosition);
        }

        // 5. Calculate safe, non-overlapping target positions for all dragons
        Dictionary<Dragon, Vector2> targetPositions = CalculateShufflePositions(droppedDragons);

        // REQUIRED DEBUG LOG: Number of dragons repositioned and each old -> new position
        Debug.Log($"[ShuffleLifeline] Number of dragons repositioned: {droppedDragons.Count}");
        for (int i = 0; i < droppedDragons.Count; i++)
        {
            Dragon d = droppedDragons[i];
            Vector2 oldPos = oldPositions[i];
            Vector2 newPos = targetPositions[d];
            Debug.Log($"[ShuffleLifeline] Dragon '{d.name}' (ID: {d.dragonId}) Old Position: {oldPos} -> New Position: {newPos}");
        }

        // 6. Spawn magical whirlwind / swirl VFX in the play area
        CreateShuffleVisualEffect();

        // 7. Animation:
        //    Phase 1 (~0.18s): dragons shrink down slightly & move toward new target positions
        //    Phase 2 (~0.24s): dragons pop back to normal scale at target positions
        List<RectTransform> rects = new List<RectTransform>();
        List<Image> images = new List<Image>();
        List<Vector3> origScales = new List<Vector3>();
        List<Color> origColors = new List<Color>();

        foreach (Dragon d in droppedDragons)
        {
            RectTransform rt = d.GetComponent<RectTransform>();
            rects.Add(rt);
            origScales.Add(rt.localScale);

            Image img = d.GetComponent<Image>();
            images.Add(img);
            origColors.Add((img != null) ? img.color : Color.white);
        }

        float phase1Duration = 0.18f;
        float elapsed = 0f;
        while (elapsed < phase1Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase1Duration);
            float ease = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i] != null && i < droppedDragons.Count)
                {
                    Vector2 startPos = oldPositions[i];
                    Vector2 endPos = targetPositions[droppedDragons[i]];
                    rects[i].anchoredPosition = Vector2.Lerp(startPos, endPos, ease);
                    rects[i].localScale = Vector3.Lerp(origScales[i], origScales[i] * 0.35f, ease);

                    if (images[i] != null)
                    {
                        images[i].color = new Color(origColors[i].r, origColors[i].g, origColors[i].b, Mathf.Lerp(origColors[i].a, 0.6f, ease));
                    }
                }
            }
            yield return null;
        }

        // Snap precisely to target positions
        for (int i = 0; i < rects.Count; i++)
        {
            if (rects[i] != null && i < droppedDragons.Count)
            {
                Vector2 targetPos = targetPositions[droppedDragons[i]];
                rects[i].anchoredPosition = targetPos;
                droppedDragons[i].transform.localPosition = new Vector3(targetPos.x, targetPos.y, 0f);
            }
        }

        float phase2Duration = Mathf.Max(0.15f, shuffleAnimationDuration - phase1Duration);
        elapsed = 0f;
        while (elapsed < phase2Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase2Duration);
            float scalePop = Mathf.Sin(t * Mathf.PI * 0.5f);
            float bounceScale = Mathf.Lerp(0.35f, 1.0f, scalePop);

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i] != null)
                {
                    rects[i].localScale = origScales[i] * bounceScale;

                    if (images[i] != null)
                    {
                        images[i].color = new Color(origColors[i].r, origColors[i].g, origColors[i].b, Mathf.Lerp(0.6f, origColors[i].a, t));
                    }
                }
            }
            yield return null;
        }

        // 8. FINAL REPOSITION ASSIGNMENT & PHYSICS SYNCHRONIZATION
        // Actually assign the new positions to the dragons' existing Transform/RectTransform positions,
        // sync to Rigidbody2D, reset velocities, and restore Dynamic simulation!
        for (int i = 0; i < droppedDragons.Count; i++)
        {
            Dragon d = droppedDragons[i];
            if (d == null) continue;

            RectTransform rt = d.GetComponent<RectTransform>();
            Vector2 targetPos = targetPositions[d];

            if (rt != null)
            {
                rt.anchoredPosition = targetPos;
            }
            d.transform.localPosition = new Vector3(targetPos.x, targetPos.y, 0f);
            rt.localScale = origScales[i];
            if (images[i] != null) images[i].color = origColors[i];

            Rigidbody2D rb = d.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                // Assign new world position directly to Box2D body
                rb.position = (Vector2)d.transform.position;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.simulated = true;
                rb.WakeUp();
            }
        }

        // Force Unity 2D physics engine to explicitly synchronize transforms with Box2D bodies
        Physics2D.SyncTransforms();

        // 9. Consume 1 use
        remainingUses--;
        UpdateUI();

        isShuffling = false;
    }

    /// <summary>
    /// Computes safe, non-overlapping target positions for all shuffled dragons inside the container.
    /// </summary>
    private Dictionary<Dragon, Vector2> CalculateShufflePositions(List<Dragon> dragons)
    {
        var positions = new Dictionary<Dragon, Vector2>();

        DragonSpawner spawner = FindAnyObjectByType<DragonSpawner>();
        float leftEdge = (spawner != null) ? spawner.cachedLeftEdge : -420f;
        float rightEdge = (spawner != null) ? spawner.cachedRightEdge : 420f;

        float groundTopY = -858f;
        if (spawner != null && spawner.ground != null)
        {
            groundTopY = spawner.ground.anchoredPosition.y + (spawner.ground.rect.height * 0.5f);
        }

        float maxSafeY = (spawner != null) ? Mathf.Min(spawner.upperBoundaryY - 100f, Mathf.Lerp(groundTopY, spawner.upperBoundaryY, 0.75f)) : 380f;

        // Sort dragons by size (largest first) to place bigger dragons with priority
        List<Dragon> sortedDragons = new List<Dragon>(dragons);
        sortedDragons.Sort((a, b) => (b.GetColliderRadius() * b.transform.lossyScale.x).CompareTo(a.GetColliderRadius() * a.transform.lossyScale.x));

        List<(Vector2 pos, float radius)> placed = new List<(Vector2, float)>();

        foreach (Dragon d in sortedDragons)
        {
            float radius = d.GetColliderRadius() * d.transform.lossyScale.x;
            float minX = leftEdge + radius + 15f;
            float maxX = rightEdge - radius - 15f;
            float minY = groundTopY + radius + 15f;
            float maxY = Mathf.Max(minY + 60f, maxSafeY - radius);

            Vector2 bestPos = new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
            float maxMinClearance = -1e9f;

            // Rejection sampling attempts to find non-overlapping coordinates
            for (int attempt = 0; attempt < 300; attempt++)
            {
                float testX = Random.Range(minX, maxX);
                float testY = Random.Range(minY, maxY);
                Vector2 testPos = new Vector2(testX, testY);

                float minClearance = float.MaxValue;
                bool overlaps = false;

                foreach (var other in placed)
                {
                    float dist = Vector2.Distance(testPos, other.pos);
                    float clearance = dist - (radius + other.radius);

                    if (clearance < 8f) // 8px buffer
                    {
                        overlaps = true;
                    }
                    if (clearance < minClearance)
                    {
                        minClearance = clearance;
                    }
                }

                if (!overlaps)
                {
                    bestPos = testPos;
                    break;
                }

                if (minClearance > maxMinClearance)
                {
                    maxMinClearance = minClearance;
                    bestPos = testPos;
                }
            }

            placed.Add((bestPos, radius));
            positions[d] = bestPos;
        }

        return positions;
    }

    /// <summary>
    /// Spawns a magical swirl / whirlwind visual effect in the play area during the shuffle.
    /// </summary>
    private void CreateShuffleVisualEffect()
    {
        EnsureBoardReference();
        Transform parentTransform = (boardRect != null) ? boardRect : transform;
        GameObject swirlObj = new GameObject("ShuffleSwirlEffect");
        swirlObj.transform.SetParent(parentTransform, false);
        swirlObj.transform.localPosition = new Vector3(0f, -200f, 0f);

        RectTransform rt = swirlObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(750f, 750f);

        Image img = swirlObj.AddComponent<Image>();
        img.color = new Color(0.4f, 0.85f, 1f, 0.45f); // Soft cyan/magic wind
        img.raycastTarget = false;

        StartCoroutine(AnimateSwirl(swirlObj, rt, img, shuffleAnimationDuration));
    }

    private IEnumerator AnimateSwirl(GameObject obj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 0.3f;
        Vector3 peakScale = Vector3.one * 1.3f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (rt != null)
            {
                float scale = Mathf.Sin(t * Mathf.PI);
                rt.localScale = Vector3.Lerp(initialScale, peakScale, scale);
                rt.localEulerAngles = new Vector3(0f, 0f, t * 360f);
            }

            if (img != null)
            {
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.5f;
                img.color = new Color(0.45f, 0.85f, 1f, alpha);
            }

            yield return null;
        }

        if (obj != null)
        {
            Destroy(obj);
        }
    }

    /// <summary>
    /// Resets the Shuffle lifeline to 1 use (×1) and re-enables interaction.
    /// Called when Start Game or Restart Game is pressed.
    /// </summary>
    public void ResetUses()
    {
        remainingUses = maxUses;
        isShuffling = false;
        UpdateUI();
    }

    /// <summary>
    /// Cancels active operations and disables interaction on Game Over.
    /// </summary>
    public void OnGameOver()
    {
        StopAllCoroutines();
        isShuffling = false;
        if (shuffleButton != null)
        {
            shuffleButton.interactable = false;
        }
    }

    /// <summary>
    /// Updates the button count text (×1, ×0) and interactable state.
    /// </summary>
    public void UpdateUI()
    {
        if (countText != null)
        {
            countText.text = $"×{remainingUses}";
        }

        if (shuffleButton != null)
        {
            shuffleButton.interactable = (remainingUses > 0 && !isShuffling);
        }
    }
}

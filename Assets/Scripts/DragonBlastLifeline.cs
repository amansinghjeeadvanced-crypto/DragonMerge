using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages the "Dragon Blast" lifeline:
/// - Starts with 1 use per game (×1).
/// - Tapping the button enters Blast Mode and visually highlights the button.
/// - Tapping an already-dropped board dragon triggers an explosion centered on that dragon.
/// - Removes the selected dragon and nearby dragons within 1.5–2 dragon widths radius.
/// - Dragons outside the blast radius remain untouched.
/// - Unity's existing 2D Rigidbody gravity naturally settles all remaining dragons into the empty space.
/// - Zero score directly awarded and no replacement dragons spawned.
/// - Invalid taps (empty space, walls, ground, UI, or aiming dragon) do nothing and do not consume the blast.
/// - Use count updates to ×0 and button disables.
/// - Start Game & Restart Game reset use count to ×1.
/// - Game Over cancels Blast Mode and disables interaction.
/// </summary>
public class DragonBlastLifeline : MonoBehaviour
{
    public static DragonBlastLifeline Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("The existing DragonBlastButton")]
    public Button blastButton;

    [Tooltip("Display text for remaining uses (×1, ×0)")]
    public TMP_Text countText;

    [Tooltip("Text label on the button (e.g. BLAST)")]
    public TMP_Text labelText;

    [Tooltip("Target graphic for color pulse highlights")]
    public Image buttonImage;

    [Header("Configuration")]
    [Tooltip("Maximum number of uses per game session")]
    public int maxUses = 1;

    [Tooltip("Blast radius multiplier relative to dragon radius (~1.75 dragon widths = 3.5x radius)")]
    public float blastRadiusMultiplier = 3.5f;

    [Tooltip("Duration of the explosion and dragon shrink animation in seconds")]
    public float blastAnimationDuration = 0.28f;

    // State
    public int remainingUses { get; private set; } = 1;
    public bool IsBlastModeActive { get; private set; } = false;

    private bool isAnimating = false;
    private Vector3 originalButtonScale = Vector3.one;
    private Color originalButtonColor = Color.white;
    private RectTransform buttonRect;
    private RectTransform boardRect;
    private int lastProcessedClickFrame = -1;

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
        if (blastButton == null)
        {
            blastButton = GetComponent<Button>();
            if (blastButton == null)
            {
                GameObject btnObj = GameObject.Find("DragonBlastButton");
                if (btnObj != null) blastButton = btnObj.GetComponent<Button>();
            }
        }

        if (blastButton != null)
        {
            buttonRect = blastButton.GetComponent<RectTransform>();
            originalButtonScale = buttonRect.localScale;

            if (buttonImage == null)
            {
                buttonImage = blastButton.GetComponent<Image>();
            }
            if (buttonImage != null)
            {
                originalButtonColor = buttonImage.color;
            }

            // Find countText in children
            if (countText == null)
            {
                Transform countT = blastButton.transform.Find("CountText");
                if (countT != null)
                {
                    countText = countT.GetComponent<TMP_Text>();
                }
                else
                {
                    // Fallback search for any child TMP_Text with count name
                    TMP_Text[] texts = blastButton.GetComponentsInChildren<TMP_Text>(true);
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
                Transform labelT = blastButton.transform.Find("Text (TMP)");
                if (labelT != null) labelText = labelT.GetComponent<TMP_Text>();
            }

            blastButton.onClick.RemoveListener(OnBlastButtonClicked);
            blastButton.onClick.AddListener(OnBlastButtonClicked);
        }

        // Find board RectTransform
        GameObject boardObj = GameObject.Find("Board");
        if (boardObj != null)
        {
            boardRect = boardObj.GetComponent<RectTransform>();
        }

        remainingUses = maxUses;
        UpdateUI();
    }

    void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        // 1. Visually highlight button when Blast Mode is active
        if (IsBlastModeActive && buttonRect != null)
        {
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f);
            buttonRect.localScale = originalButtonScale * pulse;

            if (buttonImage != null)
            {
                float colorLerp = (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f;
                // Fiery orange/red glow for Dragon Blast
                buttonImage.color = Color.Lerp(originalButtonColor, new Color(1f, 0.65f, 0.2f, 1f), colorLerp * 0.85f);
            }
        }

        // 2. Poll pointer press from the New Input System as backup tap detector
        if (IsBlastModeActive && !isAnimating && remainingUses > 0)
        {
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                Vector2 screenPos = Pointer.current.position.ReadValue();
                TrySelectDragonAtScreenPos(screenPos);
            }
        }
    }

    /// <summary>
    /// Called when the player presses the existing DragonBlastButton.
    /// Toggles Blast Mode on / off.
    /// </summary>
    public void OnBlastButtonClicked()
    {
        if (remainingUses <= 0 || isAnimating) return;

        // Do not allow while game over panel is shown
        if (GameManager.Instance != null && GameManager.Instance.gameOverPanel != null && GameManager.Instance.gameOverPanel.activeInHierarchy)
        {
            return;
        }

        SetBlastMode(!IsBlastModeActive);
    }

    /// <summary>
    /// Activates or deactivates Blast Mode and updates visual cues.
    /// </summary>
    public void SetBlastMode(bool active)
    {
        if (active && remainingUses <= 0)
        {
            active = false;
        }

        // If activating, cancel Remove Mode if it was active
        if (active && RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
        {
            RemoveDragonLifeline.Instance.SetRemoveMode(false);
        }

        IsBlastModeActive = active;

        // Reset button visual highlight if deactivating
        if (!active)
        {
            if (buttonRect != null)
            {
                buttonRect.localScale = originalButtonScale;
            }
            if (buttonImage != null)
            {
                buttonImage.color = originalButtonColor;
            }
        }

        // Protect the currently aiming dragon from raycast touches during Blast Mode
        ProtectAimingDragon(!active);
    }

    private void ProtectAimingDragon(bool raycastEnabled)
    {
        Dragon[] dragons = FindObjectsByType<Dragon>(FindObjectsInactive.Exclude);
        foreach (Dragon d in dragons)
        {
            if (d != null && !d.isDropped)
            {
                Image img = d.GetComponent<Image>();
                if (img != null)
                {
                    img.raycastTarget = raycastEnabled;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to select an already-dropped board dragon at screenPos and execute the blast.
    /// Returns true if a valid dragon was targeted; false if tap was invalid.
    /// </summary>
    public bool TrySelectDragonAtScreenPos(Vector2 screenPos)
    {
        if (!IsBlastModeActive || isAnimating || remainingUses <= 0) return false;

        // Avoid double processing multiple pointer events within the same frame
        if (Time.frameCount == lastProcessedClickFrame) return false;

        // 1. If clicked on DragonBlastButton itself, let the button click handler toggle it
        if (blastButton != null && buttonRect != null)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPos, null))
            {
                return false;
            }
        }

        // 2. If clicked on other UI buttons (like Pause, Restart, Remove, etc.), do not consume
        if (EventSystem.current != null)
        {
            PointerEventData ped = new PointerEventData(EventSystem.current) { position = screenPos };
            List<RaycastResult> hitList = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hitList);

            foreach (var hit in hitList)
            {
                if (hit.gameObject != null)
                {
                    Button hitBtn = hit.gameObject.GetComponentInParent<Button>();
                    if (hitBtn != null && hitBtn != blastButton)
                    {
                        return false;
                    }
                }
            }
        }

        // 3. Search all existing dragons for an already-dropped board dragon at screenPos
        Dragon[] dragons = FindObjectsByType<Dragon>(FindObjectsInactive.Exclude);
        Dragon closestDragon = null;
        float closestDist = float.MaxValue;

        foreach (Dragon d in dragons)
        {
            if (d == null || !d.gameObject.activeInHierarchy) continue;

            // STRICT REQUIREMENT: Only already-existing board dragons (isDropped == true)
            // The currently aiming/held dragon (isDropped == false) is strictly ignored!
            if (!d.isDropped || d.isMerging) continue;

            Vector2 dragonPos = (Vector2)d.transform.position;
            float dist = Vector2.Distance(screenPos, dragonPos);
            float radius = d.GetColliderRadius() * d.transform.lossyScale.x;

            // 15% touch margin
            if (dist <= radius * 1.15f)
            {
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestDragon = d;
                }
            }
        }

        // 4. Invalid tap: empty space, ground, wall, non-dragon object
        if (closestDragon == null)
        {
            // Invalid taps must NOT consume the blast and do nothing
            return false;
        }

        // 5. Valid dragon selected! Calculate blast radius (~1.5–2 dragon widths = ~3.5x radius)
        lastProcessedClickFrame = Time.frameCount;
        Vector2 blastCenter = (Vector2)closestDragon.transform.position;
        float baseRadius = closestDragon.GetColliderRadius() * closestDragon.transform.lossyScale.x;
        float effectiveBlastRadius = Mathf.Max(260f, baseRadius * blastRadiusMultiplier);

        // Collect all dropped board dragons within the circular blast radius
        List<Dragon> affectedDragons = new List<Dragon>();
        affectedDragons.Add(closestDragon);

        foreach (Dragon d in dragons)
        {
            if (d == null || !d.gameObject.activeInHierarchy || d == closestDragon) continue;
            if (!d.isDropped || d.isMerging) continue;

            Vector2 dPos = (Vector2)d.transform.position;
            float dist = Vector2.Distance(blastCenter, dPos);
            float dRadius = d.GetColliderRadius() * d.transform.lossyScale.x;

            // Overlap within the circular explosion area
            if (dist <= effectiveBlastRadius + (dRadius * 0.3f))
            {
                affectedDragons.Add(d);
            }
        }

        StartCoroutine(ExecuteBlastRoutine(blastCenter, effectiveBlastRadius, affectedDragons));
        return true;
    }

    /// <summary>
    /// Executes the Dragon Blast explosion:
    /// - Plays blast sound once.
    /// - Displays bright expanding explosion flash and shockwave.
    /// - Immediately disables colliders on all affected dragons so other dragons naturally fall.
    /// - Plays pop animation for affected dragons then destroys them.
    /// - Zero score directly awarded; no replacement dragons spawned.
    /// </summary>
    private IEnumerator ExecuteBlastRoutine(Vector2 blastCenter, float blastRadius, List<Dragon> affectedDragons)
    {
        isAnimating = true;

        // 1. Play blast explosion sound ONCE via AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayDragonBlast();
        }

        // 2. Immediately disable colliders and physics simulation on ALL affected dragons
        // This is crucial: the void opens immediately, allowing remaining dragons to fall under Unity 2D gravity naturally!
        foreach (Dragon d in affectedDragons)
        {
            if (d == null) continue;
            CircleCollider2D col = d.GetComponent<CircleCollider2D>();
            if (col != null) col.enabled = false;
            Rigidbody2D rb = d.GetComponent<Rigidbody2D>();
            if (rb != null) rb.simulated = false;
        }

        // 3. Spawn Visual Explosion Shockwave and Fire Flash
        CreateBlastVisualEffect(blastCenter, blastRadius);

        // 4. Animate all affected dragons:
        //    Phase 1: Quick pop expansion with fiery explosion tint (~0.07s)
        //    Phase 2: Quick shrink to zero & fade-out (~0.21s)
        List<Transform> transforms = new List<Transform>();
        List<Image> images = new List<Image>();
        List<Vector3> baseScales = new List<Vector3>();
        List<Vector3> peakedScales = new List<Vector3>();
        List<Color> baseColors = new List<Color>();

        foreach (Dragon d in affectedDragons)
        {
            if (d != null)
            {
                transforms.Add(d.transform);
                Image img = d.GetComponent<Image>();
                images.Add(img);
                Vector3 scale = d.transform.localScale;
                baseScales.Add(scale);
                peakedScales.Add(scale * 1.25f);
                baseColors.Add((img != null) ? img.color : Color.white);
            }
        }

        float phase1Duration = 0.07f;
        float elapsed = 0f;
        while (elapsed < phase1Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase1Duration);
            for (int i = 0; i < transforms.Count; i++)
            {
                if (transforms[i] != null)
                {
                    transforms[i].localScale = Vector3.Lerp(baseScales[i], peakedScales[i], t);
                    if (images[i] != null)
                    {
                        images[i].color = Color.Lerp(baseColors[i], new Color(1f, 0.8f, 0.4f, 1f), t);
                    }
                }
            }
            yield return null;
        }

        float phase2Duration = Mathf.Max(0.12f, blastAnimationDuration - phase1Duration);
        elapsed = 0f;
        while (elapsed < phase2Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase2Duration);
            for (int i = 0; i < transforms.Count; i++)
            {
                if (transforms[i] != null)
                {
                    transforms[i].localScale = Vector3.Lerp(peakedScales[i], Vector3.zero, t * t);
                    if (images[i] != null)
                    {
                        images[i].color = new Color(1f, 0.6f, 0.2f, Mathf.Lerp(1f, 0f, t));
                    }
                }
            }
            yield return null;
        }

        // 5. Destroy ALL affected dragons
        // No score is awarded directly by the blast.
        // No replacement dragons are spawned.
        // The remaining dragons naturally fall, shift, and settle using existing Unity 2D gravity and physics!
        foreach (Dragon d in affectedDragons)
        {
            if (d != null)
            {
                Destroy(d.gameObject);
            }
        }

        // 6. Consume 1 use (becomes 0)
        remainingUses--;
        UpdateUI();

        // 7. Automatically exit Blast Mode
        SetBlastMode(false);

        isAnimating = false;
    }

    /// <summary>
    /// Spawns an explosion shockwave and central fiery flash at the blast center.
    /// </summary>
    private void CreateBlastVisualEffect(Vector2 worldPos, float blastRadius)
    {
        Transform parentTransform = (boardRect != null) ? boardRect : transform;

        // 1. Shockwave Expanding Ring
        GameObject ringObj = new GameObject("BlastShockwave");
        ringObj.transform.SetParent(parentTransform, false);
        ringObj.transform.position = worldPos;

        RectTransform ringRt = ringObj.AddComponent<RectTransform>();
        ringRt.sizeDelta = new Vector2(blastRadius * 2.2f, blastRadius * 2.2f);

        Image ringImg = ringObj.AddComponent<Image>();
        ringImg.color = new Color(1f, 0.75f, 0.25f, 0.9f);
        ringImg.raycastTarget = false;

        StartCoroutine(AnimateShockwave(ringObj, ringRt, ringImg, 0.28f));

        // 2. Central Bright Fire Flash
        GameObject flashObj = new GameObject("BlastFireFlash");
        flashObj.transform.SetParent(parentTransform, false);
        flashObj.transform.position = worldPos;

        RectTransform flashRt = flashObj.AddComponent<RectTransform>();
        flashRt.sizeDelta = new Vector2(blastRadius * 1.2f, blastRadius * 1.2f);

        Image flashImg = flashObj.AddComponent<Image>();
        flashImg.color = new Color(1f, 0.95f, 0.8f, 1f);
        flashImg.raycastTarget = false;

        StartCoroutine(AnimateFireFlash(flashObj, flashRt, flashImg, 0.20f));
    }

    private IEnumerator AnimateShockwave(GameObject obj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 0.25f;
        Vector3 targetScale = Vector3.one * 1.4f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (rt != null) rt.localScale = Vector3.Lerp(initialScale, targetScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            if (img != null) img.color = new Color(1f, 0.6f + 0.3f * (1f - t), 0.2f, Mathf.Lerp(0.9f, 0f, t * t));
            yield return null;
        }

        if (obj != null) Destroy(obj);
    }

    private IEnumerator AnimateFireFlash(GameObject obj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 0.6f;
        Vector3 targetScale = Vector3.one * 1.2f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (rt != null) rt.localScale = Vector3.Lerp(initialScale, targetScale, t);
            if (img != null) img.color = new Color(1f, 0.95f, 0.7f, Mathf.Lerp(1f, 0f, t));
            yield return null;
        }

        if (obj != null) Destroy(obj);
    }

    /// <summary>
    /// Resets the lifeline to full uses (×1) and re-enables interaction.
    /// Called when Start Game or Restart Game is pressed.
    /// </summary>
    public void ResetUses()
    {
        remainingUses = maxUses;
        SetBlastMode(false);
        UpdateUI();
    }

    /// <summary>
    /// Cancels Blast Mode and disables interaction on Game Over.
    /// </summary>
    public void OnGameOver()
    {
        SetBlastMode(false);
        if (blastButton != null)
        {
            blastButton.interactable = false;
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

        if (blastButton != null)
        {
            blastButton.interactable = (remainingUses > 0);
        }

        if (remainingUses <= 0 && IsBlastModeActive)
        {
            SetBlastMode(false);
        }
    }
}

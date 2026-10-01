using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages the "Remove Dragon" lifeline:
/// - Starts with 2 uses (×2).
/// - Tapping the button enters Remove Mode and visually highlights the button.
/// - Tapping an already-dropped board dragon removes it with a pop effect and sound.
/// - Empty space remains and Unity's 2D physics naturally settles other dragons into the void.
/// - Zero score awarded and no replacement dragon spawned.
/// - Invalid taps (empty space, walls, ground, UI, or aiming dragon) do nothing and do not consume a use.
/// - Count updates: ×2 -> ×1 -> ×0. Button disables at ×0.
/// - Start Game & Restart Game reset uses to ×2.
/// - Game Over cancels Remove Mode and disables interaction.
/// </summary>
public class RemoveDragonLifeline : MonoBehaviour
{
    public static RemoveDragonLifeline Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("The existing RemoveDragonButton")]
    public Button removeButton;

    [Tooltip("Display text for remaining uses (×2, ×1, ×0)")]
    public TMP_Text countText;

    [Tooltip("Text label on the button (e.g. REMOVE)")]
    public TMP_Text labelText;

    [Tooltip("Target graphic for color pulse highlights")]
    public Image buttonImage;

    [Header("Configuration")]
    [Tooltip("Maximum number of uses per game session")]
    public int maxUses = 2;

    [Tooltip("Duration of the remove pop animation in seconds")]
    public float popAnimationDuration = 0.26f;

    // State
    public int remainingUses { get; private set; } = 2;
    public bool IsRemoveModeActive { get; private set; } = false;

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
        if (removeButton == null)
        {
            removeButton = GetComponent<Button>();
            if (removeButton == null)
            {
                GameObject btnObj = GameObject.Find("RemoveDragonButton");
                if (btnObj != null) removeButton = btnObj.GetComponent<Button>();
            }
        }

        if (removeButton != null)
        {
            buttonRect = removeButton.GetComponent<RectTransform>();
            originalButtonScale = buttonRect.localScale;

            if (buttonImage == null)
            {
                buttonImage = removeButton.GetComponent<Image>();
            }
            if (buttonImage != null)
            {
                originalButtonColor = buttonImage.color;
            }

            // Find countText in children
            if (countText == null)
            {
                Transform countT = removeButton.transform.Find("CountText");
                if (countT != null)
                {
                    countText = countT.GetComponent<TMP_Text>();
                }
                else
                {
                    // Fallback search for any child TMP_Text
                    TMP_Text[] texts = removeButton.GetComponentsInChildren<TMP_Text>(true);
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
                Transform labelT = removeButton.transform.Find("Text (TMP)");
                if (labelT != null) labelText = labelT.GetComponent<TMP_Text>();
            }

            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
            removeButton.onClick.AddListener(OnRemoveButtonClicked);
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
        // 1. Visually highlight button when Remove Mode is active
        if (IsRemoveModeActive && buttonRect != null)
        {
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f);
            buttonRect.localScale = originalButtonScale * pulse;

            if (buttonImage != null)
            {
                float colorLerp = (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f;
                buttonImage.color = Color.Lerp(originalButtonColor, new Color(1f, 0.95f, 0.4f, 1f), colorLerp * 0.8f);
            }
        }

        // 2. Poll pointer press from the New Input System as a backup tap detector
        if (IsRemoveModeActive && !isAnimating && remainingUses > 0)
        {
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                Vector2 screenPos = Pointer.current.position.ReadValue();
                TrySelectDragonAtScreenPos(screenPos);
            }
        }
    }

    /// <summary>
    /// Called when the player presses the existing RemoveDragonButton.
    /// Toggles Remove Mode on / off.
    /// </summary>
    public void OnRemoveButtonClicked()
    {
        if (remainingUses <= 0 || isAnimating) return;

        // Do not allow while game over panel is shown
        if (GameManager.Instance != null && GameManager.Instance.gameOverPanel != null && GameManager.Instance.gameOverPanel.activeInHierarchy)
        {
            return;
        }

        SetRemoveMode(!IsRemoveModeActive);
    }

    /// <summary>
    /// Activates or deactivates Remove Mode and updates visual cues.
    /// </summary>
    public void SetRemoveMode(bool active)
    {
        if (active && remainingUses <= 0)
        {
            active = false;
        }

        // If activating, cancel Blast Mode if it was active
        if (active && DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
        {
            DragonBlastLifeline.Instance.SetBlastMode(false);
        }

        IsRemoveModeActive = active;

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

        // Protect the currently aiming dragon from raycast touches during Remove Mode
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
    /// Attempts to select and remove an already-dropped board dragon at the given screen position.
    /// Returns true if a valid dragon was selected and destroyed; false if tap was invalid.
    /// </summary>
    public bool TrySelectDragonAtScreenPos(Vector2 screenPos)
    {
        if (!IsRemoveModeActive || isAnimating || remainingUses <= 0) return false;

        // Avoid double processing multiple pointer events within the same frame
        if (Time.frameCount == lastProcessedClickFrame) return false;

        // 1. If clicked on RemoveDragonButton itself, let the button click handler toggle it
        if (removeButton != null && buttonRect != null)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPos, null))
            {
                return false;
            }
        }

        // 2. If clicked on other UI buttons (like Pause, Restart, other lifelines), do not consume
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
                    if (hitBtn != null && hitBtn != removeButton)
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

            // Give a generous 15% touch margin for touch-screen precision
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
            // Invalid taps must NOT consume a use and do nothing
            return false;
        }

        // 5. Valid dragon selected!
        lastProcessedClickFrame = Time.frameCount;
        StartCoroutine(RemoveDragonRoutine(closestDragon));
        return true;
    }

    /// <summary>
    /// Executes the polished pop animation, sound effect, and destruction of the selected dragon.
    /// Unity's 2D physics naturally settles all remaining dragons into the empty space.
    /// </summary>
    private IEnumerator RemoveDragonRoutine(Dragon targetDragon)
    {
        isAnimating = true;

        // 1. Play pop sound via AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayRemoveDragon();
        }

        // 2. Immediately disable collider and physics simulation on the target dragon
        // This instantly allows other dragons touching it to naturally fall under Unity's 2D gravity!
        CircleCollider2D col = targetDragon.GetComponent<CircleCollider2D>();
        if (col != null) col.enabled = false;

        Rigidbody2D rb = targetDragon.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.simulated = false;
        }

        // 3. Visual Pop Burst Effect
        float dragonRadius = targetDragon.GetColliderRadius() * Mathf.Abs(targetDragon.transform.lossyScale.x);
        CreatePopEffect(targetDragon.transform.position, dragonRadius);

        // 4. Animate the dragon:
        //    Phase 1: Slight scale-up (~0.08s)
        //    Phase 2: Quick shrink & fade-out (~0.18s)
        Transform dt = targetDragon.transform;
        Image dragonImg = targetDragon.GetComponent<Image>();
        Vector3 baseScale = dt.localScale;
        Vector3 peakedScale = baseScale * 1.25f;
        Color origImgColor = (dragonImg != null) ? dragonImg.color : Color.white;

        float phase1Duration = 0.08f;
        float elapsed = 0f;
        while (elapsed < phase1Duration && dt != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase1Duration);
            dt.localScale = Vector3.Lerp(baseScale, peakedScale, t);
            yield return null;
        }

        float phase2Duration = Mathf.Max(0.1f, popAnimationDuration - phase1Duration);
        elapsed = 0f;
        while (elapsed < phase2Duration && dt != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / phase2Duration);
            dt.localScale = Vector3.Lerp(peakedScale, Vector3.zero, t * t);
            if (dragonImg != null)
            {
                dragonImg.color = new Color(origImgColor.r, origImgColor.g, origImgColor.b, Mathf.Lerp(origImgColor.a, 0f, t));
            }
            yield return null;
        }

        // 5. Destroy ONLY the selected dragon.
        // No score is awarded.
        // No replacement dragon is spawned.
        // The empty space remains and Unity's 2D Rigidbody gravity naturally pulls other dragons down.
        if (targetDragon != null)
        {
            Destroy(targetDragon.gameObject);
        }

        // 6. Consume 1 use
        remainingUses--;
        UpdateUI();

        // 7. Exit Remove Mode
        SetRemoveMode(false);

        isAnimating = false;
    }

    /// <summary>
    /// Spawns a glowing expanding burst ring at the dragon's position for mobile-game juice.
    /// </summary>
    private void CreatePopEffect(Vector3 worldPos, float radius)
    {
        Transform parentTransform = (boardRect != null) ? boardRect : transform;
        GameObject popObj = new GameObject("RemovePopEffect");
        popObj.transform.SetParent(parentTransform, false);
        popObj.transform.position = worldPos;

        RectTransform rt = popObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(radius * 2.2f, radius * 2.2f);

        Image img = popObj.AddComponent<Image>();
        img.color = new Color(1f, 0.96f, 0.6f, 0.85f);
        img.raycastTarget = false;

        StartCoroutine(AnimatePopFlash(popObj, rt, img, 0.22f));
    }

    private IEnumerator AnimatePopFlash(GameObject popObj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 0.4f;
        Vector3 targetScale = Vector3.one * 1.55f;
        Color initialColor = (img != null) ? img.color : Color.white;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (rt != null) rt.localScale = Vector3.Lerp(initialScale, targetScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            if (img != null) img.color = new Color(initialColor.r, initialColor.g, initialColor.b, Mathf.Lerp(initialColor.a, 0f, t * t));
            yield return null;
        }

        if (popObj != null)
        {
            Destroy(popObj);
        }
    }

    /// <summary>
    /// Resets the lifeline to full uses (×2) and re-enables interaction.
    /// Called when Start Game or Restart Game is pressed.
    /// </summary>
    public void ResetUses()
    {
        remainingUses = maxUses;
        SetRemoveMode(false);
        UpdateUI();
    }

    /// <summary>
    /// Cancels Remove Mode and disables interaction on Game Over.
    /// </summary>
    public void OnGameOver()
    {
        SetRemoveMode(false);
        if (removeButton != null)
        {
            removeButton.interactable = false;
        }
    }

    /// <summary>
    /// Updates the button count text (×2, ×1, ×0) and interactable state.
    /// </summary>
    public void UpdateUI()
    {
        if (countText != null)
        {
            countText.text = $"×{remainingUses}";
        }

        if (removeButton != null)
        {
            removeButton.interactable = (remainingUses > 0);
        }

        if (remainingUses <= 0 && IsRemoveModeActive)
        {
            SetRemoveMode(false);
        }
    }
}

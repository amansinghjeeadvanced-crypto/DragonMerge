using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages the "Magnet" lifeline:
/// - Starts with 1 use per game (×1).
/// - Clicking MagnetButton enters Magnet Mode and visually highlights the button.
/// - Tapping an already-dropped board dragon makes it the magnet center.
/// - Finds nearby dropped dragons within ~2–3 dragon widths radius.
/// - Gently attracts nearby dragons toward the magnet center using existing Rigidbody2D forces.
/// - Does NOT affect the currently held/aiming dragon at the top.
/// - Does NOT teleport dragons or permanently alter physics settings.
/// - If matching dragons collide as a result, normal merge system handles the merge.
/// - Zero score directly awarded by the magnet.
/// - Use count updates to ×0 and button disables.
/// - Start Game & Restart Game reset use count to ×1.
/// - Game Over cancels Magnet Mode and disables interaction.
/// </summary>
public class MagnetLifeline : MonoBehaviour
{
    public static MagnetLifeline Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("The existing MagnetButton")]
    public Button magnetButton;

    [Tooltip("Display text for remaining uses (×1, ×0)")]
    public TMP_Text countText;

    [Tooltip("Text label on the button (e.g. MAGNET)")]
    public TMP_Text labelText;

    [Tooltip("Target graphic for color pulse highlights")]
    public Image buttonImage;

    [Header("Configuration")]
    [Tooltip("Maximum number of uses per game session")]
    public int maxUses = 1;

    [Tooltip("Radius multiplier relative to dragon radius (~2.5 dragon widths = 5.0x radius)")]
    public float magnetRadiusMultiplier = 5.0f;

    [Tooltip("Duration in seconds the magnetic attraction force pulls nearby dragons")]
    public float attractionDuration = 0.85f;

    [Tooltip("Attraction force applied to Rigidbody2D")]
    public float pullForce = 650f;

    // State
    public int remainingUses { get; private set; } = 1;
    public bool IsMagnetModeActive { get; private set; } = false;

    private bool isAttracting = false;
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
        if (magnetButton == null)
        {
            magnetButton = GetComponent<Button>();
            if (magnetButton == null)
            {
                GameObject btnObj = GameObject.Find("MagnetButton");
                if (btnObj != null) magnetButton = btnObj.GetComponent<Button>();
            }
        }

        if (magnetButton != null)
        {
            buttonRect = magnetButton.GetComponent<RectTransform>();
            originalButtonScale = buttonRect.localScale;

            if (buttonImage == null)
            {
                buttonImage = magnetButton.GetComponent<Image>();
            }
            if (buttonImage != null)
            {
                originalButtonColor = buttonImage.color;
            }

            // Find countText in children
            if (countText == null)
            {
                Transform countT = magnetButton.transform.Find("CountText");
                if (countT != null)
                {
                    countText = countT.GetComponent<TMP_Text>();
                }
                else
                {
                    TMP_Text[] texts = magnetButton.GetComponentsInChildren<TMP_Text>(true);
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
                Transform labelT = magnetButton.transform.Find("Text (TMP)");
                if (labelT != null) labelText = labelT.GetComponent<TMP_Text>();
            }

            magnetButton.onClick.RemoveListener(OnMagnetButtonClicked);
            magnetButton.onClick.AddListener(OnMagnetButtonClicked);
        }

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
        // 1. Visually highlight button when Magnet Mode is active
        if (IsMagnetModeActive && buttonRect != null)
        {
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f);
            buttonRect.localScale = originalButtonScale * pulse;

            if (buttonImage != null)
            {
                float colorLerp = (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f;
                // Electric blue / magnetic cyan glow
                buttonImage.color = Color.Lerp(originalButtonColor, new Color(0.35f, 0.85f, 1f, 1f), colorLerp * 0.85f);
            }
        }

        // 2. Poll pointer press from the New Input System as backup tap detector
        if (IsMagnetModeActive && !isAttracting && remainingUses > 0)
        {
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                Vector2 screenPos = Pointer.current.position.ReadValue();
                TrySelectDragonAtScreenPos(screenPos);
            }
        }
    }

    /// <summary>
    /// Called when the player presses the existing MagnetButton.
    /// Toggles Magnet Mode on / off.
    /// </summary>
    public void OnMagnetButtonClicked()
    {
        if (remainingUses <= 0 || isAttracting) return;

        // Do not allow while game over panel is shown
        if (GameManager.Instance != null && GameManager.Instance.gameOverPanel != null && GameManager.Instance.gameOverPanel.activeInHierarchy)
        {
            return;
        }

        SetMagnetMode(!IsMagnetModeActive);
    }

    /// <summary>
    /// Activates or deactivates Magnet Mode and updates visual cues.
    /// </summary>
    public void SetMagnetMode(bool active)
    {
        if (active && remainingUses <= 0)
        {
            active = false;
        }

        // If activating, cancel any other active modes
        if (active)
        {
            if (RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
            {
                RemoveDragonLifeline.Instance.SetRemoveMode(false);
            }
            if (DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
            {
                DragonBlastLifeline.Instance.SetBlastMode(false);
            }
        }

        IsMagnetModeActive = active;

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

        // Protect the currently aiming dragon from raycast touches during Magnet Mode
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
    /// Attempts to select an already-dropped board dragon at screenPos and execute the magnet pull.
    /// Returns true if a valid dragon was targeted; false if tap was invalid.
    /// </summary>
    public bool TrySelectDragonAtScreenPos(Vector2 screenPos)
    {
        if (!IsMagnetModeActive || isAttracting || remainingUses <= 0) return false;

        // Avoid double processing multiple pointer events within the same frame
        if (Time.frameCount == lastProcessedClickFrame) return false;

        // 1. If clicked on MagnetButton itself, let the button click handler toggle it
        if (magnetButton != null && buttonRect != null)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(buttonRect, screenPos, null))
            {
                return false;
            }
        }

        // 2. If clicked on other UI buttons, do not consume
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
                    if (hitBtn != null && hitBtn != magnetButton)
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
            // Invalid taps must NOT consume the magnet and do nothing
            return false;
        }

        // 5. Valid dragon selected! Find nearby dropped dragons within ~2-3 dragon widths
        lastProcessedClickFrame = Time.frameCount;
        Vector2 magnetCenter = (Vector2)closestDragon.transform.position;
        float dragonWidth = closestDragon.GetColliderRadius() * 2f * closestDragon.transform.lossyScale.x;
        float effectiveRadius = Mathf.Max(480f * (Screen.width / 1080f), dragonWidth * 2.5f);

        Debug.Log($"[MagnetLifeline] Selected magnet dragon: {closestDragon.gameObject.name} (ID: {closestDragon.dragonId}) at {magnetCenter}");

        List<Dragon> nearbyDragons = new List<Dragon>();
        foreach (Dragon d in dragons)
        {
            if (d == null || !d.gameObject.activeInHierarchy || d == closestDragon) continue;
            if (!d.isDropped || d.isMerging) continue;

            Vector2 dPos = (Vector2)d.transform.position;
            float dist = Vector2.Distance(magnetCenter, dPos);
            float dRadius = d.GetColliderRadius() * d.transform.lossyScale.x;

            if (dist <= effectiveRadius + (dRadius * 0.5f))
            {
                nearbyDragons.Add(d);
            }
        }

        Debug.Log($"[MagnetLifeline] Number of nearby dragons found: {nearbyDragons.Count}");

        StartCoroutine(ExecuteMagnetRoutine(closestDragon, nearbyDragons, effectiveRadius));
        return true;
    }

    /// <summary>
    /// Executes the gentle physical magnetic pull:
    /// - Plays magnet sound once.
    /// - Displays magnetic aura & inward ripple rings.
    /// - Applies gentle attraction forces via existing Rigidbody2D physics.
    /// - Does NOT teleport dragons.
    /// - Existing merge system naturally handles collisions.
    /// </summary>
    private IEnumerator ExecuteMagnetRoutine(Dragon centerDragon, List<Dragon> nearbyDragons, float radius)
    {
        isAttracting = true;

        // 1. Play Magnet sound ONCE via AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayMagnet();
        }

        // 2. Visual feedback: magnetic aura & inward rings at center dragon
        if (centerDragon != null)
        {
            CreateMagnetVisualEffect(centerDragon, radius);
        }

        // 3. Smooth magnetic pull over attractionDuration
        float duration = Mathf.Max(1.0f, attractionDuration);
        float elapsed = 0f;
        bool loggedForce = false;

        while (elapsed < duration)
        {
            elapsed += Time.fixedDeltaTime;

            if (centerDragon == null || !centerDragon.gameObject.activeInHierarchy) break;

            Vector2 centerPos = (Vector2)centerDragon.transform.position;

            foreach (Dragon d in nearbyDragons)
            {
                if (d == null || !d.gameObject.activeInHierarchy || d.isMerging || !d.isDropped) continue;

                Rigidbody2D rb = d.GetComponent<Rigidbody2D>();
                if (rb != null && rb.simulated)
                {
                    Vector2 diff = centerPos - (Vector2)d.transform.position;
                    float dist = diff.magnitude;

                    if (dist > 10f)
                    {
                        Vector2 dir = diff.normalized;
                        // Effective attraction force scaled to overcome 2D friction (Physics2D.gravity = -2500f)
                        float forceMagnitude = rb.mass * Mathf.Max(pullForce, 4500f);
                        Vector2 pullVector = dir * forceMagnitude;

                        // Slight upward lift to reduce ground friction for smooth gliding
                        Vector2 liftVector = Vector2.up * (rb.mass * 1200f);

                        rb.AddForce(pullVector + liftVector, ForceMode2D.Force);

                        // Clamp max velocity so movement is smooth rather than violently launching
                        if (rb.linearVelocity.magnitude > 650f)
                        {
                            rb.linearVelocity = rb.linearVelocity.normalized * 650f;
                        }

                        if (!loggedForce)
                        {
                            Debug.Log($"[MagnetLifeline] Attraction force being applied: {forceMagnitude:F1}N to {d.gameObject.name} (mass={rb.mass}) toward center");
                        }
                    }
                }
            }

            loggedForce = true;
            yield return new WaitForFixedUpdate();
        }

        // 4. Consume 1 use (becomes 0)
        remainingUses--;
        UpdateUI();

        // 5. Automatically exit Magnet Mode
        SetMagnetMode(false);

        isAttracting = false;
    }

    /// <summary>
    /// Spawns a glowing magnetic aura ring around the selected dragon and inward contracting magnetic waves.
    /// </summary>
    private void CreateMagnetVisualEffect(Dragon centerDragon, float radius)
    {
        Transform parentTransform = (boardRect != null) ? boardRect : transform;

        // 1. Center Dragon Magnetic Aura Ring
        GameObject auraObj = new GameObject("MagnetAura");
        auraObj.transform.SetParent(centerDragon.transform, false);
        auraObj.transform.localPosition = Vector3.zero;

        RectTransform auraRt = auraObj.AddComponent<RectTransform>();
        float dRadius = centerDragon.GetColliderRadius();
        auraRt.sizeDelta = new Vector2(dRadius * 2.5f, dRadius * 2.5f);

        Image auraImg = auraObj.AddComponent<Image>();
        auraImg.color = new Color(0.35f, 0.85f, 1f, 0.7f);
        auraImg.raycastTarget = false;

        StartCoroutine(AnimateAura(auraObj, auraRt, auraImg, attractionDuration));

        // 2. Inward contracting magnetic ripple ring
        GameObject rippleObj = new GameObject("MagnetRipple");
        rippleObj.transform.SetParent(parentTransform, false);
        rippleObj.transform.position = centerDragon.transform.position;

        RectTransform rippleRt = rippleObj.AddComponent<RectTransform>();
        rippleRt.sizeDelta = new Vector2(radius * 2.1f, radius * 2.1f);

        Image rippleImg = rippleObj.AddComponent<Image>();
        rippleImg.color = new Color(0.4f, 0.85f, 1f, 0.5f);
        rippleImg.raycastTarget = false;

        StartCoroutine(AnimateInwardRipple(rippleObj, rippleRt, rippleImg, attractionDuration));
    }

    private IEnumerator AnimateAura(GameObject obj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float pulse = 1f + 0.15f * Mathf.Sin(elapsed * 18f);
            if (rt != null) rt.localScale = Vector3.one * pulse;
            if (img != null)
            {
                float alpha = (1f - (elapsed / duration)) * 0.7f;
                img.color = new Color(0.35f, 0.85f, 1f, alpha);
            }
            yield return null;
        }

        if (obj != null) Destroy(obj);
    }

    private IEnumerator AnimateInwardRipple(GameObject obj, RectTransform rt, Image img, float duration)
    {
        float elapsed = 0f;
        Vector3 initialScale = Vector3.one * 1.15f;
        Vector3 finalScale = Vector3.one * 0.25f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (rt != null)
            {
                rt.localScale = Vector3.Lerp(initialScale, finalScale, t);
            }
            if (img != null)
            {
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.55f;
                img.color = new Color(0.4f, 0.85f, 1f, alpha);
            }
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
        SetMagnetMode(false);
        UpdateUI();
    }

    /// <summary>
    /// Cancels Magnet Mode and disables interaction on Game Over.
    /// </summary>
    public void OnGameOver()
    {
        SetMagnetMode(false);
        if (magnetButton != null)
        {
            magnetButton.interactable = false;
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

        if (magnetButton != null)
        {
            magnetButton.interactable = (remainingUses > 0);
        }

        if (remainingUses <= 0 && IsMagnetModeActive)
        {
            SetMagnetMode(false);
        }
    }
}

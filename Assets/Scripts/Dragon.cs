using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class Dragon : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerDownHandler, IPointerUpHandler
{
    private static int nextDragonId = 0;
    public int dragonId { get; private set; }

    [Header("Physics Settings")]
    [Tooltip("Gravity scale multiplier for this dragon")]
    public float gravityScale = 1.0f;

    [Tooltip("Bounciness factor (0 = no bounce, 1 = super bouncy)")]
    [Range(0f, 1f)]
    public float bounciness = 0.2f;

    [Tooltip("Friction when rolling against other dragons or walls")]
    [Range(0f, 1f)]
    public float friction = 0.4f;

    [Tooltip("Linear damping to prevent endless sliding")]
    public float linearDamping = 0.2f;

    [Tooltip("Angular damping to prevent endless spinning")]
    public float angularDamping = 0.8f;

    [Tooltip("Fraction of the smaller rect dimension used for collider radius")]
    [Range(0.2f, 0.6f)]
    public float colliderRadiusScale = 0.45f;

    [Tooltip("Allow the dragon to roll and rotate physically")]
    public bool allowRotation = true;

    [Tooltip("Upward pop impulse applied to the evolved dragon when merging")]
    public float popForceOnMerge = 250f;

    [Header("References")]
    public RectTransform ground;
    public DragonSpawner spawner;

    // Component caches
    private Rigidbody2D rb;
    private CircleCollider2D col;
    private RectTransform dragonRect;
    private RectTransform boardRect;
    private UnityEngine.UI.Image dragonImage;
    private DragonLevel levelInfo;
    private PhysicsMaterial2D physicsMat;

    // State
    public bool isDropped { get; private set; } = false;
    public bool isMerging { get; private set; } = false;
    private bool isAiming = false;

    // Game over boundary tracking
    public bool canTriggerGameOver => isDropped && (hasCollidedOnce || timeSinceDrop > 1.2f);
    public bool hasCollidedOnce { get; private set; } = false;
    private float timeSinceDrop = 0f;

    // Clamping boundaries for aiming and container collision
    private float minAimX = -350f;
    private float maxAimX = 350f;
    private float leftWallBound = -420f;
    private float rightWallBound = 420f;

    public void SetWallBounds(float leftEdge, float rightEdge)
    {
        leftWallBound = leftEdge;
        rightWallBound = rightEdge;
        float radius = GetEffectiveRadius();
        minAimX = leftEdge + radius + 5f;
        maxAimX = rightEdge - radius - 5f;
    }

    void Awake()
    {
        dragonId = ++nextDragonId;
        dragonRect = GetComponent<RectTransform>();
        dragonImage = GetComponent<UnityEngine.UI.Image>();
        levelInfo = GetComponent<DragonLevel>();

        if (dragonRect.parent != null)
        {
            boardRect = dragonRect.parent.GetComponent<RectTransform>();
        }

        ApplyLevelScale();
        SetupPhysicsComponents();
    }

    [ContextMenu("Setup Physics")]
    public void SetupPhysicsComponents()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();

        if (col == null)
            col = GetComponent<CircleCollider2D>();
        if (col == null)
            col = gameObject.AddComponent<CircleCollider2D>();

        // Configure Rigidbody2D
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.gravityScale = gravityScale;
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;
        rb.freezeRotation = !allowRotation;

        // Mass scales with level so larger evolved dragons can push smaller ones
        if (levelInfo != null)
        {
            rb.mass = Mathf.Max(1f, 1f + (levelInfo.level * 0.5f));
        }

        // Configure CircleCollider2D
        float minDimension = Mathf.Min(dragonRect.rect.width, dragonRect.rect.height);
        if (minDimension > 5f)
        {
            col.radius = minDimension * colliderRadiusScale;
        }
        else
        {
            col.radius = 50f;
        }
        col.offset = Vector2.zero;

        // Create and assign physics material
        if (physicsMat == null)
        {
            physicsMat = new PhysicsMaterial2D("DragonPhysicsMat_" + gameObject.name)
            {
                bounciness = bounciness,
                friction = friction
            };
        }
        col.sharedMaterial = physicsMat;

        // Apply initial body type
        if (!isDropped)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.simulated = false;
        }
        else
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
        }
    }

    /// <summary>
    /// Universal base reference size for all dragons (75% of the original oversized dimensions,
    /// increased from the 65% base size: 185.848 × 75/65 = 214.44, 227.95695 × 75/65 = 263.02725).
    /// Level 0 (DragonEgg) at 1.00x uses this base size.
    /// Higher levels scale progressively from this universal reference.
    /// </summary>
    public static readonly Vector2 BaseDragonSize = new Vector2(214.44f, 263.02725f);

    /// <summary>
    /// Progressive dragon size scaling multipliers based on dragon level (0 to 8).
    /// Level 0 DragonEgg = 1.00×
    /// Level 1 Hatchling = 1.08×
    /// Level 2 Baby      = 1.16×
    /// Level 3 Young     = 1.25×
    /// Level 4 Adult     = 1.35×
    /// Level 5 Elder     = 1.48×
    /// Level 6 Mythical  = 1.62×
    /// Level 7 Legendary = 1.80×
    /// Level 8 God       = 2.00×
    /// </summary>
    public static float GetScaleForLevel(int level)
    {
        switch (level)
        {
            case 0: return 1.00f; // DragonEgg
            case 1: return 1.08f; // Hatchling
            case 2: return 1.16f; // Baby
            case 3: return 1.25f; // Young
            case 4: return 1.35f; // Adult
            case 5: return 1.48f; // Elder
            case 6: return 1.62f; // Mythical
            case 7: return 1.80f; // Legendary
            case 8: return 2.00f; // God
            default: return 1.00f;
        }
    }

    /// <summary>
    /// Applies progressive size scaling based on the dragon's level.
    /// Sets the base size to the 75% universal DragonEgg base reference,
    /// and scales both visual representation (RectTransform/Image) and physical CircleCollider2D.
    /// Applied once at initialization/spawn/merge to avoid per-frame overhead.
    /// </summary>
    public void ApplyLevelScale()
    {
        if (levelInfo == null)
            levelInfo = GetComponent<DragonLevel>();

        if (dragonRect == null)
            dragonRect = GetComponent<RectTransform>();

        // Set to the universal base reference (75% of original)
        if (dragonRect != null)
        {
            dragonRect.sizeDelta = BaseDragonSize;
        }

        int level = (levelInfo != null) ? levelInfo.level : 0;
        float scale = GetScaleForLevel(level);

        transform.localScale = new Vector3(scale, scale, 1f);

        // Ensure collider is correctly sized to match the dragon
        if (col == null)
            col = GetComponent<CircleCollider2D>();

        if (col != null && dragonRect != null)
        {
            float minDimension = Mathf.Min(dragonRect.rect.width, dragonRect.rect.height);
            col.radius = (minDimension > 5f) ? (minDimension * colliderRadiusScale) : 50f;
            col.offset = Vector2.zero;
        }
    }

    public float GetColliderRadius()
    {
        if (col != null) return col.radius;
        float minDimension = (dragonRect != null) ? Mathf.Min(dragonRect.rect.width, dragonRect.rect.height) : 214.44f;
        return (minDimension > 5f) ? (minDimension * colliderRadiusScale) : 50f;
    }

    /// <summary>
    /// Returns the effective physical world/container radius of this dragon including its level scale.
    /// </summary>
    public float GetEffectiveRadius()
    {
        return GetColliderRadius() * Mathf.Abs(transform.localScale.x);
    }

    /// <summary>
    /// Prepares this dragon to be held at the top for player aiming, clamped within the left and right walls.
    /// </summary>
    public void PrepareForAiming(DragonSpawner spawnerRef, float leftEdge, float rightEdge)
    {
        spawner = spawnerRef;
        isDropped = false;
        isMerging = false;
        isAiming = false;
        hasCollidedOnce = false;
        timeSinceDrop = 0f;

        ApplyLevelScale();
        SetWallBounds(leftEdge, rightEdge);

        if (rb == null)
            SetupPhysicsComponents();

        // Calculate aim boundaries so dragon stops right at the inner faces of the container walls
        float radius = GetEffectiveRadius();
        minAimX = leftEdge + radius + 5f;
        maxAimX = rightEdge - radius - 5f;

        // Ensure maxAimX is >= minAimX
        if (maxAimX < minAimX)
        {
            float mid = (leftEdge + rightEdge) * 0.5f;
            minAimX = mid - 40f;
            maxAimX = mid + 40f;
        }

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.simulated = false;

        if (dragonImage != null)
            dragonImage.raycastTarget = true;
    }

    /// <summary>
    /// Configures the newly merged dragon to immediately simulate physics.
    /// </summary>
    public void SetAlreadyDropped()
    {
        isDropped = true;
        isMerging = false;
        isAiming = false;
        hasCollidedOnce = true;
        timeSinceDrop = 2f;

        ApplyLevelScale();

        if (rb == null)
            SetupPhysicsComponents();

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;

        if (dragonImage != null)
            dragonImage.raycastTarget = false;
    }

    /// <summary>
    /// Updates the dragon's horizontal position while aiming, clamped inside boundaries.
    /// </summary>
    public void SetAimPositionX(float targetX)
    {
        if (isDropped) return;

        float clampedX = Mathf.Clamp(targetX, minAimX, maxAimX);
        dragonRect.anchoredPosition = new Vector2(clampedX, dragonRect.anchoredPosition.y);
    }

    /// <summary>
    /// Drops the dragon into the 2D physics simulation.
    /// </summary>
    public void Drop()
    {
        if (isDropped) return;

        isDropped = true;
        isAiming = false;

        // Switch to dynamic physics
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // Disable raycasts on this dropped dragon so player clicks/touches pass through to the board
        if (dragonImage != null)
            dragonImage.raycastTarget = false;

        // Notify the spawner to spawn the next dragon after cooldown
        if (spawner != null)
        {
            spawner.OnDragonDropped(this);
        }
    }

    #region Input Event Handlers
    public void OnPointerDown(PointerEventData eventData)
    {
        if (isDropped) return;
        if (IsPointerOverUIButton(eventData))
        {
            isAiming = false;
            return;
        }
        isAiming = true;
        UpdateAimFromPointer(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isDropped) return;
        if (IsPointerOverUIButton(eventData))
        {
            isAiming = false;
            return;
        }
        isAiming = true;
        UpdateAimFromPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (isDropped || !isAiming) return;
        UpdateAimFromPointer(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (isDropped || !isAiming) return;
        isAiming = false;
        Drop();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (isDropped || !isAiming) return;
        isAiming = false;
        Drop();
    }

    private static readonly List<RaycastResult> raycastResultsCache = new List<RaycastResult>();

    private bool IsPointerOverUIButton(PointerEventData eventData)
    {
        if (eventData == null) return false;

        // Check direct raycast target
        if (eventData.pointerPressRaycast.gameObject != null)
        {
            if (eventData.pointerPressRaycast.gameObject.GetComponentInParent<UnityEngine.UI.Button>() != null)
                return true;
        }

        if (eventData.pointerCurrentRaycast.gameObject != null)
        {
            if (eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<UnityEngine.UI.Button>() != null)
                return true;
        }

        // Raycast against all UI elements at this screen/touch position
        if (EventSystem.current != null)
        {
            raycastResultsCache.Clear();
            EventSystem.current.RaycastAll(eventData, raycastResultsCache);
            for (int i = 0; i < raycastResultsCache.Count; i++)
            {
                GameObject go = raycastResultsCache[i].gameObject;
                if (go != null && go != gameObject && !go.transform.IsChildOf(transform))
                {
                    if (go.GetComponentInParent<UnityEngine.UI.Button>() != null)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void UpdateAimFromPointer(PointerEventData eventData)
    {
        if (boardRect == null && dragonRect.parent != null)
            boardRect = dragonRect.parent.GetComponent<RectTransform>();

        if (boardRect != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                boardRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
            {
                SetAimPositionX(localPoint.x);
            }
        }
        else
        {
            SetAimPositionX(dragonRect.anchoredPosition.x + eventData.delta.x);
        }
    }
    #endregion

    #region Physics Collision and Merging
    private void OnCollisionEnter2D(Collision2D collision)
    {
        hasCollidedOnce = true;

        if (isMerging) return;

        Dragon other = collision.gameObject.GetComponent<Dragon>();
        if (other != null && !other.isMerging)
        {
            if (levelInfo == null)
                levelInfo = GetComponent<DragonLevel>();

            DragonLevel otherLevel = other.GetComponent<DragonLevel>();
            if (levelInfo != null && otherLevel != null && levelInfo.level == otherLevel.level && levelInfo.nextDragon != null)
            {
                // Concurrency safeguard: ensure exactly ONE of the two colliding dragons triggers the merge
                if (this.dragonId > other.dragonId) return;

                isMerging = true;
                other.isMerging = true;

                // Play satisfying merge sound
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayMerge();
                }

                // Calculate midpoint between the two colliding dragons
                Vector3 mergePos = (transform.position + other.transform.position) * 0.5f;

                Transform parentTransform = (boardRect != null) ? boardRect : transform.parent;
                GameObject evolvedDragon = Instantiate(levelInfo.nextDragon, parentTransform);
                evolvedDragon.transform.position = mergePos;

                Dragon evolvedDragonScript = evolvedDragon.GetComponent<Dragon>();
                if (evolvedDragonScript != null)
                {
                    evolvedDragonScript.ground = ground;
                    evolvedDragonScript.spawner = spawner;
                    evolvedDragonScript.ApplyLevelScale();
                    evolvedDragonScript.SetWallBounds(leftWallBound, rightWallBound);
                    evolvedDragonScript.SetAlreadyDropped();

                    // Give the new dragon a slight upward pop impulse for satisfying merge feedback
                    Rigidbody2D evolvedRb = evolvedDragon.GetComponent<Rigidbody2D>();
                    if (evolvedRb != null)
                    {
                        evolvedRb.AddForce(Vector2.up * popForceOnMerge, ForceMode2D.Impulse);
                    }
                }

                // Add score if GameManager is present
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.AddScore((levelInfo.level + 1) * 10, mergePos);
                }

                // Clean up merged dragons to finish the merge
                Destroy(other.gameObject);
                Destroy(gameObject);
                return;
            }
        }
    }
    #endregion

    void FixedUpdate()
    {
        if (isDropped)
        {
            timeSinceDrop += Time.fixedDeltaTime;

            // Safety container wall bounds check: keep dragon strictly inside the playable container
            if (rb != null)
            {
                float leftBound = (spawner != null) ? spawner.cachedLeftEdge : leftWallBound;
                float rightBound = (spawner != null) ? spawner.cachedRightEdge : rightWallBound;
                float radius = GetEffectiveRadius();

                if (dragonRect.anchoredPosition.x - radius < leftBound)
                {
                    dragonRect.anchoredPosition = new Vector2(leftBound + radius + 2f, dragonRect.anchoredPosition.y);
                    if (rb.linearVelocity.x < 0f)
                    {
                        rb.linearVelocity = new Vector2(Mathf.Abs(rb.linearVelocity.x) * 0.4f + 30f, rb.linearVelocity.y);
                    }
                }
                else if (dragonRect.anchoredPosition.x + radius > rightBound)
                {
                    dragonRect.anchoredPosition = new Vector2(rightBound - radius - 2f, dragonRect.anchoredPosition.y);
                    if (rb.linearVelocity.x > 0f)
                    {
                        rb.linearVelocity = new Vector2(-Mathf.Abs(rb.linearVelocity.x) * 0.4f - 30f, rb.linearVelocity.y);
                    }
                }

                // Safety bottom check: destroy only if far below ground
                if (ground != null && dragonRect.anchoredPosition.y < ground.anchoredPosition.y - 400f)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class DragonSpawner : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Dragon Prefabs")]
    [Tooltip("Pool of dragon prefabs that can spawn at the top")]
    public GameObject[] dragonPrefabs;

    [Header("Scene References")]
    public RectTransform ground;

    [Header("Timing")]
    [Tooltip("Delay in seconds before the next dragon spawns after dropping")]
    public float dropCooldown = 0.6f;

    [Header("Playable Container Walls")]
    [Tooltip("Half-width of the playable container between left and right walls (from center)")]
    public float containerHalfWidth = 420f;

    [Tooltip("Visible width / thickness of each wall pillar")]
    public float wallVisualWidth = 180f;

    [Tooltip("Collider depth / thickness behind the wall face to prevent tunneling")]
    public float wallColliderThickness = 300f;

    [Tooltip("Sprite for the visible left container wall (fantasy stone/wood pillar)")]
    public Sprite wallSpriteLeft;

    [Tooltip("Sprite for the visible right container wall (fantasy stone/wood pillar)")]
    public Sprite wallSpriteRight;

    [Header("Upper Boundary (Game Over Line)")]
    [Tooltip("Y position of the game over line relative to Board center (e.g. 800)")]
    public float upperBoundaryY = 800f;

    [Tooltip("Show visual red danger line across the board")]
    public bool showDangerLine = true;

    [Tooltip("Thickness / height of the visible danger line in pixels")]
    public float dangerLineThickness = 6f;

    [Tooltip("Color of the danger line")]
    public Color dangerLineColor = new Color(1f, 0.2f, 0.2f, 0.65f);

    private RectTransform spawnerRect;
    private RectTransform boardRect;
    private Dragon currentDragon;
    private bool isSpawning = false;
    private bool isAiming = false;

    // Boundaries of the playable container
    public float cachedLeftEdge { get; private set; } = -420f;
    public float cachedRightEdge { get; private set; } = 420f;

    void Awake()
    {
        spawnerRect = GetComponent<RectTransform>();
        if (transform.parent != null)
        {
            boardRect = transform.parent.GetComponent<RectTransform>();
        }

        // Safety fallback checks so zero-deserialized values are never used
        if (containerHalfWidth < 150f) containerHalfWidth = 420f;
        if (wallVisualWidth < 50f) wallVisualWidth = 180f;
        if (wallColliderThickness < 100f) wallColliderThickness = 300f;
        float defaultUpperY = (spawnerRect != null) ? (spawnerRect.anchoredPosition.y - 200f) : 800f;
        if (upperBoundaryY <= 0f) upperBoundaryY = defaultUpperY;

        // Configure global 2D physics properties for canvas pixel coordinates
        Physics2D.gravity = new Vector2(0f, -2500f);
        Physics2D.maxTranslationSpeed = 5000f;
        Physics2D.maxLinearCorrection = 25f;
        Physics2D.defaultContactOffset = 0.5f;
        Physics2D.bounceThreshold = 10f;
    }

    void Start()
    {
        SetupBoundaries();
        SetupBoardInputForwarder();
        SpawnDragon();
    }

    /// <summary>
    /// Updates playable dimensions responsive to the visible arena dimensions.
    /// Called by ResponsiveGameLayout or dynamically on screen resize.
    /// </summary>
    public void UpdateResponsiveDimensions(float halfWidth, float visualWallWidth, float upperY, float groundY, float spawnY)
    {
        if (spawnerRect == null) spawnerRect = GetComponent<RectTransform>();
        if (boardRect == null && transform.parent != null) boardRect = transform.parent.GetComponent<RectTransform>();

        bool changed = false;

        if (Mathf.Abs(containerHalfWidth - halfWidth) > 0.5f)
        {
            containerHalfWidth = halfWidth;
            changed = true;
        }

        if (Mathf.Abs(wallVisualWidth - visualWallWidth) > 0.5f)
        {
            wallVisualWidth = visualWallWidth;
            changed = true;
        }

        if (Mathf.Abs(upperBoundaryY - upperY) > 0.5f)
        {
            upperBoundaryY = upperY;
            changed = true;
        }

        if (ground != null && Mathf.Abs(ground.anchoredPosition.y - groundY) > 0.5f)
        {
            ground.anchoredPosition = new Vector2(0f, groundY);
            changed = true;
        }

        if (spawnerRect != null && Mathf.Abs(spawnerRect.anchoredPosition.y - spawnY) > 0.5f)
        {
            spawnerRect.anchoredPosition = new Vector2(0f, spawnY);
            changed = true;
        }

        if (changed)
        {
            SetupBoundaries();

            // Re-align current aiming dragon if one is held
            if (currentDragon != null && !currentDragon.isDropped)
            {
                currentDragon.PrepareForAiming(this, cachedLeftEdge, cachedRightEdge);
                currentDragon.SetAimPositionX(0f);
            }
        }
    }

    /// <summary>
    /// Builds two visible vertical fantasy stone/wood walls inside the game area forming
    /// the playable container, plus Ground floor collider and UpperBoundary deadline trigger.
    /// The phone screen bezel has no collision.
    /// </summary>
    [ContextMenu("Setup Boundaries")]
    public void SetupBoundaries()
    {
        if (spawnerRect == null) spawnerRect = GetComponent<RectTransform>();
        if (boardRect == null && transform.parent != null) boardRect = transform.parent.GetComponent<RectTransform>();
        if (ground == null || boardRect == null) return;

        // Force canvas update to ensure accurate rect sizes
        Canvas.ForceUpdateCanvases();

        if (containerHalfWidth < 150f) containerHalfWidth = 420f;
        if (wallVisualWidth < 50f) wallVisualWidth = 180f;
        if (wallColliderThickness < 100f) wallColliderThickness = 300f;

        // Calculate container inner boundaries
        cachedLeftEdge = -containerHalfWidth;
        cachedRightEdge = containerHalfWidth;
        float playWidth = cachedRightEdge - cachedLeftEdge;

        // Bouncy and low-friction physics material for side walls
        PhysicsMaterial2D wallMaterial = new PhysicsMaterial2D("BoardWallMaterial")
        {
            bounciness = 0.35f,
            friction = 0.2f
        };

        // 1. Setup Ground Collider strictly supporting the container floor
        BoxCollider2D groundCol = ground.GetComponent<BoxCollider2D>();
        if (groundCol == null)
            groundCol = ground.gameObject.AddComponent<BoxCollider2D>();

        groundCol.size = new Vector2(playWidth + (wallVisualWidth * 2f), ground.rect.height);
        groundCol.offset = Vector2.zero;
        groundCol.isTrigger = false;
        groundCol.sharedMaterial = wallMaterial;

        // Wall vertical span: from below ground floor to above the spawner and upper boundary
        float groundY = ground.anchoredPosition.y;
        float topY = Mathf.Max(upperBoundaryY + 350f, (spawnerRect != null) ? spawnerRect.anchoredPosition.y + 100f : 1100f);
        float wallHeight = topY - groundY + 80f;
        float wallCenterY = (topY + groundY - 80f) * 0.5f;

        // Load wall sprites if not already assigned
        EnsureWallSpritesLoaded();

        // 2. Setup Visible Solid Left Wall (stops dragons and never lets them pass through)
        Transform leftWallT = boardRect.Find("LeftWall");
        GameObject leftWallObj = (leftWallT != null) ? leftWallT.gameObject : new GameObject("LeftWall");
        leftWallObj.transform.SetParent(boardRect, false);

        RectTransform leftWallRect = leftWallObj.GetComponent<RectTransform>();
        if (leftWallRect == null) leftWallRect = leftWallObj.AddComponent<RectTransform>();
        leftWallRect.anchorMin = new Vector2(0.5f, 0.5f);
        leftWallRect.anchorMax = new Vector2(0.5f, 0.5f);
        leftWallRect.pivot = new Vector2(0.5f, 0.5f);
        leftWallRect.anchoredPosition = new Vector2(cachedLeftEdge - (wallVisualWidth * 0.5f), wallCenterY);
        leftWallRect.sizeDelta = new Vector2(wallVisualWidth, wallHeight);

        UnityEngine.UI.Image leftImage = leftWallObj.GetComponent<UnityEngine.UI.Image>();
        if (leftImage == null) leftImage = leftWallObj.AddComponent<UnityEngine.UI.Image>();
        if (wallSpriteLeft != null)
        {
            leftImage.sprite = wallSpriteLeft;
            leftImage.type = UnityEngine.UI.Image.Type.Sliced;
            leftImage.fillCenter = true;
        }
        leftImage.color = Color.white;
        leftImage.raycastTarget = false;

        BoxCollider2D leftCol = leftWallObj.GetComponent<BoxCollider2D>();
        if (leftCol == null) leftCol = leftWallObj.AddComponent<BoxCollider2D>();
        leftCol.isTrigger = false;
        leftCol.size = new Vector2(wallColliderThickness, wallHeight * 1.5f);
        leftCol.offset = new Vector2((wallVisualWidth - wallColliderThickness) * 0.5f, 0);
        leftCol.sharedMaterial = wallMaterial;

        // 3. Setup Visible Solid Right Wall (stops dragons and never lets them pass through)
        Transform rightWallT = boardRect.Find("RightWall");
        GameObject rightWallObj = (rightWallT != null) ? rightWallT.gameObject : new GameObject("RightWall");
        rightWallObj.transform.SetParent(boardRect, false);

        RectTransform rightWallRect = rightWallObj.GetComponent<RectTransform>();
        if (rightWallRect == null) rightWallRect = rightWallObj.AddComponent<RectTransform>();
        rightWallRect.anchorMin = new Vector2(0.5f, 0.5f);
        rightWallRect.anchorMax = new Vector2(0.5f, 0.5f);
        rightWallRect.pivot = new Vector2(0.5f, 0.5f);
        rightWallRect.anchoredPosition = new Vector2(cachedRightEdge + (wallVisualWidth * 0.5f), wallCenterY);
        rightWallRect.sizeDelta = new Vector2(wallVisualWidth, wallHeight);

        UnityEngine.UI.Image rightImage = rightWallObj.GetComponent<UnityEngine.UI.Image>();
        if (rightImage == null) rightImage = rightWallObj.AddComponent<UnityEngine.UI.Image>();
        if (wallSpriteRight != null)
        {
            rightImage.sprite = wallSpriteRight;
            rightImage.type = UnityEngine.UI.Image.Type.Sliced;
            rightImage.fillCenter = true;
        }
        rightImage.color = Color.white;
        rightImage.raycastTarget = false;

        BoxCollider2D rightCol = rightWallObj.GetComponent<BoxCollider2D>();
        if (rightCol == null) rightCol = rightWallObj.AddComponent<BoxCollider2D>();
        rightCol.isTrigger = false;
        rightCol.size = new Vector2(wallColliderThickness, wallHeight * 1.5f);
        rightCol.offset = new Vector2((wallColliderThickness - wallVisualWidth) * 0.5f, 0);
        rightCol.sharedMaterial = wallMaterial;

        // 4. Setup Upper Boundary (Game Over Danger Line) strictly spanning inside container
        float actualUpperY = (upperBoundaryY > 50f) ? upperBoundaryY : ((spawnerRect != null) ? spawnerRect.anchoredPosition.y - 200f : 800f);

        Transform upperT = boardRect.Find("UpperBoundary");
        GameObject upperObj = (upperT != null) ? upperT.gameObject : new GameObject("UpperBoundary");
        upperObj.transform.SetParent(boardRect, false);

        RectTransform upperRect = upperObj.GetComponent<RectTransform>();
        if (upperRect == null) upperRect = upperObj.AddComponent<RectTransform>();
        upperRect.anchorMin = new Vector2(0.5f, 0.5f);
        upperRect.anchorMax = new Vector2(0.5f, 0.5f);
        upperRect.pivot = new Vector2(0.5f, 0.5f);
        upperRect.anchoredPosition = new Vector2(0f, actualUpperY);
        upperRect.sizeDelta = new Vector2(playWidth, dangerLineThickness);

        // Visual danger line
        UnityEngine.UI.Image lineImage = upperObj.GetComponent<UnityEngine.UI.Image>();
        if (showDangerLine)
        {
            if (lineImage == null) lineImage = upperObj.AddComponent<UnityEngine.UI.Image>();
            lineImage.enabled = true;
            lineImage.color = dangerLineColor;
            lineImage.raycastTarget = false;
        }
        else if (lineImage != null)
        {
            lineImage.enabled = false;
        }

        // Trigger Collider strictly inside the play area between walls
        BoxCollider2D upperCol = upperObj.GetComponent<BoxCollider2D>();
        if (upperCol == null) upperCol = upperObj.AddComponent<BoxCollider2D>();
        upperCol.isTrigger = true;
        upperCol.size = new Vector2(playWidth, 24f);
        upperCol.offset = Vector2.zero;

        UpperBoundary upperScript = upperObj.GetComponent<UpperBoundary>();
        if (upperScript == null) upperScript = upperObj.AddComponent<UpperBoundary>();
        upperScript.ResetBoundary();

        // Bring visible walls to front so they cleanly frame the arena
        leftWallObj.transform.SetAsLastSibling();
        rightWallObj.transform.SetAsLastSibling();
    }

    private void EnsureWallSpritesLoaded()
    {
        if (wallSpriteLeft == null)
            wallSpriteLeft = Resources.Load<Sprite>("ArenaWall_Left");
        if (wallSpriteRight == null)
            wallSpriteRight = Resources.Load<Sprite>("ArenaWall_Right");

        if (wallSpriteLeft == null)
            wallSpriteLeft = LoadSpriteFromDisk("Assets/pictures/ArenaWall_Left.png", new Vector4(0, 220, 0, 200));
        if (wallSpriteRight == null)
            wallSpriteRight = LoadSpriteFromDisk("Assets/pictures/ArenaWall_Right.png", new Vector4(0, 220, 0, 200));
    }

    private Sprite LoadSpriteFromDisk(string assetPath, Vector4 border)
    {
        try
        {
            string fullPath = System.IO.Path.Combine(Application.dataPath, assetPath.StartsWith("Assets/") ? assetPath.Substring(7) : assetPath);
            if (System.IO.File.Exists(fullPath))
            {
                byte[] fileData = System.IO.File.ReadAllBytes(fullPath);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(fileData))
                {
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DragonSpawner] Could not load wall sprite from disk: {ex.Message}");
        }
        return null;
    }

    private void SetupBoardInputForwarder()
    {
        if (boardRect == null) return;

        BoardInputHandler inputHandler = boardRect.GetComponent<BoardInputHandler>();
        if (inputHandler == null)
            inputHandler = boardRect.gameObject.AddComponent<BoardInputHandler>();

        inputHandler.spawner = this;
    }

    /// <summary>
    /// Randomly selects a dragon prefab from the allowed spawn pool:
    /// Level 0 = DragonEgg
    /// Level 1 = Hatchling
    /// Level 2 = Baby
    /// Level 3 = Young
    /// Level 4 = Adult
    /// Level 5 = Elder
    /// Higher levels (Mythical, Legendary, God) must NEVER be randomly spawned.
    /// </summary>
    private GameObject GetRandomSpawnPrefab()
    {
        EnsureSpawnPoolPrefabs();

        if (dragonPrefabs == null || dragonPrefabs.Length == 0) return null;

        var validList = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < dragonPrefabs.Length; i++)
        {
            GameObject p = dragonPrefabs[i];
            if (p == null) continue;

            string pName = p.name;
            // Strictly exclude Levels 6-8 (Mythical, Legendary, God)
            if (pName.IndexOf("Mythical", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                pName.IndexOf("Legendary", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                pName.IndexOf("God", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            DragonLevel dl = p.GetComponent<DragonLevel>();
            // Allow Levels 0 through 5 (DragonEgg, Hatchling, Baby, Young, Adult, Elder)
            if (dl != null && dl.level > 5)
            {
                continue;
            }

            validList.Add(p);
        }

        if (validList.Count == 0) return dragonPrefabs[0];
        return validList[Random.Range(0, validList.Count)];
    }

    private void EnsureSpawnPoolPrefabs()
    {
        if (dragonPrefabs == null) return;

        bool hasAdult = false;
        bool hasElder = false;
        for (int i = 0; i < dragonPrefabs.Length; i++)
        {
            if (dragonPrefabs[i] == null) continue;
            string n = dragonPrefabs[i].name;
            if (n.IndexOf("Adult", System.StringComparison.OrdinalIgnoreCase) >= 0) hasAdult = true;
            if (n.IndexOf("Elder", System.StringComparison.OrdinalIgnoreCase) >= 0) hasElder = true;
        }

        // Add Adult from Young's nextDragon if missing
        if (!hasAdult)
        {
            for (int i = 0; i < dragonPrefabs.Length; i++)
            {
                if (dragonPrefabs[i] != null && dragonPrefabs[i].name.IndexOf("Young", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    DragonLevel dl = dragonPrefabs[i].GetComponent<DragonLevel>();
                    if (dl != null && dl.nextDragon != null)
                    {
                        System.Array.Resize(ref dragonPrefabs, dragonPrefabs.Length + 1);
                        dragonPrefabs[dragonPrefabs.Length - 1] = dl.nextDragon;
                        hasAdult = true;
                        break;
                    }
                }
            }
        }

        // Add Elder from Adult's nextDragon if missing
        if (!hasElder)
        {
            for (int i = 0; i < dragonPrefabs.Length; i++)
            {
                if (dragonPrefabs[i] != null && dragonPrefabs[i].name.IndexOf("Adult", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    DragonLevel dl = dragonPrefabs[i].GetComponent<DragonLevel>();
                    if (dl != null && dl.nextDragon != null)
                    {
                        System.Array.Resize(ref dragonPrefabs, dragonPrefabs.Length + 1);
                        dragonPrefabs[dragonPrefabs.Length - 1] = dl.nextDragon;
                        hasElder = true;
                        break;
                    }
                }
            }
        }
    }

    public void SpawnDragon()
    {
        if (dragonPrefabs == null || dragonPrefabs.Length == 0) return;
        if (isSpawning || currentDragon != null) return;

        GameObject selectedPrefab = GetRandomSpawnPrefab();
        if (selectedPrefab == null) return;

        Transform parentTransform = (boardRect != null) ? boardRect : transform.parent;
        GameObject newDragon = Instantiate(selectedPrefab, parentTransform);

        RectTransform dragonRect = newDragon.GetComponent<RectTransform>();
        dragonRect.anchoredPosition = spawnerRect.anchoredPosition;

        currentDragon = newDragon.GetComponent<Dragon>();
        if (currentDragon != null)
        {
            currentDragon.ground = ground;
            currentDragon.ApplyLevelScale();
            currentDragon.PrepareForAiming(this, cachedLeftEdge, cachedRightEdge);
        }
    }

    public void OnDragonDropped(Dragon droppedDragon)
    {
        if (currentDragon == droppedDragon)
        {
            currentDragon = null;
        }

        StartCoroutine(SpawnRoutine(dropCooldown));
    }

    private IEnumerator SpawnRoutine(float delay)
    {
        isSpawning = true;
        yield return new WaitForSeconds(delay);
        isSpawning = false;
        SpawnDragon();
    }

    public void Restart()
    {
        StopAllCoroutines();
        isSpawning = false;
        currentDragon = null;
        SpawnDragon();
    }

    #region Input Event Forwarding
    public void OnPointerDown(PointerEventData eventData)
    {
        if (RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
        {
            isAiming = false;
            RemoveDragonLifeline.Instance.TrySelectDragonAtScreenPos(eventData.position);
            return;
        }

        if (DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
        {
            isAiming = false;
            DragonBlastLifeline.Instance.TrySelectDragonAtScreenPos(eventData.position);
            return;
        }

        if (MagnetLifeline.Instance != null && MagnetLifeline.Instance.IsMagnetModeActive)
        {
            isAiming = false;
            MagnetLifeline.Instance.TrySelectDragonAtScreenPos(eventData.position);
            return;
        }

        if (IsPointerOverUIButton(eventData))
        {
            isAiming = false;
            return;
        }

        isAiming = true;
        HandleAimInput(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isAiming) return;

        if (RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
        {
            isAiming = false;
            return;
        }

        if (DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
        {
            isAiming = false;
            return;
        }

        if (MagnetLifeline.Instance != null && MagnetLifeline.Instance.IsMagnetModeActive)
        {
            isAiming = false;
            return;
        }

        HandleAimInput(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isAiming) return;
        isAiming = false;

        if (RemoveDragonLifeline.Instance != null && RemoveDragonLifeline.Instance.IsRemoveModeActive)
        {
            return;
        }

        if (DragonBlastLifeline.Instance != null && DragonBlastLifeline.Instance.IsBlastModeActive)
        {
            return;
        }

        if (MagnetLifeline.Instance != null && MagnetLifeline.Instance.IsMagnetModeActive)
        {
            return;
        }

        if (currentDragon != null && !currentDragon.isDropped)
        {
            currentDragon.Drop();
        }
    }

    private void HandleAimInput(PointerEventData eventData)
    {
        if (currentDragon == null || currentDragon.isDropped) return;

        if (boardRect != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                boardRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
            {
                currentDragon.SetAimPositionX(localPoint.x);
            }
        }
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
                if (go != null)
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
    #endregion
}

/// <summary>
/// Forwards pointer events on the board to the DragonSpawner.
/// </summary>
public class BoardInputHandler : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public DragonSpawner spawner;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (spawner != null)
            spawner.OnPointerDown(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (spawner != null)
            spawner.OnDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (spawner != null)
            spawner.OnPointerUp(eventData);
    }
}

/// <summary>
/// Upper boundary danger line trigger that ends the game ONLY when a dragon in the pile
/// stacks up to the upper boundary deadline. Touching side walls NEVER triggers Game Over.
/// </summary>
public class UpperBoundary : MonoBehaviour
{
    private bool gameOverTriggered = false;

    public void ResetBoundary()
    {
        gameOverTriggered = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CheckGameOver(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        CheckGameOver(other);
    }

    private void CheckGameOver(Collider2D other)
    {
        if (gameOverTriggered) return;

        Dragon dragon = other.GetComponent<Dragon>();
        if (dragon == null) return;

        // 1. Must be dropped
        if (!dragon.isDropped) return;

        // 2. Ignore dragons falling downward from top spawner
        Rigidbody2D rb = dragon.GetComponent<Rigidbody2D>();
        if (rb != null && rb.linearVelocity.y < -150f)
        {
            return;
        }

        // 3. Must have collided in the pile / landed
        if (!dragon.hasCollidedOnce) return;

        // 4. Must actually be at or above the danger line
        if (dragon.transform.position.y < transform.position.y - 40f)
        {
            return;
        }

        gameOverTriggered = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.GameOver();
        }
    }
}
using UnityEngine;

/// <summary>
/// DragonUnlockManager popup system has been completely removed.
/// Higher-level dragons (Mythical, Legendary, God) merge and appear directly with uninterrupted gameplay.
/// No unlock popups, time scale pauses, or input delays are executed.
/// </summary>
public class DragonUnlockManager : MonoBehaviour
{
    public static DragonUnlockManager Instance { get; private set; }

    public bool IsPopupOpen => false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Deactivate any existing UnlockPopup panel in the scene
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null)
        {
            Transform popup = canvas.transform.Find("UnlockPopup");
            if (popup != null)
            {
                popup.gameObject.SetActive(false);
            }
        }
    }

    public void OnDragonCreated(GameObject dragon) { }
    public void ShowUnlockPopup(string displayName, int level, Sprite dragonSprite) { }
    public void DismissPopup(bool silent = false) { }
    public void OnGameStart() { }
    public void OnGameOver() { }
    public void ResetSessionUnlocks() { }
    public void EnsureUI() { }
    public void OnOkButtonClicked() { }
}

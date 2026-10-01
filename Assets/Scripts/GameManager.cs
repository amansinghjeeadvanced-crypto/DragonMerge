using System.Collections;
using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Panels")]
    public GameObject gameStartPanel;
    public GameObject gameplayPanel;
    public GameObject gameOverPanel;

    [Header("Score UI Elements")]
    [Tooltip("Main score display during gameplay")]
    public TMP_Text scoreText;

    [Tooltip("Final score display on the Game Over screen")]
    public TMP_Text finalScoreText;

    [Tooltip("Second score display on the Game Over screen if present")]
    public TMP_Text gameOverScoreText;

    [Header("Visual Juice")]
    [Tooltip("Scale multiplier when score punches on point increase")]
    public float scorePunchScale = 1.2f;

    private int score = 0;

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

        AutoFindScoreUI();
    }

    private void AutoFindScoreUI()
    {
        // 1. Auto-find gameplay score text
        if (scoreText == null && gameplayPanel != null)
        {
            scoreText = gameplayPanel.GetComponentInChildren<TMP_Text>(true);
        }

        // 2. Auto-find Game Over score texts
        if (gameOverPanel != null)
        {
            TMP_Text[] gameOverTexts = gameOverPanel.GetComponentsInChildren<TMP_Text>(true);
            if (gameOverTexts.Length >= 2)
            {
                if (finalScoreText == null) finalScoreText = gameOverTexts[0];
                if (gameOverScoreText == null) gameOverScoreText = gameOverTexts[1];
            }
            else if (gameOverTexts.Length == 1 && finalScoreText == null)
            {
                finalScoreText = gameOverTexts[0];
            }
        }

        // Apply clean font styling
        ConfigureTextStyle(scoreText, 28f, 60f);
        ConfigureTextStyle(finalScoreText, 28f, 60f);
        ConfigureTextStyle(gameOverScoreText, 28f, 60f);
    }

    private void ConfigureTextStyle(TMP_Text textComp, float minSize, float maxSize)
    {
        if (textComp == null) return;
        textComp.enableAutoSizing = true;
        textComp.fontSizeMin = minSize;
        textComp.fontSizeMax = maxSize;
        textComp.alignment = TextAlignmentOptions.Center;
        textComp.overflowMode = TextOverflowModes.Overflow;
        textComp.richText = true;
        textComp.fontStyle = FontStyles.Bold;
        textComp.fontWeight = FontWeight.Bold;
        textComp.characterSpacing = 2.5f;
        textComp.enableVertexGradient = true;
        textComp.colorGradient = new VertexGradient(
            new Color(1f, 1f, 0.95f, 1f),      // Top Left: Radiant White-Gold highlight
            new Color(1f, 1f, 0.95f, 1f),      // Top Right: Radiant White-Gold highlight
            new Color(1f, 0.80f, 0.18f, 1f),   // Bottom Left: Rich Royal Gold
            new Color(1f, 0.80f, 0.18f, 1f)    // Bottom Right: Rich Royal Gold
        );

        Material fantasyMat = Resources.Load<Material>("Fonts & Materials/LiberationSans SDF - FantasyScoreGold");
        if (fantasyMat != null)
        {
            textComp.fontSharedMaterial = fantasyMat;
        }
    }

    void Start()
    {
        if (gameStartPanel != null) gameStartPanel.SetActive(true);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        UpdateScoreUI(punch: false);
    }

    public void StartGame()
    {
        Time.timeScale = 1f;

        score = 0;
        UpdateScoreUI(punch: false);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
            AudioManager.Instance.PlayMusic();
        }

        if (gameStartPanel != null) gameStartPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);

        UpperBoundary upper = FindAnyObjectByType<UpperBoundary>();
        if (upper != null) upper.ResetBoundary();

        if (RemoveDragonLifeline.Instance != null)
        {
            RemoveDragonLifeline.Instance.ResetUses();
        }

        if (DragonBlastLifeline.Instance != null)
        {
            DragonBlastLifeline.Instance.ResetUses();
        }

        if (ShuffleLifeline.Instance != null)
        {
            ShuffleLifeline.Instance.ResetUses();
        }

        if (MagnetLifeline.Instance != null)
        {
            MagnetLifeline.Instance.ResetUses();
        }
    }

    public void GameOver()
    {
        Time.timeScale = 1f;

        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(true);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayGameOver();
            AudioManager.Instance.PauseMusic();
        }

        if (RemoveDragonLifeline.Instance != null)
        {
            RemoveDragonLifeline.Instance.OnGameOver();
        }

        if (DragonBlastLifeline.Instance != null)
        {
            DragonBlastLifeline.Instance.OnGameOver();
        }

        if (ShuffleLifeline.Instance != null)
        {
            ShuffleLifeline.Instance.OnGameOver();
        }

        if (MagnetLifeline.Instance != null)
        {
            MagnetLifeline.Instance.OnGameOver();
        }

        StopAllCoroutines();
        StartCoroutine(RollUpScoreRoutine(score));
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;

        score = 0;
        UpdateScoreUI(punch: false);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
            AudioManager.Instance.PlayMusic();
        }

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(true);

        UpperBoundary upper = FindAnyObjectByType<UpperBoundary>();
        if (upper != null) upper.ResetBoundary();

        if (RemoveDragonLifeline.Instance != null)
        {
            RemoveDragonLifeline.Instance.ResetUses();
        }

        if (DragonBlastLifeline.Instance != null)
        {
            DragonBlastLifeline.Instance.ResetUses();
        }

        if (ShuffleLifeline.Instance != null)
        {
            ShuffleLifeline.Instance.ResetUses();
        }

        if (MagnetLifeline.Instance != null)
        {
            MagnetLifeline.Instance.ResetUses();
        }

        // Remove any existing dragons from the board
        Dragon[] dragons = FindObjectsByType<Dragon>(FindObjectsInactive.Exclude);
        foreach (Dragon d in dragons)
        {
            Destroy(d.gameObject);
        }

        // Restart dragon spawner
        DragonSpawner spawner = FindAnyObjectByType<DragonSpawner>();
        if (spawner != null)
        {
            spawner.Restart();
        }
    }

    public void AddScore(int points, Vector3? worldPos = null)
    {
        score += points;
        UpdateScoreUI(punch: true);
    }

    public void AddScore(int points)
    {
        AddScore(points, null);
    }

    private void UpdateScoreUI(bool punch)
    {
        if (scoreText != null)
        {
            scoreText.text = score.ToString();

            if (punch && gameObject.activeInHierarchy)
            {
                StartCoroutine(PunchScale(scoreText.rectTransform, scorePunchScale, 0.2f));
            }
        }
    }

    private IEnumerator RollUpScoreRoutine(int targetScore)
    {
        float duration = (targetScore > 0) ? 0.9f : 0.05f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3);
            int displayVal = Mathf.RoundToInt(Mathf.Lerp(0, targetScore, ease));

            SetGameOverScoreText(displayVal);
            yield return null;
        }

        SetGameOverScoreText(targetScore);

        if (finalScoreText != null)
        {
            StartCoroutine(PunchScale(finalScoreText.rectTransform, 1.25f, 0.25f));
        }
        if (gameOverScoreText != null && gameOverScoreText != finalScoreText)
        {
            StartCoroutine(PunchScale(gameOverScoreText.rectTransform, 1.25f, 0.25f));
        }
    }

    private void SetGameOverScoreText(int val)
    {
        string text = val.ToString();
        if (finalScoreText != null)
        {
            finalScoreText.text = text;
        }
        if (gameOverScoreText != null)
        {
            gameOverScoreText.text = text;
        }
    }

    private IEnumerator PunchScale(RectTransform target, float punchScale, float duration)
    {
        if (target == null) yield break;

        Vector3 originalScale = Vector3.one;
        float elapsed = 0f;
        float halfDuration = duration * 0.5f;

        // Scale up
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.Lerp(originalScale, originalScale * punchScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        elapsed = 0f;
        float remainingDuration = duration - halfDuration;
        // Settle back
        while (elapsed < remainingDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / remainingDuration;
            target.localScale = Vector3.Lerp(originalScale * punchScale, originalScale, t);
            yield return null;
        }

        target.localScale = originalScale;
    }

    public void QuitGame()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
        Application.Quit();
    }
}
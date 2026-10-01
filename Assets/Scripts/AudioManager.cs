using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Centralized AudioManager handling background music and sound effects (SFX)
/// for dragon drop, collisions, merging, game over, and button clicks.
/// All clips and volume controls are exposed in the Inspector.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<AudioManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("AudioManager");
                    _instance = go.AddComponent<AudioManager>();
                }
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    [Header("Background Music")]
    [Tooltip("Looping background music clip")]
    public AudioClip musicClip;

    [Range(0f, 1f)]
    [Tooltip("Music volume (0 = silent, 1 = max)")]
    public float musicVolume = 0.75f;

    [Tooltip("Should background music loop continuously")]
    public bool loopMusic = true;

    [Header("Sound Effects (SFX)")]
    [Range(0f, 1f)]
    [Tooltip("Master SFX volume (0 = silent, 1 = max)")]
    public float sfxVolume = 1f;

    [Tooltip("Merge sound — played when two matching dragons successfully merge")]
    public AudioClip mergeClip;

    [Tooltip("Game Over sound — played once when game over occurs")]
    public AudioClip gameOverClip;

    [Tooltip("Button click sound — played when UI buttons are clicked")]
    public AudioClip buttonClickClip;

    [Tooltip("Remove dragon lifeline pop sound")]
    public AudioClip removeDragonClip;

    [Tooltip("Dragon blast lifeline explosion sound")]
    public AudioClip dragonBlastClip;

    [Tooltip("Shuffle lifeline magic swirl sound")]
    public AudioClip shuffleClip;

    [Tooltip("Magnet lifeline magnetic attraction sound")]
    public AudioClip magnetClip;

    // Dedicated internal AudioSources
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private AudioSource mergeSource;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            SetupAudioSources();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void SetupAudioSources()
    {
        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = loopMusic;
            musicSource.volume = musicVolume;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.volume = sfxVolume;
        }

        if (mergeSource == null)
        {
            mergeSource = gameObject.AddComponent<AudioSource>();
            mergeSource.playOnAwake = false;
            mergeSource.loop = false;
            mergeSource.volume = sfxVolume;
        }
    }

    void Start()
    {
        AutoHookButtons();
    }

    void OnValidate()
    {
        ApplyVolumeSettings();
    }

    public void ApplyVolumeSettings()
    {
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
            musicSource.loop = loopMusic;
        }
        if (sfxSource != null)
        {
            sfxSource.volume = sfxVolume;
        }
        if (mergeSource != null)
        {
            mergeSource.volume = sfxVolume;
        }
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null)
        {
            sfxSource.volume = sfxVolume;
        }
        if (mergeSource != null)
        {
            mergeSource.volume = sfxVolume;
        }
    }

    #region Music Control
    /// <summary>
    /// Starts or resumes background music without creating duplicate sources or overlapping music.
    /// </summary>
    public void PlayMusic()
    {
        if (musicSource == null) SetupAudioSources();
        if (musicClip == null) return;

        musicSource.volume = musicVolume;
        musicSource.loop = loopMusic;

        if (musicSource.clip != musicClip)
        {
            musicSource.clip = musicClip;
            musicSource.Play();
        }
        else if (!musicSource.isPlaying)
        {
            musicSource.UnPause();
            if (!musicSource.isPlaying)
            {
                musicSource.Play();
            }
        }
    }

    /// <summary>
    /// Pauses background music (e.g. on Game Over).
    /// </summary>
    public void PauseMusic()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            musicSource.Pause();
        }
    }

    /// <summary>
    /// Stops background music completely.
    /// </summary>
    public void StopMusic()
    {
        if (musicSource != null)
        {
            musicSource.Stop();
        }
    }
    #endregion

    #region Sound Effects Control
    /// <summary>
    /// Plays satisfying merge sound when two matching dragons merge.
    /// Played on dedicated source so it is prominent and never cut off by collision sounds.
    /// </summary>
    public void PlayMerge()
    {
        if (mergeClip == null) return;
        if (mergeSource == null) SetupAudioSources();
        mergeSource.PlayOneShot(mergeClip, sfxVolume);
    }

    /// <summary>
    /// Plays Game Over sound once when the deadline is breached.
    /// </summary>
    public void PlayGameOver()
    {
        if (gameOverClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(gameOverClip, sfxVolume);
    }

    /// <summary>
    /// Plays UI button click sound for Play, Restart, and other buttons.
    /// </summary>
    public void PlayButtonClick()
    {
        if (buttonClickClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(buttonClickClip, sfxVolume);
    }

    /// <summary>
    /// Plays pop sound when a dragon is removed using the Remove Dragon lifeline.
    /// </summary>
    public void PlayRemoveDragon()
    {
        if (removeDragonClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(removeDragonClip, sfxVolume);
    }

    /// <summary>
    /// Plays explosion sound once when the Dragon Blast lifeline is activated.
    /// </summary>
    public void PlayDragonBlast()
    {
        if (dragonBlastClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(dragonBlastClip, sfxVolume);
    }

    /// <summary>
    /// Plays magic swirl sound once when the Shuffle lifeline is activated.
    /// </summary>
    public void PlayShuffle()
    {
        if (shuffleClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(shuffleClip, sfxVolume);
    }

    /// <summary>
    /// Plays magnetic attraction sound once when the Magnet lifeline is activated.
    /// </summary>
    public void PlayMagnet()
    {
        if (magnetClip == null) return;
        if (sfxSource == null) SetupAudioSources();
        sfxSource.PlayOneShot(magnetClip, sfxVolume);
    }
    #endregion

    #region Auto Button Hooking
    /// <summary>
    /// Automatically registers PlayButtonClick to all UI Buttons in the scene.
    /// </summary>
    public void AutoHookButtons()
    {
        Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button b in buttons)
        {
            if (b == null || b.gameObject.scene.name == null) continue; // Skip prefabs
            b.onClick.RemoveListener(PlayButtonClick);
            b.onClick.AddListener(PlayButtonClick);
        }
    }
    #endregion
}

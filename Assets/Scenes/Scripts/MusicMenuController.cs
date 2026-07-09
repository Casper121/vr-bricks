using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// Music menu controller with a simple built-in UI that looks like a compact dark music panel.
///
/// Features:
/// - Separate Play and Pause buttons. They sit on the same position and switch visibility.
/// - Previous song, next song, stop
/// - Progress bar / song position slider
/// - Master volume, music volume, per-song volume, sound volume prepared for later game sounds
/// - Optional AudioMixer support
/// - Can auto-build the UI if buttons/sliders are not assigned
///
/// Put this script on your MusicMenu panel.
/// Add your songs to Music Clips in the Inspector.
/// Use Song Volumes to make individual songs quieter/louder. Same index as Music Clips.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class LegoMusicMenuController : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Music
    // ---------------------------------------------------------------------

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private List<AudioClip> musicClips = new List<AudioClip>();

    [Tooltip("Optional per-song volume multiplier. Same index as Music Clips. 1 = normal, 0.5 = half volume.")]
    [SerializeField] private List<float> songVolumes = new List<float>();

    [SerializeField] private bool playOnStart = false;
    [SerializeField] private bool loopSingleSong = false;
    [SerializeField] private bool autoPlayNextSong = true;

    [Header("Runtime Audio Player")]
    [Tooltip("Keep this enabled. The AudioSource is moved/created outside the MusicMenu so closing the panel does not stop the music.")]
    [SerializeField] private bool keepAudioPlayingWhenMenuCloses = true;

    [Tooltip("Name of the scene object that holds the runtime AudioSource.")]
    [SerializeField] private string runtimeAudioPlayerName = "LegoMusicRuntimeAudioPlayer";

    // ---------------------------------------------------------------------
    // Volume
    // ---------------------------------------------------------------------

    [Header("Volume")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultMasterVolume = 0.8f;

    [Range(0f, 1f)]
    [SerializeField] private float defaultMusicVolume = 0.65f;

    [Range(0f, 1f)]
    [SerializeField] private float defaultSoundVolume = 0.8f;

    [Header("Audio Mixer Optional")]
    [Tooltip("Optional. If assigned, the script writes volume values to exposed mixer parameters in decibels.")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string masterVolumeParameter = "MasterVolume";
    [SerializeField] private string musicVolumeParameter = "MusicVolume";
    [SerializeField] private string soundVolumeParameter = "SoundVolume";

    // ---------------------------------------------------------------------
    // UI References
    // ---------------------------------------------------------------------

    [Header("UI References")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text songTitleText;

    [SerializeField] private Button previousButton;
    [SerializeField] private Button playButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button nextButton;

    [SerializeField] private TMP_Text previousButtonLabel;
    [SerializeField] private TMP_Text playButtonLabel;
    [SerializeField] private TMP_Text pauseButtonLabel;
    [SerializeField] private TMP_Text stopButtonLabel;
    [SerializeField] private TMP_Text nextButtonLabel;

    [SerializeField] private Slider songProgressSlider;

    [SerializeField] private TMP_Text masterVolumeLabel;
    [SerializeField] private TMP_Text musicVolumeLabel;
    [SerializeField] private TMP_Text soundVolumeLabel;

    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider soundVolumeSlider;

    // ---------------------------------------------------------------------
    // Auto Build UI
    // ---------------------------------------------------------------------

    [Header("Auto Build UI")]
    [SerializeField] private bool autoBuildMissingUI = true;
    [SerializeField] private Vector2 panelSize = new Vector2(520f, 600f);
    [SerializeField] private Color panelColor = new Color(0.055f, 0.055f, 0.065f, 0.96f);
    [SerializeField] private Color mainTextColor = Color.white;
    [SerializeField] private Color sliderFillColor = new Color(0.44f, 0.48f, 0.92f, 1f);
    [SerializeField] private Color sliderBackgroundColor = new Color(0.82f, 0.82f, 0.82f, 1f);
    [SerializeField] private Color sliderHandleColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    // ---------------------------------------------------------------------
    // Runtime State
    // ---------------------------------------------------------------------

    private int currentClipIndex;
    private bool isPaused;
    private float currentMusicVolume;
    private bool isDraggingProgress;
    private RuntimeMusicTicker runtimeTicker;

    private const string PlayIcon = "▶";
    private const string PauseIcon = "❚❚";
    private const string StopIcon = "■";
    private const string NextIcon = "▶▶";
    private const string PreviousIcon = "◀◀";

    // ---------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------

    private void Awake()
    {
        EnsureSongVolumeList();
        EnsureAudioSource();

        if (autoBuildMissingUI && MissingRequiredUI())
            BuildMusicUI();

        AutoFindMissingButtonLabels();
        WireUI();
        ApplyInitialValues();
        RefreshUI();
    }

    private void Start()
    {
        EnsureSongVolumeList();

        if (playOnStart && musicClips.Count > 0)
            PlayCurrentSong();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureSongVolumeList();
    }
#endif

    private void OnEnable()
    {
        // When the hidden menu becomes visible again, immediately refresh the buttons and progress.
        RefreshUI();
        UpdateSongProgressUI();
    }

    private void Update()
    {
        RuntimeTick();
    }

    /// <summary>
    /// Called by this component while the menu is active and by the runtime audio player
    /// while the menu is hidden. This keeps auto-next and progress state alive even when
    /// the visual panel is closed.
    /// </summary>
    public void RuntimeTick()
    {
        UpdateSongProgressUI();
        AutoPlayNextIfNeeded();
    }

    private void OnDestroy()
    {
        UnwireUI();
    }

    // ---------------------------------------------------------------------
    // Public Controls
    // ---------------------------------------------------------------------

    public void PlayMusic()
    {
        if (musicClips.Count == 0)
        {
            Debug.LogWarning("LegoMusicMenuController: No music clips assigned.", this);
            return;
        }

        if (musicSource.clip == null)
        {
            PlayCurrentSong();
            return;
        }

        if (isPaused)
            musicSource.UnPause();
        else if (!musicSource.isPlaying)
            musicSource.Play();

        isPaused = false;
        RefreshUI();
    }

    public void PauseMusic()
    {
        if (musicSource == null || musicSource.clip == null)
            return;

        if (!musicSource.isPlaying)
            return;

        musicSource.Pause();
        isPaused = true;
        RefreshUI();
    }

    // Kept for compatibility with older UI events. New UI uses PlayMusic and PauseMusic separately.
    public void TogglePlayPause()
    {
        if (musicSource != null && musicSource.isPlaying)
            PauseMusic();
        else
            PlayMusic();
    }

    public void StopMusic()
    {
        if (musicSource == null)
            return;

        musicSource.Stop();
        musicSource.time = 0f;
        isPaused = false;
        hasCurrentSongStarted = false;
        RefreshUI();
    }

    public void NextSong()
    {
        if (musicClips.Count == 0)
            return;

        currentClipIndex++;

        if (currentClipIndex >= musicClips.Count)
            currentClipIndex = 0;

        PlayCurrentSong();
    }

    public void PreviousSong()
    {
        if (musicClips.Count == 0)
            return;

        currentClipIndex--;

        if (currentClipIndex < 0)
            currentClipIndex = musicClips.Count - 1;

        PlayCurrentSong();
    }

    public void SetMasterVolume(float value)
    {
        value = Mathf.Clamp01(value);
        AudioListener.volume = value;
        SetMixerLinearVolume(masterVolumeParameter, value);
        RefreshVolumeLabels();
    }

    public void SetMusicVolume(float value)
    {
        currentMusicVolume = Mathf.Clamp01(value);
        UpdateMusicSourceVolume();
        SetMixerLinearVolume(musicVolumeParameter, currentMusicVolume);
        RefreshVolumeLabels();
    }

    public void SetSoundVolume(float value)
    {
        value = Mathf.Clamp01(value);
        LegoAudioSettings.SetSoundVolume(value);
        SetMixerLinearVolume(soundVolumeParameter, value);
        RefreshVolumeLabels();
    }

    public void BeginProgressDrag()
    {
        isDraggingProgress = true;
    }

    public void EndProgressDrag()
    {
        isDraggingProgress = false;
        ApplyProgressSliderToSong();
    }

    public void ApplyProgressSliderToSong()
    {
        if (songProgressSlider == null || musicSource == null || musicSource.clip == null)
            return;

        float targetTime = Mathf.Clamp01(songProgressSlider.value) * musicSource.clip.length;
        musicSource.time = targetTime;
    }

    // ---------------------------------------------------------------------
    // Setup
    // ---------------------------------------------------------------------

    private void EnsureAudioSource()
    {
        if (!keepAudioPlayingWhenMenuCloses)
        {
            if (musicSource == null)
                musicSource = GetComponent<AudioSource>();

            if (musicSource == null)
                musicSource = gameObject.AddComponent<AudioSource>();

            ConfigureAudioSource(musicSource);
            return;
        }

        // Important:
        // If the AudioSource lives on the MusicMenu or below it, Unity stops it
        // when the panel GameObject is disabled. Therefore the real player lives
        // on a separate active scene object. The menu can open/close freely.
        if (musicSource == null || musicSource.transform == transform || musicSource.transform.IsChildOf(transform))
            musicSource = GetOrCreateRuntimeAudioSource();

        ConfigureAudioSource(musicSource);
        RegisterRuntimeTicker();
    }

    private AudioSource GetOrCreateRuntimeAudioSource()
    {
        string safeName = string.IsNullOrWhiteSpace(runtimeAudioPlayerName)
            ? "LegoMusicRuntimeAudioPlayer"
            : runtimeAudioPlayerName;

        GameObject playerObject = GameObject.Find(safeName);

        if (playerObject == null)
        {
            playerObject = new GameObject(safeName);

            // Keep it at scene root so it is not disabled together with the menu.
            playerObject.transform.SetParent(null);
        }

        AudioSource source = playerObject.GetComponent<AudioSource>();

        if (source == null)
            source = playerObject.AddComponent<AudioSource>();

        return source;
    }

    private void RegisterRuntimeTicker()
    {
        if (musicSource == null)
            return;

        runtimeTicker = musicSource.GetComponent<RuntimeMusicTicker>();

        if (runtimeTicker == null)
            runtimeTicker = musicSource.gameObject.AddComponent<RuntimeMusicTicker>();

        runtimeTicker.SetController(this);
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = loopSingleSong;
        source.spatialBlend = 0f;
        UpdateMusicSourceVolume();
    }

    private bool MissingRequiredUI()
    {
        return titleText == null ||
               songTitleText == null ||
               previousButton == null ||
               playButton == null ||
               pauseButton == null ||
               stopButton == null ||
               nextButton == null ||
               songProgressSlider == null ||
               masterVolumeSlider == null ||
               musicVolumeSlider == null ||
               soundVolumeSlider == null;
    }

    private void WireUI()
    {
        if (previousButton != null)
            previousButton.onClick.AddListener(PreviousSong);

        if (playButton != null)
            playButton.onClick.AddListener(PlayMusic);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(PauseMusic);

        if (stopButton != null)
            stopButton.onClick.AddListener(StopMusic);

        if (nextButton != null)
            nextButton.onClick.AddListener(NextSong);

        if (songProgressSlider != null)
            songProgressSlider.onValueChanged.AddListener(OnProgressSliderChanged);

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);

        if (soundVolumeSlider != null)
            soundVolumeSlider.onValueChanged.AddListener(SetSoundVolume);
    }

    private void UnwireUI()
    {
        if (previousButton != null)
            previousButton.onClick.RemoveListener(PreviousSong);

        if (playButton != null)
            playButton.onClick.RemoveListener(PlayMusic);

        if (pauseButton != null)
            pauseButton.onClick.RemoveListener(PauseMusic);

        if (stopButton != null)
            stopButton.onClick.RemoveListener(StopMusic);

        if (nextButton != null)
            nextButton.onClick.RemoveListener(NextSong);

        if (songProgressSlider != null)
            songProgressSlider.onValueChanged.RemoveListener(OnProgressSliderChanged);

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(SetMasterVolume);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(SetMusicVolume);

        if (soundVolumeSlider != null)
            soundVolumeSlider.onValueChanged.RemoveListener(SetSoundVolume);
    }

    private void ApplyInitialValues()
    {
        SetupSlider(masterVolumeSlider, defaultMasterVolume);
        SetupSlider(musicVolumeSlider, defaultMusicVolume);
        SetupSlider(soundVolumeSlider, defaultSoundVolume);
        SetupSlider(songProgressSlider, 0f);

        currentMusicVolume = Mathf.Clamp01(defaultMusicVolume);

        SetMasterVolume(defaultMasterVolume);
        SetMusicVolume(defaultMusicVolume);
        SetSoundVolume(defaultSoundVolume);
    }

    private void SetupSlider(Slider slider, float value)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
    }

    private void EnsureSongVolumeList()
    {
        if (songVolumes == null)
            songVolumes = new List<float>();

        while (songVolumes.Count < musicClips.Count)
            songVolumes.Add(1f);

        for (int i = 0; i < songVolumes.Count; i++)
            songVolumes[i] = Mathf.Clamp01(songVolumes[i]);
    }

    private float GetCurrentSongVolume()
    {
        if (songVolumes == null || currentClipIndex < 0 || currentClipIndex >= songVolumes.Count)
            return 1f;

        return Mathf.Clamp01(songVolumes[currentClipIndex]);
    }

    private void UpdateMusicSourceVolume()
    {
        if (musicSource == null)
            return;

        musicSource.volume = Mathf.Clamp01(currentMusicVolume) * GetCurrentSongVolume();
    }

    // ---------------------------------------------------------------------
    // Playback
    // ---------------------------------------------------------------------

    private void PlayCurrentSong()
    {
        if (musicSource == null || musicClips.Count == 0)
            return;

        currentClipIndex = Mathf.Clamp(currentClipIndex, 0, musicClips.Count - 1);

        AudioClip clip = musicClips[currentClipIndex];

        if (clip == null)
        {
            Debug.LogWarning("LegoMusicMenuController: Music clip slot is empty.", this);
            return;
        }

        musicSource.clip = clip;
        musicSource.loop = loopSingleSong;
        musicSource.time = 0f;
        UpdateMusicSourceVolume();
        musicSource.Play();
        isPaused = false;

        // FIX: AutoPlayNextIfNeeded used to detect "song naturally finished"
        // by checking musicSource.time > 0 - but Unity resets time back to 0
        // itself once a non-looping clip finishes, and exactly WHEN it does
        // that reset relative to our own Update() check varies frame to
        // frame. That race is exactly why auto-advance worked "sometimes" and
        // just silently stalled other times. Tracking "has this song actually
        // started playing" ourselves removes the guesswork entirely - it's
        // set true here, right after telling the AudioSource to play.
        hasCurrentSongStarted = true;

        RefreshUI();
    }

    private bool hasCurrentSongStarted;

    private void AutoPlayNextIfNeeded()
    {
        if (!autoPlayNextSong || musicSource == null || loopSingleSong)
            return;

        if (!hasCurrentSongStarted)
            return;

        if (musicSource.clip == null || musicSource.isPlaying || isPaused)
            return;

        // The song genuinely stopped on its own (not paused, not still
        // playing) after having actually started - that's a real "song
        // finished" event, regardless of whatever musicSource.time happens
        // to read right now.
        hasCurrentSongStarted = false;
        NextSong();
    }

    private void OnProgressSliderChanged(float value)
    {
        if (isDraggingProgress)
            return;
    }

    private void UpdateSongProgressUI()
    {
        if (songProgressSlider == null || musicSource == null || musicSource.clip == null || isDraggingProgress)
            return;

        float length = Mathf.Max(0.0001f, musicSource.clip.length);
        songProgressSlider.value = Mathf.Clamp01(musicSource.time / length);
    }

    // ---------------------------------------------------------------------
    // UI Refresh
    // ---------------------------------------------------------------------

    private void RefreshUI()
    {
        if (titleText != null)
            titleText.text = "Music";

        if (songTitleText != null)
            songTitleText.text = GetCurrentSongName();

        if (previousButtonLabel != null)
            previousButtonLabel.text = PreviousIcon;

        if (stopButtonLabel != null)
            stopButtonLabel.text = StopIcon;

        if (nextButtonLabel != null)
            nextButtonLabel.text = NextIcon;

        if (playButtonLabel != null)
            playButtonLabel.text = PlayIcon;

        if (pauseButtonLabel != null)
            pauseButtonLabel.text = PauseIcon;

        bool playing = musicSource != null && musicSource.isPlaying;

        if (playButton != null)
            playButton.gameObject.SetActive(!playing);

        if (pauseButton != null)
            pauseButton.gameObject.SetActive(playing);

        RefreshVolumeLabels();
    }

    private string GetCurrentSongName()
    {
        if (musicSource != null && musicSource.clip != null)
            return "„" + musicSource.clip.name + "“";

        if (musicClips.Count > 0)
        {
            int index = Mathf.Clamp(currentClipIndex, 0, musicClips.Count - 1);

            if (musicClips[index] != null)
                return "„" + musicClips[index].name + "“";
        }

        return "Kein Song ausgewählt";
    }

    private void RefreshVolumeLabels()
    {
        if (masterVolumeLabel != null)
            masterVolumeLabel.text = "Gesamtlautstärke";

        if (musicVolumeLabel != null)
            musicVolumeLabel.text = "Musiklautstärke";

        if (soundVolumeLabel != null)
            soundVolumeLabel.text = "Soundlautstärke";
    }

    private void SetMixerLinearVolume(string parameterName, float linearValue)
    {
        if (audioMixer == null || string.IsNullOrEmpty(parameterName))
            return;

        float safeValue = Mathf.Clamp(linearValue, 0.0001f, 1f);
        float decibels = Mathf.Log10(safeValue) * 20f;
        audioMixer.SetFloat(parameterName, decibels);
    }

    // ---------------------------------------------------------------------
    // Auto UI Builder
    // ---------------------------------------------------------------------

    private void BuildMusicUI()
    {
        RectTransform root = GetComponent<RectTransform>();
        root.sizeDelta = panelSize;

        Image background = GetComponent<Image>();

        if (background == null)
            background = gameObject.AddComponent<Image>();

        background.color = panelColor;

        titleText = CreateText("Title", "Music", new Vector2(0f, 245f), new Vector2(460f, 45f), 32, FontStyles.Bold);
        songTitleText = CreateText("SongTitle", "Kein Song ausgewählt", new Vector2(0f, 150f), new Vector2(470f, 45f), 25, FontStyles.Bold);

        previousButton = CreateIconButton("PreviousButton", PreviousIcon, new Vector2(-126f, 88f), new Vector2(58f, 58f), out previousButtonLabel);
        playButton = CreateIconButton("PlayButton", PlayIcon, new Vector2(-54f, 88f), new Vector2(68f, 68f), out playButtonLabel);
        pauseButton = CreateIconButton("PauseButton", PauseIcon, new Vector2(-54f, 88f), new Vector2(68f, 68f), out pauseButtonLabel);
        stopButton = CreateIconButton("StopButton", StopIcon, new Vector2(26f, 88f), new Vector2(56f, 56f), out stopButtonLabel);
        nextButton = CreateIconButton("NextButton", NextIcon, new Vector2(96f, 88f), new Vector2(58f, 58f), out nextButtonLabel);

        songProgressSlider = CreateSlider("SongProgressSlider", new Vector2(0f, 35f), new Vector2(430f, 18f), 0f);

        masterVolumeLabel = CreateText("MasterVolumeLabel", "Gesamtlautstärke", new Vector2(0f, -70f), new Vector2(460f, 38f), 27, FontStyles.Bold);
        masterVolumeSlider = CreateSlider("MasterVolumeSlider", new Vector2(0f, -110f), new Vector2(430f, 20f), defaultMasterVolume);

        musicVolumeLabel = CreateText("MusicVolumeLabel", "Musiklautstärke", new Vector2(0f, -155f), new Vector2(460f, 38f), 27, FontStyles.Bold);
        musicVolumeSlider = CreateSlider("MusicVolumeSlider", new Vector2(0f, -195f), new Vector2(430f, 20f), defaultMusicVolume);

        soundVolumeLabel = CreateText("SoundVolumeLabel", "Soundlautstärke", new Vector2(0f, -240f), new Vector2(460f, 38f), 27, FontStyles.Bold);
        soundVolumeSlider = CreateSlider("SoundVolumeSlider", new Vector2(0f, -280f), new Vector2(430f, 20f), defaultSoundVolume);
    }

    private TMP_Text CreateText(string objectName, string text, Vector2 anchoredPosition, Vector2 size, int fontSize, FontStyles style)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = mainTextColor;
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return label;
    }

    private Button CreateIconButton(string objectName, string icon, Vector2 anchoredPosition, Vector2 size, out TMP_Text label)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = go.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);

        Button button = go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        label = CreateText(objectName + "Label", icon, Vector2.zero, size, 36, FontStyles.Bold);
        label.transform.SetParent(go.transform, false);

        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }

    private Slider CreateSlider(string objectName, Vector2 anchoredPosition, Vector2 size, float defaultValue)
    {
        GameObject root = new GameObject(objectName);
        root.transform.SetParent(transform, false);

        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = size;

        Slider slider = root.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(defaultValue);

        GameObject backgroundGO = new GameObject("Background");
        backgroundGO.transform.SetParent(root.transform, false);
        RectTransform backgroundRect = backgroundGO.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image background = backgroundGO.AddComponent<Image>();
        background.color = sliderBackgroundColor;

        GameObject fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(root.transform, false);
        RectTransform fillAreaRect = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        RectTransform fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillGO.AddComponent<Image>();
        fill.color = sliderFillColor;

        GameObject handleAreaGO = new GameObject("Handle Slide Area");
        handleAreaGO.transform.SetParent(root.transform, false);
        RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = Vector2.zero;
        handleAreaRect.offsetMax = Vector2.zero;

        GameObject handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaGO.transform, false);
        RectTransform handleRect = handleGO.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(26f, 26f);
        Image handle = handleGO.AddComponent<Image>();
        handle.color = sliderHandleColor;

        slider.targetGraphic = handle;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    private void AutoFindMissingButtonLabels()
    {
        if (previousButtonLabel == null)
            previousButtonLabel = FindLabelInButton(previousButton);

        if (playButtonLabel == null)
            playButtonLabel = FindLabelInButton(playButton);

        if (pauseButtonLabel == null)
            pauseButtonLabel = FindLabelInButton(pauseButton);

        if (stopButtonLabel == null)
            stopButtonLabel = FindLabelInButton(stopButton);

        if (nextButtonLabel == null)
            nextButtonLabel = FindLabelInButton(nextButton);
    }

    private TMP_Text FindLabelInButton(Button button)
    {
        if (button == null)
            return null;

        return button.GetComponentInChildren<TMP_Text>(true);
    }

    // ---------------------------------------------------------------------
    // Runtime ticker
    // ---------------------------------------------------------------------

    /// <summary>
    /// Lives on the active runtime AudioSource object, not on the MusicMenu panel.
    /// Unity stops Update on disabled menu panels, so this small helper keeps
    /// auto-next running while the menu is closed.
    /// </summary>
    private class RuntimeMusicTicker : MonoBehaviour
    {
        private LegoMusicMenuController controller;

        public void SetController(LegoMusicMenuController newController)
        {
            controller = newController;
        }

        private void Update()
        {
            if (controller != null)
                controller.RuntimeTick();
        }
    }

}
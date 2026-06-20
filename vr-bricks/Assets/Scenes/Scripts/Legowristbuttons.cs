using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Controls the three wrist buttons on the left controller.
/// 
/// The wrist buttons can open:
/// - the block menu,
/// - the music player panel,
/// - the main menu panel.
/// 
/// While any connected panel is open or closing, the wrist buttons hide.
/// When all panels are closed, the wrist buttons smoothly appear again.
/// </summary>
public class LegoWristButtons : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Button References
    // -------------------------------------------------------------------------

    [Header("Button References")]
    public Button blockMenuButton;
    public Button musicPlayerButton;
    public Button mainMenuButton;

    // -------------------------------------------------------------------------
    // Inspector: Button Icons
    // -------------------------------------------------------------------------

    [Header("Button Icons")]
    public Sprite blockMenuIcon;
    public Sprite musicPlayerIcon;
    public Sprite mainMenuIcon;

    // -------------------------------------------------------------------------
    // Inspector: Panels
    // -------------------------------------------------------------------------

    [Header("Panels to Open")]
    public GameObject blockMenu;
    public GameObject musicPlayerPanel;
    public GameObject mainMenuPanel;

    // -------------------------------------------------------------------------
    // Inspector: Buttons Root
    // -------------------------------------------------------------------------

    [Header("Buttons Root")]
    [Tooltip("The GameObject that contains all three buttons. Gets hidden when a panel is open or closing.")]
    public GameObject buttonsRoot;

    // -------------------------------------------------------------------------
    // Inspector: Return Animation
    // -------------------------------------------------------------------------

    [Header("Buttons Return Animation")]
    [Tooltip("How long the wrist buttons smooth-in animation takes.")]
    public float showDuration = 0.28f;

    [Tooltip("Start scale for the smooth-in animation. 0.85 = subtle, 0 = full pop from nothing.")]
    public float startScale = 0.85f;

    [Tooltip("If enabled, the buttons fade in softly. This does NOT disable or grey out the buttons.")]
    public bool useFade = false;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private bool buttonsShouldBeVisible = true;

    private Vector3 buttonsOriginalScale = Vector3.one;
    private CanvasGroup buttonsCanvasGroup;
    private Coroutine showRoutine;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        CacheButtonsRoot();
    }

    private void Start()
    {
        SetupButton(blockMenuButton, blockMenuIcon, OnBlockMenuPressed);
        SetupButton(musicPlayerButton, musicPlayerIcon, OnMusicPlayerPressed);
        SetupButton(mainMenuButton, mainMenuIcon, OnMainMenuPressed);

        ForceRefreshVisibility();
    }

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Caches the original root scale and prepares the optional CanvasGroup fade.
    /// </summary>
    private void CacheButtonsRoot()
    {
        if (buttonsRoot == null)
            return;

        buttonsOriginalScale = buttonsRoot.transform.localScale;

        if (!useFade)
            return;

        buttonsCanvasGroup = buttonsRoot.GetComponent<CanvasGroup>();

        if (buttonsCanvasGroup == null)
            buttonsCanvasGroup = buttonsRoot.AddComponent<CanvasGroup>();

        // Important:
        // Never set interactable/blocksRaycasts to false during show,
        // because that can make Unity buttons look grey/disabled.
        buttonsCanvasGroup.interactable = true;
        buttonsCanvasGroup.blocksRaycasts = true;
    }

    /// <summary>
    /// Assigns icon, click action, button colors, and hover animation.
    /// </summary>
    private void SetupButton(Button btn, Sprite icon, UnityEngine.Events.UnityAction action)
    {
        if (btn == null)
            return;

        btn.interactable = true;

        if (icon != null)
        {
            Image img = btn.GetComponent<Image>();

            if (img != null)
            {
                img.sprite = icon;
                img.color = Color.white;
            }
        }

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.disabledColor = Color.white;
        btn.colors = cb;

        LegoButtonHover.AddTo(btn.gameObject);
    }

    // -------------------------------------------------------------------------
    // Visibility
    // -------------------------------------------------------------------------

    /// <summary>
    /// Immediately applies the correct visibility state when the scene starts.
    /// </summary>
    private void ForceRefreshVisibility()
    {
        bool shouldShow = !AnyPanelOpenOrClosing();

        buttonsShouldBeVisible = shouldShow;

        if (buttonsRoot != null)
        {
            buttonsRoot.SetActive(shouldShow);
            buttonsRoot.transform.localScale = buttonsOriginalScale;
        }

        if (buttonsCanvasGroup != null)
        {
            buttonsCanvasGroup.alpha = 1f;
            buttonsCanvasGroup.interactable = true;
            buttonsCanvasGroup.blocksRaycasts = true;
        }
    }

    /// <summary>
    /// Shows or hides the wrist buttons when panel visibility changes.
    /// </summary>
    private void UpdateButtonsVisibility()
    {
        if (buttonsRoot == null)
            return;

        bool shouldShow = !AnyPanelOpenOrClosing();

        if (buttonsShouldBeVisible == shouldShow)
            return;

        buttonsShouldBeVisible = shouldShow;

        if (shouldShow)
            ShowButtonsSmooth();
        else
            HideButtonsImmediately();
    }

    /// <summary>
    /// Returns true if any connected panel is open or currently closing.
    /// </summary>
    private bool AnyPanelOpenOrClosing()
    {
        return IsOpen(blockMenu) ||
               IsOpen(musicPlayerPanel) ||
               IsOpen(mainMenuPanel);
    }

    /// <summary>
    /// Returns true if the LEGO hand menu is open or in its close animation.
    /// </summary>
    private bool IsOpenOrClosing(LegoHandMenu menu)
    {
        if (menu == null)
            return false;

        return menu.IsOpenOrClosing;
    }

    /// <summary>
    /// Returns true if the given panel GameObject is active.
    /// </summary>
    private bool IsOpen(GameObject panel)
    {
        return panel != null && panel.activeSelf;
    }

    /// <summary>
    /// Hides the wrist buttons immediately.
    /// </summary>
    private void HideButtonsImmediately()
    {
        if (buttonsRoot == null)
            return;

        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        if (buttonsCanvasGroup != null)
        {
            buttonsCanvasGroup.alpha = 1f;
            buttonsCanvasGroup.interactable = true;
            buttonsCanvasGroup.blocksRaycasts = true;
        }

        buttonsRoot.transform.localScale = buttonsOriginalScale;
        buttonsRoot.SetActive(false);
    }

    /// <summary>
    /// Starts the smooth return animation for the wrist buttons.
    /// </summary>
    private void ShowButtonsSmooth()
    {
        if (buttonsRoot == null)
            return;

        if (showRoutine != null)
            StopCoroutine(showRoutine);

        buttonsRoot.SetActive(true);
        showRoutine = StartCoroutine(ShowButtonsRoutine());
    }

    /// <summary>
    /// Smoothly scales and optionally fades the wrist buttons back in.
    /// </summary>
    private IEnumerator ShowButtonsRoutine()
    {
        if (buttonsRoot == null)
            yield break;

        buttonsRoot.transform.localScale = buttonsOriginalScale * startScale;

        if (buttonsCanvasGroup != null)
        {
            buttonsCanvasGroup.alpha = useFade ? 0f : 1f;
            buttonsCanvasGroup.interactable = true;
            buttonsCanvasGroup.blocksRaycasts = true;
        }

        float elapsed = 0f;

        while (elapsed < showDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / showDuration);
            float eased = EaseOutCubic(t);

            float scale = Mathf.Lerp(startScale, 1f, eased);
            buttonsRoot.transform.localScale = buttonsOriginalScale * scale;

            if (buttonsCanvasGroup != null)
                buttonsCanvasGroup.alpha = useFade ? Mathf.Lerp(0f, 1f, eased) : 1f;

            yield return null;
        }

        buttonsRoot.transform.localScale = buttonsOriginalScale;

        if (buttonsCanvasGroup != null)
        {
            buttonsCanvasGroup.alpha = 1f;
            buttonsCanvasGroup.interactable = true;
            buttonsCanvasGroup.blocksRaycasts = true;
        }

        showRoutine = null;
    }

    /// <summary>
    /// Cubic easing used for the smooth button return animation.
    /// </summary>
    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    // -------------------------------------------------------------------------
    // Button Actions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Opens or closes the LEGO block menu.
    /// </summary>
    private void OnBlockMenuPressed()
    {
        if (blockMenu == null)
            return;

        blockMenu.SetActive(!blockMenu.activeSelf);
    }

    /// <summary>
    /// Opens or closes the music player panel.
    /// </summary>
    private void OnMusicPlayerPressed()
    {
        if (musicPlayerPanel == null)
            return;

        musicPlayerPanel.SetActive(!musicPlayerPanel.activeSelf);
    }

    /// <summary>
    /// Opens or closes the main menu panel.
    /// </summary>
    private void OnMainMenuPressed()
    {
        if (mainMenuPanel == null)
            return;

        mainMenuPanel.SetActive(!mainMenuPanel.activeSelf);
    }
}
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Three wrist buttons on the left controller.
/// Buttons hide when any panel is open or closing.
/// When all panels are closed, the buttons smoothly appear again.
/// </summary>
public class LegoWristButtons : MonoBehaviour
{
    [Header("Button References")]
    public Button blockMenuButton;
    public Button musicPlayerButton;
    public Button mainMenuButton;

    [Header("Button Icons")]
    public Sprite blockMenuIcon;
    public Sprite musicPlayerIcon;
    public Sprite mainMenuIcon;

    [Header("Panels to Open")]
    public LegoHandMenu blockMenu;
    public GameObject musicPlayerPanel;
    public GameObject mainMenuPanel;

    [Header("Buttons Root")]
    [Tooltip("The GameObject that contains all three buttons. Gets hidden when a panel is open or closing.")]
    public GameObject buttonsRoot;

    [Header("Buttons Return Animation")]
    [Tooltip("How long the wrist buttons smooth-in animation takes.")]
    public float showDuration = 0.28f;

    [Tooltip("Start scale for the smooth-in animation. 0.85 = subtle, 0 = full pop from nothing.")]
    public float startScale = 0.85f;

    [Tooltip("If enabled, the buttons fade in softly. This does NOT disable/interactable-grey the buttons.")]
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
        if (buttonsRoot != null)
        {
            buttonsOriginalScale = buttonsRoot.transform.localScale;

            if (useFade)
            {
                buttonsCanvasGroup = buttonsRoot.GetComponent<CanvasGroup>();

                if (buttonsCanvasGroup == null)
                    buttonsCanvasGroup = buttonsRoot.AddComponent<CanvasGroup>();

                // Important:
                // Never set interactable/blocksRaycasts to false during show,
                // because that can make Unity buttons look grey/disabled.
                buttonsCanvasGroup.interactable = true;
                buttonsCanvasGroup.blocksRaycasts = true;
            }
        }
    }

    private void Start()
    {
        SetupButton(blockMenuButton, blockMenuIcon, OnBlockMenuPressed);
        SetupButton(musicPlayerButton, musicPlayerIcon, OnMusicPlayerPressed);
        SetupButton(mainMenuButton, mainMenuIcon, OnMainMenuPressed);

        ForceRefreshVisibility();
    }

    private void Update()
    {
        UpdateButtonsVisibility();
    }

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------

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

    private bool AnyPanelOpenOrClosing()
    {
        return IsOpenOrClosing(blockMenu) ||
               IsOpen(musicPlayerPanel) ||
               IsOpen(mainMenuPanel);
    }

    private bool IsOpenOrClosing(LegoHandMenu menu)
    {
        if (menu == null)
            return false;

        return menu.IsOpenOrClosing;
    }

    private bool IsOpen(GameObject panel)
    {
        return panel != null && panel.activeSelf;
    }

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

    private void ShowButtonsSmooth()
    {
        if (buttonsRoot == null)
            return;

        if (showRoutine != null)
            StopCoroutine(showRoutine);

        buttonsRoot.SetActive(true);
        showRoutine = StartCoroutine(ShowButtonsRoutine());
    }

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

    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    // -------------------------------------------------------------------------
    // Button Actions
    // -------------------------------------------------------------------------

    private void OnBlockMenuPressed()
    {
        if (blockMenu == null)
            return;

        blockMenu.SetMenuOpen(!blockMenu.IsOpenOrClosing);
    }

    private void OnMusicPlayerPressed()
    {
        if (musicPlayerPanel == null)
            return;

        musicPlayerPanel.SetActive(!musicPlayerPanel.activeSelf);
    }

    private void OnMainMenuPressed()
    {
        if (mainMenuPanel == null)
            return;

        mainMenuPanel.SetActive(!mainMenuPanel.activeSelf);
    }
}
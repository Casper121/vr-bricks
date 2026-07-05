using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Prevents one or more hand menus from being used while the right hand is
/// holding a block.
///
/// When a block is held, every assigned menu becomes non-interactable and can
/// optionally fade to communicate that its buttons are temporarily blocked.
///
/// You can either:
/// - Put ONE instance of this script somewhere in the scene and drag ALL of
///   your menu Canvases (Block Menu, Music Menu, Settings Menu, Room Menu,
///   ...) into the "Menu Canvases" list below, or
/// - Put a separate instance on each individual menu panel, same as before -
///   both work, and you can mix and match.
/// </summary>
public class LegoMenuBlockHeldGuard : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Menu References
    // -------------------------------------------------------------------------

    [Header("Menus")]
    [Tooltip("Every Canvas listed here gets faded/blocked while a block is held. Add as many menus as you like - Block Menu, Music Menu, Settings Menu, Room Menu, etc.")]
    [SerializeField] private List<Canvas> menuCanvases = new List<Canvas>();

    [Header("Right Hand Interactor")]
    [SerializeField] private XRBaseInteractor rightInteractor;

    // -------------------------------------------------------------------------
    // Inspector: Fade Settings
    // -------------------------------------------------------------------------

    [Header("Fade")]
    [SerializeField] private bool fadeMenuWhenBlocked = true;
    [SerializeField] private float normalAlpha = 1f;
    [SerializeField] private float blockedAlpha = 0.35f;
    [SerializeField] private float fadeSpeed = 12f;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private readonly List<CanvasGroup> canvasGroups = new List<CanvasGroup>();
    private bool lastBlockedState;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Backward compatible auto-detect: if nothing was assigned in the
        // Inspector at all, fall back to whatever single Canvas sits under this
        // GameObject - exactly like the original single-menu behavior.
        if (menuCanvases.Count == 0)
        {
            Canvas foundCanvas = GetComponentInChildren<Canvas>(true);

            if (foundCanvas != null)
                menuCanvases.Add(foundCanvas);
        }

        for (int i = 0; i < menuCanvases.Count; i++)
        {
            Canvas menuCanvas = menuCanvases[i];

            if (menuCanvas == null)
                continue;

            CanvasGroup canvasGroup = menuCanvas.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = menuCanvas.gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = normalAlpha;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            canvasGroups.Add(canvasGroup);
        }
    }

    private void Update()
    {
        if (canvasGroups.Count == 0 || rightInteractor == null)
            return;

        bool blockHeld = rightInteractor.hasSelection;

        UpdateInteractivity(blockHeld);
        UpdateFade(blockHeld);
    }

    // -------------------------------------------------------------------------
    // Internal Logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enables or disables menu interaction (for every managed menu) when the
    /// held-block state changes.
    /// </summary>
    private void UpdateInteractivity(bool blockHeld)
    {
        if (blockHeld == lastBlockedState)
            return;

        for (int i = 0; i < canvasGroups.Count; i++)
        {
            CanvasGroup canvasGroup = canvasGroups[i];

            if (canvasGroup == null)
                continue;

            canvasGroup.interactable = !blockHeld;
            canvasGroup.blocksRaycasts = !blockHeld;
        }

        lastBlockedState = blockHeld;
    }

    /// <summary>
    /// Smoothly fades every managed menu while it is blocked, if fading is enabled.
    /// </summary>
    private void UpdateFade(bool blockHeld)
    {
        float targetAlpha = fadeMenuWhenBlocked
            ? (blockHeld ? blockedAlpha : normalAlpha)
            : normalAlpha;

        for (int i = 0; i < canvasGroups.Count; i++)
        {
            CanvasGroup canvasGroup = canvasGroups[i];

            if (canvasGroup == null)
                continue;

            canvasGroup.alpha = Mathf.Lerp(
                canvasGroup.alpha,
                targetAlpha,
                Time.deltaTime * fadeSpeed
            );
        }
    }
}
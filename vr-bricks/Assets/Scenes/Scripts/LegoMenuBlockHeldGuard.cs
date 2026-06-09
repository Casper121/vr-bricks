using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Prevents the hand menu from being used while the right hand is holding a block.
/// 
/// When a block is held, the menu becomes non-interactable and can optionally fade
/// to communicate that menu buttons are temporarily blocked.
/// </summary>
public class LegoMenuBlockHeldGuard : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Menu References
    // -------------------------------------------------------------------------

    [Header("Menu")]
    [SerializeField] private Canvas menuCanvas;

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

    private CanvasGroup canvasGroup;
    private bool lastBlockedState;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (menuCanvas == null)
            menuCanvas = GetComponentInChildren<Canvas>(true);

        if (menuCanvas != null)
        {
            canvasGroup = menuCanvas.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = menuCanvas.gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = normalAlpha;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void Update()
    {
        if (canvasGroup == null || rightInteractor == null)
            return;

        bool blockHeld = rightInteractor.hasSelection;

        UpdateInteractivity(blockHeld);
        UpdateFade(blockHeld);
    }

    // -------------------------------------------------------------------------
    // Internal Logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enables or disables menu interaction when the held-block state changes.
    /// </summary>
    private void UpdateInteractivity(bool blockHeld)
    {
        if (blockHeld == lastBlockedState)
            return;

        canvasGroup.interactable = !blockHeld;
        canvasGroup.blocksRaycasts = !blockHeld;
        lastBlockedState = blockHeld;
    }

    /// <summary>
    /// Smoothly fades the menu while it is blocked, if fading is enabled.
    /// </summary>
    private void UpdateFade(bool blockHeld)
    {
        if (!fadeMenuWhenBlocked)
        {
            canvasGroup.alpha = normalAlpha;
            return;
        }

        float targetAlpha = blockHeld ? blockedAlpha : normalAlpha;

        canvasGroup.alpha = Mathf.Lerp(
            canvasGroup.alpha,
            targetAlpha,
            Time.deltaTime * fadeSpeed
        );
    }
}
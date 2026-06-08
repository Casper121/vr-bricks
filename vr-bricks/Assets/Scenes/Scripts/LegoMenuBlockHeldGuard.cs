using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class LegoMenuBlockHeldGuard : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField] private Canvas menuCanvas;

    [Header("Right Hand Interactor")]
    [SerializeField] private XRBaseInteractor rightInteractor;

    [Header("Fade")]
    [SerializeField] private bool fadeMenuWhenBlocked = true;
    [SerializeField] private float normalAlpha = 1f;
    [SerializeField] private float blockedAlpha = 0.35f;
    [SerializeField] private float fadeSpeed = 12f;

    private CanvasGroup canvasGroup;
    private bool lastBlockedState;

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

        if (blockHeld != lastBlockedState)
        {
            canvasGroup.interactable = !blockHeld;
            canvasGroup.blocksRaycasts = !blockHeld;
            lastBlockedState = blockHeld;
        }

        if (fadeMenuWhenBlocked)
        {
            float targetAlpha = blockHeld ? blockedAlpha : normalAlpha;

            canvasGroup.alpha = Mathf.Lerp(
                canvasGroup.alpha,
                targetAlpha,
                Time.deltaTime * fadeSpeed
            );
        }
        else
        {
            canvasGroup.alpha = normalAlpha;
        }
    }
}
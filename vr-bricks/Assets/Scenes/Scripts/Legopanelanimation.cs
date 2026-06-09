using UnityEngine;
using System.Collections;

/// <summary>
/// Smooth open and close animation for UI panels.
/// 
/// Attach this script to the same GameObject as the Canvas,
/// or to a child object inside the Canvas.
/// 
/// The animation scales the panel in and out. Optional fading can be enabled,
/// but interaction is kept active so Unity buttons do not appear grey/disabled.
/// </summary>
public class LegoPanelAnimation : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Open Animation
    // -------------------------------------------------------------------------

    [Header("Open Animation")]
    public float duration = 0.35f;
    public float startScale = 0.85f;

    // -------------------------------------------------------------------------
    // Inspector: Close Animation
    // -------------------------------------------------------------------------

    [Header("Close Animation")]
    public float closeDuration = 0.22f;
    public float closeEndScale = 0.85f;

    // -------------------------------------------------------------------------
    // Inspector: Fade
    // -------------------------------------------------------------------------

    [Header("Fade")]
    [Tooltip("Optional fade. Disable this if UI elements look grey while opening.")]
    public bool useFade = false;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private Vector3 originalScale;
    private CanvasGroup canvasGroup;
    private Coroutine currentRoutine;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        originalScale = transform.localScale;
        PrepareCanvasGroup();
    }

    private void OnEnable()
    {
        PlayOpen();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the open animation.
    /// </summary>
    public void PlayOpen()
    {
        if (!gameObject.activeInHierarchy)
            return;

        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(OpenRoutine());
    }

    /// <summary>
    /// Starts the close animation and calls the callback when it finishes.
    /// </summary>
    public void PlayClose(System.Action onFinished)
    {
        if (!gameObject.activeInHierarchy)
        {
            if (onFinished != null)
                onFinished.Invoke();

            return;
        }

        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(CloseRoutine(onFinished));
    }

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates and configures a CanvasGroup when fading is enabled.
    /// </summary>
    private void PrepareCanvasGroup()
    {
        if (!useFade)
            return;

        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Important:
        // Do not disable interactable/blocksRaycasts during opening,
        // because Unity buttons can look grey/disabled because of that.
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    // -------------------------------------------------------------------------
    // Animation Routines
    // -------------------------------------------------------------------------

    /// <summary>
    /// Animates the panel from small to full size.
    /// </summary>
    private IEnumerator OpenRoutine()
    {
        transform.localScale = originalScale * startScale;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = useFade ? 0f : 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);

            float scale = Mathf.Lerp(startScale, 1f, eased);
            transform.localScale = originalScale * scale;

            if (canvasGroup != null)
                canvasGroup.alpha = useFade ? Mathf.Lerp(0f, 1f, eased) : 1f;

            yield return null;
        }

        transform.localScale = originalScale;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        currentRoutine = null;
    }

    /// <summary>
    /// Animates the panel closed and invokes the finished callback.
    /// </summary>
    private IEnumerator CloseRoutine(System.Action onFinished)
    {
        Vector3 startScaleVector = transform.localScale;
        Vector3 endScaleVector = originalScale * closeEndScale;

        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        float elapsed = 0f;

        while (elapsed < closeDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / closeDuration);
            float eased = EaseInCubic(t);

            transform.localScale = Vector3.Lerp(startScaleVector, endScaleVector, eased);

            if (canvasGroup != null)
                canvasGroup.alpha = useFade ? Mathf.Lerp(startAlpha, 0f, eased) : 1f;

            yield return null;
        }

        transform.localScale = originalScale;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        currentRoutine = null;

        if (onFinished != null)
            onFinished.Invoke();
    }

    // -------------------------------------------------------------------------
    // Easing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cubic easing for opening animations.
    /// </summary>
    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    /// <summary>
    /// Cubic easing for closing animations.
    /// </summary>
    private float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}
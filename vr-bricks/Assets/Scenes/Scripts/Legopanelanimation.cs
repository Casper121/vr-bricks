using UnityEngine;
using System.Collections;

/// <summary>
/// Smooth open and close animation for UI panels.
/// Attach this to the same GameObject as the Canvas,
/// or to a child object inside the Canvas.
/// </summary>
public class LegoPanelAnimation : MonoBehaviour
{
    [Header("Open Animation")]
    public float duration = 0.35f;
    public float startScale = 0.85f;

    [Header("Close Animation")]
    public float closeDuration = 0.22f;
    public float closeEndScale = 0.85f;

    [Header("Fade")]
    [Tooltip("Optional fade. Disable this if UI elements look grey while opening.")]
    public bool useFade = false;

    private Vector3 originalScale;
    private CanvasGroup canvasGroup;
    private Coroutine currentRoutine;

    private void Awake()
    {
        originalScale = transform.localScale;

        if (useFade)
        {
            canvasGroup = GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // Important:
            // Do not disable interactable/blocksRaycasts during opening,
            // because Unity buttons can look grey/disabled because of that.
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void OnEnable()
    {
        PlayOpen();
    }

    public void PlayOpen()
    {
        if (!gameObject.activeInHierarchy)
            return;

        if (currentRoutine != null)
            StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(OpenRoutine());
    }

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

    private float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}
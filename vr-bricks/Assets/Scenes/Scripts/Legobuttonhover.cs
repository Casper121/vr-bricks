using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Adds a simple hover and press animation to a Unity UI Button.
/// 
/// Modes:
/// - ScaleOnly: scales the button on hover and press.
/// - Darken: scales the button and shows a dark overlay while hovered.
/// </summary>
[RequireComponent(typeof(Button))]
public class LegoButtonHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    public enum HoverMode
    {
        ScaleOnly,
        Darken
    }

    // -------------------------------------------------------------------------
    // Inspector: Mode
    // -------------------------------------------------------------------------

    [Header("Mode")]
    public HoverMode mode = HoverMode.ScaleOnly;

    // -------------------------------------------------------------------------
    // Inspector: Scale Animation
    // -------------------------------------------------------------------------

    [Header("Scale")]
    public float hoverScale = 1.1f;
    public float pressScale = 0.92f;
    public float animSpeed = 14f;

    // -------------------------------------------------------------------------
    // Inspector: Darken Overlay
    // -------------------------------------------------------------------------

    [Header("Darken (only if mode = Darken)")]
    [Tooltip("Alpha of black overlay on hover.")]
    public float darkenAlpha = 0.25f;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private Vector3 baseScale;
    private float currentScale = 1f;
    private bool hovered;
    private bool pressed;
    private Image overlay;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds or reuses a LegoButtonHover component on the given GameObject.
    /// </summary>
    public static LegoButtonHover AddTo(GameObject go, HoverMode mode = HoverMode.ScaleOnly)
    {
        LegoButtonHover hover = go.GetComponent<LegoButtonHover>();

        if (hover == null)
            hover = go.AddComponent<LegoButtonHover>();

        hover.mode = mode;
        return hover;
    }

    /// <summary>
    /// Reserved for compatibility with menu color updates.
    /// </summary>
    public void RefreshCache()
    {
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        baseScale = transform.localScale;
        CreateOverlay();
    }

    private void Update()
    {
        float target = pressed ? pressScale : hovered ? hoverScale : 1f;

        currentScale = Mathf.Lerp(
            currentScale,
            target,
            Time.deltaTime * animSpeed
        );

        transform.localScale = baseScale * currentScale;
    }

    // -------------------------------------------------------------------------
    // Pointer Events
    // -------------------------------------------------------------------------

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        SetOverlayVisible(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        SetOverlayVisible(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
    }

    // -------------------------------------------------------------------------
    // Internal Logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the optional hover overlay used by Darken mode.
    /// </summary>
    private void CreateOverlay()
    {
        GameObject overlayObject = new GameObject("HoverOverlay");
        overlayObject.transform.SetParent(transform, false);
        overlayObject.transform.SetAsLastSibling();

        RectTransform rectTransform = overlayObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        overlay = overlayObject.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0f);
        overlay.raycastTarget = false;
    }

    /// <summary>
    /// Shows or hides the dark overlay depending on the current hover mode.
    /// </summary>
    private void SetOverlayVisible(bool visible)
    {
        if (overlay == null)
            return;

        if (mode != HoverMode.Darken)
        {
            overlay.color = new Color(0f, 0f, 0f, 0f);
            return;
        }

        overlay.color = visible
            ? new Color(0f, 0f, 0f, darkenAlpha)
            : new Color(0f, 0f, 0f, 0f);
    }
}
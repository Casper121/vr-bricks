using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Hover/press animation. Configurable per button:
/// - ScaleOnly: just scale up/down, no color change
/// - Darken: scale + dark overlay on hover (for color slots)
/// </summary>
[RequireComponent(typeof(Button))]
public class LegoButtonHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    public enum HoverMode { ScaleOnly, Darken }

    [Header("Mode")]
    public HoverMode mode = HoverMode.ScaleOnly;

    [Header("Scale")]
    public float hoverScale = 1.1f;
    public float pressScale = 0.92f;
    public float animSpeed = 14f;

    [Header("Darken (only if mode = Darken)")]
    [Tooltip("Alpha of black overlay on hover.")]
    public float darkenAlpha = 0.25f;

    // -------------------------------------------------------------------------
    // Runtime
    // -------------------------------------------------------------------------

    private Vector3 baseScale;
    private float currentScale = 1f;
    private bool hovered;
    private bool pressed;
    private Image overlay;

    public static LegoButtonHover AddTo(GameObject go, HoverMode mode = HoverMode.ScaleOnly)
    {
        var h = go.GetComponent<LegoButtonHover>();
        if (h == null) h = go.AddComponent<LegoButtonHover>();
        h.mode = mode;
        return h;
    }

    private void Awake()
    {
        baseScale = transform.localScale;
        CreateOverlay();
    }

    private void Update()
    {
        float target = pressed ? pressScale : hovered ? hoverScale : 1f;
        currentScale = Mathf.Lerp(currentScale, target, Time.deltaTime * animSpeed);
        transform.localScale = baseScale * currentScale;
    }

    private void CreateOverlay()
    {
        GameObject ov = new GameObject("HoverOverlay");
        ov.transform.SetParent(transform, false);
        ov.transform.SetAsLastSibling();

        RectTransform rt = ov.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        overlay = ov.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0f);
        overlay.raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        hovered = true;
        if (mode == HoverMode.Darken && overlay != null)
            overlay.color = new Color(0f, 0f, 0f, darkenAlpha);
    }

    public void OnPointerExit(PointerEventData _)
    {
        hovered = false;
        pressed = false;
        if (overlay != null)
            overlay.color = new Color(0f, 0f, 0f, 0f);
    }

    public void OnPointerDown(PointerEventData _) => pressed = true;
    public void OnPointerUp(PointerEventData _) => pressed = false;

    public void RefreshCache() { }
}
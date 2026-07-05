using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple, low-cost alternative to dragging a live scale slider.
/// 
/// Instead of continuously firing ApplyScale() 30-50x/second while dragging (which is
/// what made things heavy), this steps through a small list of FIXED scale values, one
/// step per button press. All the actual scaling/centering/grid-growth logic is reused
/// unchanged from LegoScaleMenu.ApplyScale() - this script is just a cheaper trigger for it.
/// 
/// Setup:
/// - Assign your existing LegoScaleMenu component.
/// - Assign two buttons (Scale Up / Scale Down) and optionally a label.
/// - You can leave LegoScaleMenu's own Scale Slider field EMPTY - it's optional there.
/// </summary>
public class LegoDiscreteScaleButtons : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LegoScaleMenu scaleMenu;

    [Header("Buttons")]
    [SerializeField] private Button scaleUpButton;
    [SerializeField] private Button scaleDownButton;

    [Header("UI (optional)")]
    [SerializeField] private TMP_Text scaleLabel;

    [Header("Preset Scale Steps")]
    [Tooltip("The fixed scale values the player can step through. Keep this list short (5-8 entries) for a snappy, simple feel.")]
    [SerializeField] private float[] presetScales = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };

    [Tooltip("Index into presetScales used as the starting scale. 1f should normally sit at this index.")]
    [SerializeField] private int startIndex = 2;

    private int currentIndex;

    private void Start()
    {
        currentIndex = Mathf.Clamp(startIndex, 0, presetScales.Length - 1);

        if (scaleUpButton != null)
            scaleUpButton.onClick.AddListener(ScaleUp);

        if (scaleDownButton != null)
            scaleDownButton.onClick.AddListener(ScaleDown);

        UpdateLabel();
        UpdateButtonInteractivity();
    }

    private void OnDestroy()
    {
        if (scaleUpButton != null)
            scaleUpButton.onClick.RemoveListener(ScaleUp);

        if (scaleDownButton != null)
            scaleDownButton.onClick.RemoveListener(ScaleDown);
    }

    // ---------------------------------------------------------------------
    // Public API - wire these to your wrist/menu buttons directly if you
    // prefer not to use the Scale Up/Down button fields above.
    // ---------------------------------------------------------------------

    public void ScaleUp()
    {
        if (currentIndex >= presetScales.Length - 1)
            return;

        currentIndex++;
        Apply();
    }

    public void ScaleDown()
    {
        if (currentIndex <= 0)
            return;

        currentIndex--;
        Apply();
    }

    public void SetScaleIndex(int index)
    {
        currentIndex = Mathf.Clamp(index, 0, presetScales.Length - 1);
        Apply();
    }

    // ---------------------------------------------------------------------
    // Internal
    // ---------------------------------------------------------------------

    private void Apply()
    {
        if (scaleMenu == null)
        {
            Debug.LogWarning("LegoDiscreteScaleButtons: No LegoScaleMenu assigned.", this);
            return;
        }

        // Single, one-shot call - NOT called every frame, so this is cheap even with a
        // large socket grid. All snapping/centering/grid-growth logic from LegoScaleMenu
        // still applies exactly as before.
        scaleMenu.ApplyScale(presetScales[currentIndex]);

        UpdateLabel();
        UpdateButtonInteractivity();
    }

    private void UpdateLabel()
    {
        if (scaleLabel != null)
            scaleLabel.text = $"{presetScales[currentIndex]:F2}x";
    }

    private void UpdateButtonInteractivity()
    {
        if (scaleUpButton != null)
            scaleUpButton.interactable = currentIndex < presetScales.Length - 1;

        if (scaleDownButton != null)
            scaleDownButton.interactable = currentIndex > 0;
    }
}
using UnityEngine;
using UnityEngine.UI;

public class LegoScaleMenu : MonoBehaviour
{
     [Header("UI References")]
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private Text scaleLabel;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;

    [Header("Configuration")]
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 4.0f;
    [SerializeField] private float defaultScale = 1.0f;

    private LegoBlock[] cachedBlocks;

    private void Awake()
    {
        scaleSlider.minValue = minScale;
        scaleSlider.maxValue = maxScale;
        scaleSlider.value    = defaultScale;

        scaleSlider.onValueChanged.AddListener(OnSliderChanged);
        applyButton.onClick.AddListener(ApplyScale);
        resetButton.onClick.AddListener(ResetScale);

        UpdateLabel(defaultScale);
    }

    [System.Obsolete]
    public void OnMenuOpened()
    {
        cachedBlocks = FindObjectsByType<LegoBlock>(FindObjectsSortMode.None);
    }

    private void OnSliderChanged(float value)
    {
        UpdateLabel(value);
        ApplyScaleToAllBlocks(value);
    }

    private void ApplyScale()
    {
        ApplyScaleToAllBlocks(scaleSlider.value);
    }

    private void ResetScale()
    {
        scaleSlider.value = defaultScale;
    }

    private void ApplyScaleToAllBlocks(float uniformScale)
    {
        if (cachedBlocks == null) return;
        foreach (LegoBlock block in cachedBlocks)
            block.transform.localScale = Vector3.one * uniformScale;
    }

    private void UpdateLabel(float value)
    {
        if (scaleLabel != null)
            scaleLabel.text = $"{value:F1}×";
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LegoScaleMenu : MonoBehaviour
{
     [Header("UI References")]
    [SerializeField] public Canvas menuCanvas;
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private TMP_Text scaleLabel;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;

    [Header("Configuration")]
    [SerializeField] private float minScale = 0.1f;
    [SerializeField] private float maxScale = 4.0f;
    [SerializeField] private float defaultScale = 1.0f;

    private LegoBlock[] cachedBlocks;

    private void Awake()
    {
        if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);

        scaleSlider.minValue = minScale;
        scaleSlider.maxValue = maxScale;
        scaleSlider.value    = defaultScale;

        scaleSlider.onValueChanged.AddListener(OnSliderChanged);
        applyButton.onClick.AddListener(ApplyScale);
        resetButton.onClick.AddListener(ResetScale);

        UpdateLabel(defaultScale);
    }


    public void OnEnable()
    {
        cachedBlocks = FindObjectsByType<LegoBlock>(FindObjectsSortMode.None);
    }

    public void OnDisable()
    {
        gameObject.SetActive(false);
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

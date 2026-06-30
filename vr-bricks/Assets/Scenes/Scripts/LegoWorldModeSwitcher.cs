using UnityEngine;

public class LegoWorldModeSwitcher : MonoBehaviour
{
    public enum LegoWorldMode
    {
        Virtual,
        MixedReality
    }

    [Header("Start Mode")]
    [SerializeField] private LegoWorldMode startMode = LegoWorldMode.Virtual;

    [Header("World Roots")]
    [SerializeField] private GameObject virtualWorldRoot;
    [SerializeField] private GameObject mixedRealityWorldRoot;

    [Header("Optional")]
    [Tooltip("If assigned, this object is only active in Virtual Mode.")]
    [SerializeField] private GameObject virtualOnlyCameraOrRig;

    [Tooltip("If assigned, this object is only active in Mixed Reality Mode.")]
    [SerializeField] private GameObject mixedRealityOnlyCameraOrRig;

    private LegoWorldMode currentMode;

    private void Start()
    {
        SetMode(startMode);
    }

    [ContextMenu("Set Virtual Mode")]
    public void SetVirtualMode()
    {
        SetMode(LegoWorldMode.Virtual);
    }

    [ContextMenu("Set Mixed Reality Mode")]
    public void SetMixedRealityMode()
    {
        SetMode(LegoWorldMode.MixedReality);
    }

    [ContextMenu("Toggle Mode")]
    public void ToggleMode()
    {
        if (currentMode == LegoWorldMode.Virtual)
            SetMode(LegoWorldMode.MixedReality);
        else
            SetMode(LegoWorldMode.Virtual);
    }

    public void SetMode(LegoWorldMode mode)
    {
        currentMode = mode;

        bool virtualActive = mode == LegoWorldMode.Virtual;
        bool mixedRealityActive = mode == LegoWorldMode.MixedReality;

        if (virtualWorldRoot != null)
            virtualWorldRoot.SetActive(virtualActive);

        if (mixedRealityWorldRoot != null)
            mixedRealityWorldRoot.SetActive(mixedRealityActive);

        if (virtualOnlyCameraOrRig != null)
            virtualOnlyCameraOrRig.SetActive(virtualActive);

        if (mixedRealityOnlyCameraOrRig != null)
            mixedRealityOnlyCameraOrRig.SetActive(mixedRealityActive);

        Debug.Log("LegoWorldModeSwitcher: Mode changed to " + mode);
    }
}
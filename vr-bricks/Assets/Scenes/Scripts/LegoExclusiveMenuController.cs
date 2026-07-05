using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Simple and strict menu switcher:
/// - If the requested panel is already open: close it with animation.
/// - If another panel is open: hide all others immediately, then open requested panel.
/// - Settings opens with N.
/// - Music opens with L.
///
/// Buttons should call this controller. LegoWristButtons already calls ToggleMusicPanel().
/// </summary>
public class LegoExclusiveMenuController : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("Your block menu object. In your scene this is usually MenuCanvas.")]
    [SerializeField] private GameObject blockMenu;

    [Tooltip("Your visible settings panel object. In your scene this is usually SettingsMenu.")]
    [SerializeField] private GameObject settingsPanel;

    [Tooltip("Optional. Leave empty if you do not have one.")]
    [SerializeField] private GameObject mainPanel;

    [Tooltip("Your music menu panel root. Assign your MusicMenu here.")]
    [SerializeField] private GameObject musicPanel;

    [Header("Keyboard")]
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField] private Key settingsToggleKey = Key.N;
    [SerializeField] private Key musicToggleKey = Key.L;

    private readonly List<GameObject> panels = new List<GameObject>();

    private void Awake()
    {
        RebuildPanelList();
        CloseAllImmediate();
    }

    private void Update()
    {
        if (!allowKeyboardFallback || Keyboard.current == null)
            return;

        KeyControl settingsKey = Keyboard.current[settingsToggleKey];
        if (settingsKey != null && settingsKey.wasPressedThisFrame)
            ToggleSettingsPanel();

        KeyControl musicKey = Keyboard.current[musicToggleKey];
        if (musicKey != null && musicKey.wasPressedThisFrame)
            ToggleMusicPanel();
    }

    private void RebuildPanelList()
    {
        panels.Clear();

        AddPanel(blockMenu);
        AddPanel(settingsPanel);
        AddPanel(mainPanel);
        AddPanel(musicPanel);
    }

    private void AddPanel(GameObject panel)
    {
        if (panel != null && !panels.Contains(panel))
            panels.Add(panel);
    }

    // ---------------------------------------------------------------------
    // Public UI API
    // ---------------------------------------------------------------------

    public void ToggleBlockMenu()
    {
        TogglePanel(blockMenu);
    }

    public void ToggleSettingsPanel()
    {
        TogglePanel(settingsPanel);
    }

    public void ToggleMainPanel()
    {
        TogglePanel(mainPanel);
    }

    public void ToggleMusicPanel()
    {
        TogglePanel(musicPanel);
    }

    public void CloseBlockMenu()
    {
        ClosePanelAnimated(blockMenu);
    }

    public void CloseSettingsPanel()
    {
        ClosePanelAnimated(settingsPanel);
    }

    public void CloseMainPanel()
    {
        ClosePanelAnimated(mainPanel);
    }

    public void CloseMusicPanel()
    {
        ClosePanelAnimated(musicPanel);
    }

    public void CloseAllPanels()
    {
        for (int i = 0; i < panels.Count; i++)
            ClosePanelAnimated(panels[i]);
    }

    // ---------------------------------------------------------------------
    // Core
    // ---------------------------------------------------------------------

    private void TogglePanel(GameObject target)
    {
        if (target == null)
        {
            Debug.LogWarning("LegoExclusiveMenuController: Target panel is not assigned.", this);
            return;
        }

        RebuildPanelList();

        if (IsOpen(target))
        {
            ClosePanelAnimated(target);
            return;
        }

        CloseAllExceptImmediate(target);
        OpenPanelAnimated(target);
    }

    private bool IsOpen(GameObject panel)
    {
        if (panel == null)
            return false;

        LegoHandMenu handMenu = GetHandMenu(panel);

        if (handMenu != null)
            return handMenu.IsOpenOrClosing;

        return panel.activeSelf;
    }

    private void OpenPanelAnimated(GameObject panel)
    {
        if (panel == null)
            return;

        if (!panel.activeSelf)
            panel.SetActive(true);

        LegoHandMenu handMenu = GetHandMenu(panel);

        if (handMenu != null)
        {
            handMenu.SetMenuOpen(true);
            return;
        }

        LegoPanelAnimation animation = GetAnimation(panel);

        if (animation != null)
            animation.PlayOpen();
    }

    private void ClosePanelAnimated(GameObject panel)
    {
        if (panel == null)
            return;

        LegoHandMenu handMenu = GetHandMenu(panel);

        if (handMenu != null)
        {
            handMenu.SetMenuOpen(false);
            return;
        }

        if (!panel.activeSelf)
            return;

        LegoPanelAnimation animation = GetAnimation(panel);

        if (animation != null)
        {
            animation.PlayClose(() =>
            {
                if (panel != null)
                    panel.SetActive(false);
            });

            return;
        }

        panel.SetActive(false);
    }

    private void CloseAllExceptImmediate(GameObject keepOpen)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            GameObject panel = panels[i];

            if (panel == null || panel == keepOpen)
                continue;

            ClosePanelImmediate(panel);
        }
    }

    private void CloseAllImmediate()
    {
        for (int i = 0; i < panels.Count; i++)
            ClosePanelImmediate(panels[i]);
    }

    private void ClosePanelImmediate(GameObject panel)
    {
        if (panel == null)
            return;

        panel.SetActive(false);
    }

    private LegoHandMenu GetHandMenu(GameObject panel)
    {
        if (panel == null)
            return null;

        LegoHandMenu handMenu = panel.GetComponent<LegoHandMenu>();

        if (handMenu == null)
            handMenu = panel.GetComponentInChildren<LegoHandMenu>(true);

        return handMenu;
    }

    private LegoPanelAnimation GetAnimation(GameObject panel)
    {
        if (panel == null)
            return null;

        LegoPanelAnimation animation = panel.GetComponent<LegoPanelAnimation>();

        if (animation == null)
            animation = panel.GetComponentInChildren<LegoPanelAnimation>(true);

        return animation;
    }
}

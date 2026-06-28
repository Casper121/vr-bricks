using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// Two-panel menu controller with XRI InputActionReferences.
///
/// Editor:
/// - M toggles BlockMenu
/// - N toggles SettingsMenu
///
/// Meta Quest / XRI:
/// - Block Toggle Action = X button action
/// - Settings Toggle Action = Y button action
/// </summary>
public class LegoTwoPanelMenuController : MonoBehaviour
{
    [Header("Real visible menu roots")]
    [SerializeField] private GameObject blockMenuRoot;
    [SerializeField] private GameObject settingsMenuRoot;

    [Header("Wrist buttons")]
    [SerializeField] private Button blockMenuButton;
    [SerializeField] private Button settingsMenuButton;

    [Header("Close buttons")]
    [SerializeField] private Button blockCloseButton;
    [SerializeField] private Button settingsCloseButton;

    [Header("Meta Quest / XRI Input Actions")]
    [SerializeField] private InputActionReference blockToggleAction;
    [SerializeField] private InputActionReference settingsToggleAction;

    [Header("Editor Keyboard Fallback")]
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField] private Key blockToggleKey = Key.M;
    [SerializeField] private Key settingsToggleKey = Key.N;

    private void Awake()
    {
        WireButtons();
        ForceBothClosed();
    }

    private void OnEnable()
    {
        WireButtons();

        if (blockToggleAction != null)
            blockToggleAction.action.Enable();

        if (settingsToggleAction != null)
            settingsToggleAction.action.Enable();
    }

    private void OnDisable()
    {
        if (blockToggleAction != null)
            blockToggleAction.action.Disable();

        if (settingsToggleAction != null)
            settingsToggleAction.action.Disable();
    }

    private void Update()
    {
        if (WasBlockTogglePressed())
            ToggleBlockMenu();

        if (WasSettingsTogglePressed())
            ToggleSettingsMenu();
    }

    private bool WasBlockTogglePressed()
    {
        if (blockToggleAction != null && blockToggleAction.action.WasPressedThisFrame())
            return true;

        if (allowKeyboardFallback && Keyboard.current != null)
        {
            KeyControl key = Keyboard.current[blockToggleKey];

            if (key != null && key.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    private bool WasSettingsTogglePressed()
    {
        if (settingsToggleAction != null && settingsToggleAction.action.WasPressedThisFrame())
            return true;

        if (allowKeyboardFallback && Keyboard.current != null)
        {
            KeyControl key = Keyboard.current[settingsToggleKey];

            if (key != null && key.wasPressedThisFrame)
                return true;
        }

        return false;
    }

    private void WireButtons()
    {
        if (blockMenuButton != null)
        {
            blockMenuButton.onClick.RemoveAllListeners();
            blockMenuButton.onClick.AddListener(ToggleBlockMenu);
        }

        if (settingsMenuButton != null)
        {
            settingsMenuButton.onClick.RemoveAllListeners();
            settingsMenuButton.onClick.AddListener(ToggleSettingsMenu);
        }

        if (blockCloseButton != null)
        {
            blockCloseButton.onClick.RemoveAllListeners();
            blockCloseButton.onClick.AddListener(CloseBlockMenu);
        }

        if (settingsCloseButton != null)
        {
            settingsCloseButton.onClick.RemoveAllListeners();
            settingsCloseButton.onClick.AddListener(CloseSettingsMenu);
        }
    }

    public void ToggleBlockMenu()
    {
        if (blockMenuRoot == null)
        {
            Debug.LogError("Block Menu Root is not assigned.", this);
            return;
        }

        if (blockMenuRoot.activeSelf)
        {
            CloseBlockMenu();
            return;
        }

        ForceSettingsClosed();
        OpenBlockMenu();
    }

    public void ToggleSettingsMenu()
    {
        if (settingsMenuRoot == null)
        {
            Debug.LogError("Settings Menu Root is not assigned.", this);
            return;
        }

        if (settingsMenuRoot.activeSelf)
        {
            CloseSettingsMenu();
            return;
        }

        ForceBlockClosed();
        OpenSettingsMenu();
    }

    public void OpenBlockMenu()
    {
        if (blockMenuRoot == null)
            return;

        blockMenuRoot.SetActive(true);
        PlayOpenAnimation(blockMenuRoot);
    }

    public void OpenSettingsMenu()
    {
        if (settingsMenuRoot == null)
            return;

        settingsMenuRoot.SetActive(true);
        PlayOpenAnimation(settingsMenuRoot);
    }

    public void CloseBlockMenu()
    {
        CloseWithAnimation(blockMenuRoot);
    }

    public void CloseSettingsMenu()
    {
        CloseWithAnimation(settingsMenuRoot);
    }

    public void ForceBothClosed()
    {
        ForceBlockClosed();
        ForceSettingsClosed();
    }

    private void ForceBlockClosed()
    {
        if (blockMenuRoot != null)
            blockMenuRoot.SetActive(false);
    }

    private void ForceSettingsClosed()
    {
        if (settingsMenuRoot != null)
            settingsMenuRoot.SetActive(false);
    }

    private void PlayOpenAnimation(GameObject root)
    {
        LegoPanelAnimation animation = FindAnimation(root);

        if (animation != null)
            animation.PlayOpen();
    }

    private void CloseWithAnimation(GameObject root)
    {
        if (root == null || !root.activeSelf)
            return;

        LegoPanelAnimation animation = FindAnimation(root);

        if (animation != null)
        {
            animation.PlayClose(() =>
            {
                if (root != null)
                    root.SetActive(false);
            });

            return;
        }

        root.SetActive(false);
    }

    private LegoPanelAnimation FindAnimation(GameObject root)
    {
        if (root == null)
            return null;

        LegoPanelAnimation animation = root.GetComponent<LegoPanelAnimation>();

        if (animation == null)
            animation = root.GetComponentInChildren<LegoPanelAnimation>(true);

        return animation;
    }
}

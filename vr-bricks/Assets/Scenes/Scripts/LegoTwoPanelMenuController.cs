using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// Three-panel menu controller with XRI InputActionReferences.
///
/// Editor:
/// - M toggles BlockMenu
/// - N toggles SettingsMenu
/// - L toggles MusicMenu
///
/// This version fixes the music panel being visible on scene start while still
/// keeping the music logic alive. It does NOT deactivate the MusicMenu GameObject.
/// Instead it disables its Canvas / GraphicRaycaster and hides it with CanvasGroup.
/// That means LegoMusicMenuController.Update can continue running for auto-next.
/// </summary>
public class LegoTwoPanelMenuController : MonoBehaviour
{
    [Header("Real visible menu roots")]
    [SerializeField] private GameObject blockMenuRoot;
    [SerializeField] private GameObject settingsMenuRoot;
    [SerializeField] private GameObject musicMenuRoot;

    [Header("Wrist buttons")]
    [SerializeField] private Button blockMenuButton;
    [SerializeField] private Button settingsMenuButton;
    [SerializeField] private Button musicMenuButton;

    [Header("Close buttons")]
    [SerializeField] private Button blockCloseButton;
    [SerializeField] private Button settingsCloseButton;
    [SerializeField] private Button musicCloseButton;

    [Header("Meta Quest / XRI Input Actions")]
    [SerializeField] private InputActionReference blockToggleAction;
    [SerializeField] private InputActionReference settingsToggleAction;
    [SerializeField] private InputActionReference musicToggleAction;

    [Header("Editor Keyboard Fallback")]
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField] private Key blockToggleKey = Key.M;
    [SerializeField] private Key settingsToggleKey = Key.N;
    [SerializeField] private Key musicToggleKey = Key.L;

    [Header("Music Menu Behaviour")]
    [Tooltip("Keep true. The MusicMenu GameObject stays active so music logic keeps running while the panel is closed.")]
    [SerializeField] private bool keepMusicMenuAliveWhenClosed = true;

    [Tooltip("Keep true. Forces all menus closed/hidden on scene start.")]
    [SerializeField] private bool forceAllMenusClosedOnStart = true;

    [Tooltip("Extra safety: for the first frames after scene start the music panel is forced hidden again, in case another script opens/builds it in Start.")]
    [SerializeField] private int forceClosedStartFrames = 8;

    [Tooltip("When true, a closed alive MusicMenu disables all Canvas components below it. This is stronger than CanvasGroup alpha and fixes panels that stay visible anyway.")]
    [SerializeField] private bool disableMusicCanvasesWhenClosed = true;

    private int startHideFramesRemaining;

    private void Awake()
    {
        PrepareMusicMenuForHiddenMode();
        WireButtons();

        if (forceAllMenusClosedOnStart)
        {
            startHideFramesRemaining = Mathf.Max(1, forceClosedStartFrames);
            ForceAllClosed();
        }
    }

    private IEnumerator Start()
    {
        if (!forceAllMenusClosedOnStart)
            yield break;

        // Several UI scripts build/enable children in Start or after one frame.
        // Hide repeatedly for a few frames so the music menu cannot pop visible.
        int frames = Mathf.Max(1, forceClosedStartFrames);

        for (int i = 0; i < frames; i++)
        {
            ForceAllClosed();
            yield return null;
        }
    }

    private void OnEnable()
    {
        WireButtons();
        EnableAction(blockToggleAction);
        EnableAction(settingsToggleAction);
        EnableAction(musicToggleAction);
    }

    private void OnDisable()
    {
        DisableAction(blockToggleAction);
        DisableAction(settingsToggleAction);
        DisableAction(musicToggleAction);
    }

    private void Update()
    {
        if (startHideFramesRemaining > 0 && forceAllMenusClosedOnStart)
        {
            startHideFramesRemaining--;
            ForceAllClosed();
        }

        if (WasBlockTogglePressed())
            ToggleBlockMenu();

        if (WasSettingsTogglePressed())
            ToggleSettingsMenu();

        if (WasMusicTogglePressed())
            ToggleMusicMenu();
    }

    private void EnableAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
            actionReference.action.Enable();
    }

    private void DisableAction(InputActionReference actionReference)
    {
        if (actionReference != null && actionReference.action != null)
            actionReference.action.Disable();
    }

    private bool WasBlockTogglePressed()
    {
        return WasActionOrKeyPressed(blockToggleAction, blockToggleKey);
    }

    private bool WasSettingsTogglePressed()
    {
        return WasActionOrKeyPressed(settingsToggleAction, settingsToggleKey);
    }

    private bool WasMusicTogglePressed()
    {
        return WasActionOrKeyPressed(musicToggleAction, musicToggleKey);
    }

    private bool WasActionOrKeyPressed(InputActionReference actionReference, Key fallbackKey)
    {
        if (actionReference != null && actionReference.action != null && actionReference.action.WasPressedThisFrame())
            return true;

        if (!allowKeyboardFallback || Keyboard.current == null)
            return false;

        KeyControl key = Keyboard.current[fallbackKey];
        return key != null && key.wasPressedThisFrame;
    }

    private void WireButtons()
    {
        WireButton(blockMenuButton, ToggleBlockMenu);
        WireButton(settingsMenuButton, ToggleSettingsMenu);
        WireButton(musicMenuButton, ToggleMusicMenu);

        WireButton(blockCloseButton, CloseBlockMenu);
        WireButton(settingsCloseButton, CloseSettingsMenu);
        WireButton(musicCloseButton, CloseMusicMenu);
    }

    private void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    // ---------------------------------------------------------------------
    // Public UI API
    // ---------------------------------------------------------------------

    public void ToggleBlockMenu()
    {
        TogglePanel(blockMenuRoot, "Block Menu Root");
    }

    public void ToggleSettingsMenu()
    {
        TogglePanel(settingsMenuRoot, "Settings Menu Root");
    }

    public void ToggleMusicMenu()
    {
        TogglePanel(musicMenuRoot, "Music Menu Root");
    }

    // Compatibility for older wrist-button scripts that call ToggleMusicPanel.
    public void ToggleMusicPanel()
    {
        ToggleMusicMenu();
    }

    public void OpenBlockMenu()
    {
        OpenPanel(blockMenuRoot);
    }

    public void OpenSettingsMenu()
    {
        OpenPanel(settingsMenuRoot);
    }

    public void OpenMusicMenu()
    {
        OpenPanel(musicMenuRoot);
    }

    public void CloseBlockMenu()
    {
        CloseWithAnimation(blockMenuRoot);
    }

    public void CloseSettingsMenu()
    {
        CloseWithAnimation(settingsMenuRoot);
    }

    public void CloseMusicMenu()
    {
        CloseWithAnimation(musicMenuRoot);
    }

    public void ForceBothClosed()
    {
        ForceAllClosed();
    }

    public void ForceAllClosed()
    {
        ForcePanelClosed(blockMenuRoot);
        ForcePanelClosed(settingsMenuRoot);
        ForcePanelClosed(musicMenuRoot);
    }

    // ---------------------------------------------------------------------
    // Core
    // ---------------------------------------------------------------------

    private void TogglePanel(GameObject target, string targetName)
    {
        if (target == null)
        {
            Debug.LogWarning(targetName + " is not assigned.", this);
            return;
        }

        if (IsPanelOpen(target))
        {
            CloseWithAnimation(target);
            return;
        }

        ForceAllExceptClosed(target);
        OpenPanel(target);
    }

    private void OpenPanel(GameObject root)
    {
        if (root == null)
            return;

        root.SetActive(true);

        if (IsMusicMenu(root) && keepMusicMenuAliveWhenClosed)
            SetMusicPanelVisible(root, true);

        PlayOpenAnimation(root);
    }

    private void ForceAllExceptClosed(GameObject keepOpen)
    {
        if (blockMenuRoot != keepOpen)
            ForcePanelClosed(blockMenuRoot);

        if (settingsMenuRoot != keepOpen)
            ForcePanelClosed(settingsMenuRoot);

        if (musicMenuRoot != keepOpen)
            ForcePanelClosed(musicMenuRoot);
    }

    private void ForcePanelClosed(GameObject root)
    {
        if (root == null)
            return;

        if (IsMusicMenu(root) && keepMusicMenuAliveWhenClosed)
        {
            // Keep active so music logic keeps updating, but hide visuals strongly.
            root.SetActive(true);
            SetMusicPanelVisible(root, false);
            return;
        }

        root.SetActive(false);
    }

    private void CloseWithAnimation(GameObject root)
    {
        if (root == null || !IsPanelOpen(root))
            return;

        LegoPanelAnimation animation = FindAnimation(root);

        if (animation != null && !(IsMusicMenu(root) && keepMusicMenuAliveWhenClosed))
        {
            animation.PlayClose(() =>
            {
                if (root != null)
                    root.SetActive(false);
            });

            return;
        }

        // For the alive music menu, hide immediately. Close animation can leave
        // child Canvas/CanvasGroup values visible, so immediate hiding is safer.
        if (IsMusicMenu(root) && keepMusicMenuAliveWhenClosed)
            SetMusicPanelVisible(root, false);
        else
            root.SetActive(false);
    }

    private void PlayOpenAnimation(GameObject root)
    {
        LegoPanelAnimation animation = FindAnimation(root);

        if (animation != null)
            animation.PlayOpen();
    }

    private void PrepareMusicMenuForHiddenMode()
    {
        if (musicMenuRoot == null || !keepMusicMenuAliveWhenClosed)
            return;

        // Important: GameObject stays active so LegoMusicMenuController.Awake/Start/Update can run.
        musicMenuRoot.SetActive(true);
        EnsureCanvasGroup(musicMenuRoot);
        SetMusicPanelVisible(musicMenuRoot, false);
    }

    private bool IsMusicMenu(GameObject root)
    {
        return root != null && root == musicMenuRoot;
    }

    private bool IsPanelOpen(GameObject root)
    {
        if (root == null)
            return false;

        if (IsMusicMenu(root) && keepMusicMenuAliveWhenClosed)
        {
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);

            if (disableMusicCanvasesWhenClosed && canvases != null && canvases.Length > 0)
            {
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i] != null && canvases[i].enabled)
                        return true;
                }

                return false;
            }

            CanvasGroup canvasGroup = EnsureCanvasGroup(root);
            return root.activeSelf && canvasGroup != null && canvasGroup.alpha > 0.5f && canvasGroup.blocksRaycasts;
        }

        return root.activeSelf;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        if (root == null)
            return null;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = root.AddComponent<CanvasGroup>();

        return canvasGroup;
    }

    private void SetMusicPanelVisible(GameObject root, bool visible)
    {
        if (root == null)
            return;

        root.SetActive(true);

        CanvasGroup canvasGroup = EnsureCanvasGroup(root);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        // Strong hide/show for Canvas-based menus. This keeps the script alive but
        // disables rendering and UI raycasts.
        if (disableMusicCanvasesWhenClosed)
        {
            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);

            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null)
                    canvases[i].enabled = visible;
            }

            GraphicRaycaster[] raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);

            for (int i = 0; i < raycasters.Length; i++)
            {
                if (raycasters[i] != null)
                    raycasters[i].enabled = visible;
            }
        }

        // Fallback for non-Canvas child graphics.
        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);

        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].raycastTarget = visible;
        }
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

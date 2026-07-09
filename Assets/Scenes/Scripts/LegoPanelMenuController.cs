using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRNode = UnityEngine.XR.XRNode;

/// <summary>
/// Multi-panel menu controller.
///
/// Direct-code VR Mapping:
/// - Left Trigger = Block Menu
/// - Left X Button = Settings Menu
/// - Left Y Button = Music Menu
/// - Room buttons teleport player to rooms
/// - Left Joystick Up = Fly Up
/// - Left Joystick Down = Fly Down
///
/// Editor:
/// - M toggles BlockMenu
/// - N toggles SettingsMenu
/// - L toggles MusicMenu
/// - R toggles RoomMenu
/// - E flies up
/// - Q flies down
/// </summary>
public class LegoTwoPanelMenuController : MonoBehaviour
{
    [System.Serializable]
    public class RoomEntry
    {
        [Header("Room")]
        public string roomName = "Room";

        [Tooltip("Button with the room icon.")]
        public Button roomButton;

        [Tooltip("Target spawn point for this room.")]
        public Transform spawnPoint;
    }

    [Header("Real visible menu roots")]
    [SerializeField] private GameObject blockMenuRoot;
    [SerializeField] private GameObject settingsMenuRoot;
    [SerializeField] private GameObject musicMenuRoot;

    [Header("Room Menu")]
    [SerializeField] private GameObject roomMenuRoot;

    [Tooltip("Optional close button inside the room menu.")]
    [SerializeField] private Button roomCloseButton;

    [Tooltip("Neutral room spawn. Player starts here.")]
    [SerializeField] private Transform neutralRoomSpawn;

    [SerializeField] private List<RoomEntry> rooms = new List<RoomEntry>();

    [SerializeField] private bool teleportToNeutralRoomOnStart = true;
    [SerializeField] private bool closeRoomMenuAfterTeleport = true;

    [Header("Wrist buttons")]
    [SerializeField] private Button blockMenuButton;
    [SerializeField] private Button settingsMenuButton;
    [SerializeField] private Button musicMenuButton;

    [Tooltip("Optional wrist/menu button that opens the room menu.")]
    [SerializeField] private Button roomMenuButton;

    [Header("Close buttons")]
    [SerializeField] private Button blockCloseButton;
    [SerializeField] private Button settingsCloseButton;
    [SerializeField] private Button musicCloseButton;

    [Header("Direct XR Controller Input")]
    [SerializeField] private bool useDirectXRControllerInput = true;

    [Tooltip("Quest/XR: left trigger opens block menu.")]
    [SerializeField] private bool leftTriggerTogglesBlockMenu = true;

    [Tooltip("Quest/XR: left X button opens settings menu.")]
    [SerializeField] private bool leftPrimaryButtonTogglesSettingsMenu = true;

    [Tooltip("Quest/XR: left Y button opens music menu.")]
    [SerializeField] private bool leftSecondaryButtonTogglesMusicMenu = true;

    [Header("Fly Movement / Teleport Player")]
    [Tooltip("Usually your XR Origin root. If empty, this object's transform is used.")]
    [SerializeField] private Transform xrOriginRoot;

    [Tooltip("Usually your Main Camera inside XR Origin.")]
    [SerializeField] private Transform headCamera;

    [Tooltip("Optional. Assign the CharacterController on your XR Origin if you have one.")]
    [SerializeField] private CharacterController characterController;

    [SerializeField] private float flySpeed = 2.2f;

    [Tooltip("Joystick has to pass this value before flying starts.")]
    [SerializeField] private float flyDeadzone = 0.25f;

    [Tooltip("Disable CharacterController shortly while changing height/teleporting, so it does not block movement.")]
    [SerializeField] private bool temporarilyDisableCharacterController = true;

    [Header("Editor Keyboard Fallback")]
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField] private Key blockToggleKey = Key.B;
    [SerializeField] private Key settingsToggleKey = Key.N;
    [SerializeField] private Key musicToggleKey = Key.M;
    [SerializeField] private Key roomToggleKey = Key.L;
    [SerializeField] private Key flyUpKey = Key.E;
    [SerializeField] private Key flyDownKey = Key.Q;

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

    private XRInputDevice leftController;

    private bool lastLeftTriggerPressed;
    private bool lastLeftPrimaryPressed;
    private bool lastLeftSecondaryPressed;

    private void Awake()
    {
        if (xrOriginRoot == null)
            xrOriginRoot = transform;

        if (headCamera == null && Camera.main != null)
            headCamera = Camera.main.transform;

        if (characterController == null && xrOriginRoot != null)
            characterController = xrOriginRoot.GetComponent<CharacterController>();

        TryFindLeftController();

        PrepareMusicMenuForHiddenMode();
        WireButtons();
        WireRoomButtons();

        if (forceAllMenusClosedOnStart)
        {
            startHideFramesRemaining = Mathf.Max(1, forceClosedStartFrames);
            ForceAllClosed();
        }
    }

    private IEnumerator Start()
    {
        if (teleportToNeutralRoomOnStart && neutralRoomSpawn != null)
            TeleportTo(neutralRoomSpawn);

        if (!forceAllMenusClosedOnStart)
            yield break;

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
        WireRoomButtons();
        TryFindLeftController();
    }

    private void OnDestroy()
    {
        UnwireRoomButtons();
    }

    private void Update()
    {
        if (startHideFramesRemaining > 0 && forceAllMenusClosedOnStart)
        {
            startHideFramesRemaining--;
            ForceAllClosed();
        }

        // FIX: HandleDirectXRControllerInput() and HandleFlyMovement() used to
        // run here too - but LeftControllerGameControls now handles ALL
        // controller-button menu toggling and flying instead. Having both
        // active at once meant: (1) every menu button toggle happened TWICE
        // per press (open, then immediately close again, since both scripts
        // reacted to the same physical button), and (2) this old
        // HandleFlyMovement() read the LEFT joystick's Y axis for flying -
        // the SAME stick used for walking - so pushing forward to walk also
        // triggered vertical flying at the same time, causing the "jumping
        // while walking forward" behavior. Keeping only
        // HandleKeyboardMenuInput() here, since that's for menu keyboard
        // shortcuts (M/N/B/L) and doesn't overlap with anything
        // LeftControllerGameControls does.
        HandleKeyboardMenuInput();
    }

    private void TryFindLeftController()
    {
        leftController = XRInputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
    }

    private void HandleDirectXRControllerInput()
    {
        if (!useDirectXRControllerInput)
            return;

        if (!leftController.isValid)
            TryFindLeftController();

        if (!leftController.isValid)
            return;

        bool triggerPressed = false;
        bool primaryPressed = false;
        bool secondaryPressed = false;

        leftController.TryGetFeatureValue(XRCommonUsages.triggerButton, out triggerPressed);
        leftController.TryGetFeatureValue(XRCommonUsages.primaryButton, out primaryPressed);
        leftController.TryGetFeatureValue(XRCommonUsages.secondaryButton, out secondaryPressed);

        if (leftTriggerTogglesBlockMenu && triggerPressed && !lastLeftTriggerPressed)
            ToggleBlockMenu();

        if (leftPrimaryButtonTogglesSettingsMenu && primaryPressed && !lastLeftPrimaryPressed)
            ToggleSettingsMenu();

        if (leftSecondaryButtonTogglesMusicMenu && secondaryPressed && !lastLeftSecondaryPressed)
            ToggleMusicMenu();

        lastLeftTriggerPressed = triggerPressed;
        lastLeftPrimaryPressed = primaryPressed;
        lastLeftSecondaryPressed = secondaryPressed;
    }

    private void HandleKeyboardMenuInput()
    {
        if (!allowKeyboardFallback || Keyboard.current == null)
            return;

        if (WasKeyboardPressed(blockToggleKey))
            ToggleBlockMenu();

        if (WasKeyboardPressed(settingsToggleKey))
            ToggleSettingsMenu();

        if (WasKeyboardPressed(musicToggleKey))
            ToggleMusicMenu();

        if (WasKeyboardPressed(roomToggleKey))
            ToggleRoomMenu();
    }

    private void HandleFlyMovement()
    {
        float vertical = 0f;

        if (useDirectXRControllerInput)
        {
            if (!leftController.isValid)
                TryFindLeftController();

            if (leftController.isValid)
            {
                Vector2 stick;

                if (leftController.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out stick))
                    vertical += stick.y;
            }
        }

        if (allowKeyboardFallback && Keyboard.current != null)
        {
            KeyControl upKey = Keyboard.current[flyUpKey];
            KeyControl downKey = Keyboard.current[flyDownKey];

            if (upKey != null && upKey.isPressed)
                vertical += 1f;

            if (downKey != null && downKey.isPressed)
                vertical -= 1f;
        }

        if (Mathf.Abs(vertical) < flyDeadzone)
            return;

        vertical = Mathf.Clamp(vertical, -1f, 1f);

        Vector3 move = Vector3.up * vertical * flySpeed * Time.deltaTime;
        MoveXRRoot(move);
    }

    private void MoveXRRoot(Vector3 move)
    {
        if (xrOriginRoot == null)
            return;

        if (temporarilyDisableCharacterController && characterController != null)
        {
            characterController.enabled = false;
            xrOriginRoot.position += move;
            characterController.enabled = true;
            return;
        }

        xrOriginRoot.position += move;
    }

    private bool WasKeyboardPressed(Key key)
    {
        if (key == Key.None || Keyboard.current == null)
            return false;

        KeyControl keyControl = Keyboard.current[key];
        return keyControl != null && keyControl.wasPressedThisFrame;
    }

    private void WireButtons()
    {
        WireButton(blockMenuButton, ToggleBlockMenu);
        WireButton(settingsMenuButton, ToggleSettingsMenu);
        WireButton(musicMenuButton, ToggleMusicMenu);
        WireButton(roomMenuButton, ToggleRoomMenu);

        WireButton(blockCloseButton, CloseBlockMenu);
        WireButton(settingsCloseButton, CloseSettingsMenu);
        WireButton(musicCloseButton, CloseMusicMenu);
        WireButton(roomCloseButton, CloseRoomMenu);
    }

    private void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void WireRoomButtons()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            int index = i;

            if (rooms[index] != null && rooms[index].roomButton != null)
            {
                rooms[index].roomButton.onClick.RemoveAllListeners();
                rooms[index].roomButton.onClick.AddListener(() => TeleportToRoom(index));
            }
        }
    }

    private void UnwireRoomButtons()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i] != null && rooms[i].roomButton != null)
                rooms[i].roomButton.onClick.RemoveAllListeners();
        }

        if (roomCloseButton != null)
            roomCloseButton.onClick.RemoveAllListeners();
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

    public void ToggleRoomMenu()
    {
        TogglePanel(roomMenuRoot, "Room Menu Root");
    }

    public void ToggleMusicPanel()
    {
        ToggleMusicMenu();
    }

    public void OpenBlockMenu()
    {
        OpenPanelExclusive(blockMenuRoot);
    }

    public void OpenSettingsMenu()
    {
        OpenPanelExclusive(settingsMenuRoot);
    }

    public void OpenMusicMenu()
    {
        OpenPanelExclusive(musicMenuRoot);
    }

    public void OpenRoomMenu()
    {
        OpenPanelExclusive(roomMenuRoot);
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

    public void CloseRoomMenu()
    {
        CloseWithAnimation(roomMenuRoot);
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
        ForcePanelClosed(roomMenuRoot);
    }

    // ---------------------------------------------------------------------
    // Room Teleport
    // ---------------------------------------------------------------------

    public void TeleportToRoom(int roomIndex)
    {
        if (roomIndex < 0 || roomIndex >= rooms.Count)
            return;

        RoomEntry entry = rooms[roomIndex];

        if (entry == null || entry.spawnPoint == null)
        {
            Debug.LogWarning("Room entry has no spawn point.", this);
            return;
        }

        TeleportTo(entry.spawnPoint);

        if (closeRoomMenuAfterTeleport)
            CloseRoomMenu();
    }

    public void TeleportToNeutralRoom()
    {
        if (neutralRoomSpawn != null)
            TeleportTo(neutralRoomSpawn);

        if (closeRoomMenuAfterTeleport)
            CloseRoomMenu();
    }

    private void TeleportTo(Transform target)
    {
        if (target == null || xrOriginRoot == null)
            return;

        bool hadCharacterController = characterController != null && characterController.enabled;

        if (hadCharacterController)
            characterController.enabled = false;

        Vector3 targetPosition = target.position;

        if (headCamera != null)
        {
            Vector3 cameraOffset = headCamera.position - xrOriginRoot.position;
            cameraOffset.y = 0f;

            xrOriginRoot.position = targetPosition - cameraOffset;
        }
        else
        {
            xrOriginRoot.position = targetPosition;
        }

        Vector3 currentForward = headCamera != null ? headCamera.forward : xrOriginRoot.forward;
        currentForward.y = 0f;

        Vector3 targetForward = target.forward;
        targetForward.y = 0f;

        if (currentForward.sqrMagnitude > 0.001f && targetForward.sqrMagnitude > 0.001f)
        {
            currentForward.Normalize();
            targetForward.Normalize();

            float angle = Vector3.SignedAngle(currentForward, targetForward, Vector3.up);
            xrOriginRoot.RotateAround(targetPosition, Vector3.up, angle);
        }

        if (hadCharacterController)
            characterController.enabled = true;

        Debug.Log("Teleported to room spawn: " + target.name);
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

    private void OpenPanelExclusive(GameObject root)
    {
        if (root == null)
            return;

        ForceAllExceptClosed(root);
        OpenPanel(root);
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

        if (roomMenuRoot != keepOpen)
            ForcePanelClosed(roomMenuRoot);
    }

    private void ForcePanelClosed(GameObject root)
    {
        if (root == null)
            return;

        if (IsMusicMenu(root) && keepMusicMenuAliveWhenClosed)
        {
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

        bool isMusicKeptAlive = IsMusicMenu(root) && keepMusicMenuAliveWhenClosed;
        LegoPanelAnimation animation = FindAnimation(root);

        if (animation != null)
        {
            animation.PlayClose(() =>
            {
                if (root == null)
                    return;

                if (isMusicKeptAlive)
                    FinishMusicPanelCloseAfterAnimation(root);
                else
                    root.SetActive(false);
            });

            return;
        }

        if (isMusicKeptAlive)
            SetMusicPanelVisible(root, false);
        else
            root.SetActive(false);
    }

    /// <summary>
    /// Called after the music panel's close animation finishes. LegoPanelAnimation
    /// resets scale/alpha back to "fully open" right before invoking this callback
    /// (it normally expects the caller to SetActive(false) immediately after) - since
    /// we deliberately keep the music panel's GameObject active so its AudioSource
    /// keeps playing, we instead hide it via disabling its Canvas/raycasting here,
    /// which stays hidden regardless of that scale/alpha reset.
    /// </summary>
    private void FinishMusicPanelCloseAfterAnimation(GameObject root)
    {
        SetMusicPanelVisible(root, false);
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

            return root.activeSelf &&
                   canvasGroup != null &&
                   canvasGroup.alpha > 0.5f &&
                   canvasGroup.blocksRaycasts;
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
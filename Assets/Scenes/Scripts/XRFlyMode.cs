using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Left controller controls (physical VR controller buttons only):
/// Y = Music Menu
/// X = Settings Menu
/// Left Trigger = Block Menu
/// Left Joystick Press (click in) = Room Menu
/// Left Joystick Up/Down (tilt) = Fly up/down
///
/// Keyboard equivalents (M/N/B/L) are handled exclusively by
/// LegoTwoPanelMenuController - NOT duplicated here - so there is only ever
/// one place per input that can trigger a given menu toggle.
///
/// Put this on the XR Origin root.
/// </summary>
public class LeftControllerGameControls : MonoBehaviour
{
    [Header("Menu Controller")]
    [SerializeField] private LegoTwoPanelMenuController twoPanelMenuController;

    [Header("XR Origin / Movement")]
    [Tooltip("Usually your XR Origin root. If empty, this transform is used.")]
    [SerializeField] private Transform xrOriginRoot;

    [Tooltip("Optional. If your XR Origin has a CharacterController, assign it or leave empty.")]
    [SerializeField] private CharacterController characterController;

    [Header("Left Controller Buttons")]
    [Tooltip("Left Y button -> Music Menu. Bind this specifically to <XRController>{LeftHand}/secondaryButton (make sure the binding is scoped to LeftHand only, not a generic/hand-agnostic secondaryButton - otherwise the right controller's secondary button will also trigger this).")]
    [SerializeField] private InputActionReference leftYButton;

    [Tooltip("Left X button -> Settings Menu. Bind this specifically to <XRController>{LeftHand}/primaryButton.")]
    [SerializeField] private InputActionReference leftXButton;

    [Tooltip("Left trigger press -> Block Menu. Bind this specifically to <XRController>{LeftHand}/triggerPressed.")]
    [SerializeField] private InputActionReference leftTriggerButton;

    [Tooltip("Left joystick press/click-in -> Room Menu. Bind this specifically to <XRController>{LeftHand}/primary2DAxisClick.")]
    [SerializeField] private InputActionReference leftJoystickClickButton;

    [Header("Left Joystick Fly")]
    [Tooltip("Left joystick 2D axis. Usually <XRController>{LeftHand}/primary2DAxis")]
    [SerializeField] private InputActionReference leftJoystickAxis;

    [SerializeField] private float flySpeed = 2.2f;

    [Tooltip("Joystick must be above this value before flying starts.")]
    [SerializeField] private float joystickDeadzone = 0.25f;

    [Tooltip("Disable CharacterController while moving vertically to avoid it blocking height movement.")]
    [SerializeField] private bool temporarilyDisableCharacterController = true;

    [Header("Keyboard Test Fallback (Fly only - menu keys live in LegoTwoPanelMenuController)")]
    [SerializeField] private bool allowKeyboardFallback = true;

    private void Awake()
    {
        if (xrOriginRoot == null)
            xrOriginRoot = transform;

        if (characterController == null && xrOriginRoot != null)
            characterController = xrOriginRoot.GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        EnableAction(leftYButton);
        EnableAction(leftXButton);
        EnableAction(leftTriggerButton);
        EnableAction(leftJoystickClickButton);
        EnableAction(leftJoystickAxis);
    }

    private void OnDisable()
    {
        DisableAction(leftYButton);
        DisableAction(leftXButton);
        DisableAction(leftTriggerButton);
        DisableAction(leftJoystickClickButton);
        DisableAction(leftJoystickAxis);
    }

    private void Update()
    {
        HandleMenus();
        HandleFly();
    }

    private void HandleMenus()
    {
        // Physical controller buttons only. Keyboard equivalents (M/N/B/L) are
        // handled exclusively by LegoTwoPanelMenuController.HandleKeyboardMenuInput -
        // intentionally NOT duplicated here, so each input has exactly one path
        // to its menu toggle.

        // Y = Music Menu
        if (WasPressedThisFrame(leftYButton))
            ToggleMusicMenu();

        // X = Settings Menu
        if (WasPressedThisFrame(leftXButton))
            ToggleSettingsMenu();

        // Left Trigger = Block Menu
        if (WasPressedThisFrame(leftTriggerButton))
            ToggleBlockMenu();

        // Left Joystick Click = Room Menu
        if (WasPressedThisFrame(leftJoystickClickButton))
            ToggleRoomMenu();
    }

    private void HandleFly()
    {
        float vertical = 0f;

        if (leftJoystickAxis != null && leftJoystickAxis.action != null)
        {
            Vector2 joystick = leftJoystickAxis.action.ReadValue<Vector2>();
            vertical = joystick.y;
        }

        if (allowKeyboardFallback && Keyboard.current != null)
        {
            if (Keyboard.current.eKey.isPressed)
                vertical += 1f;

            if (Keyboard.current.qKey.isPressed)
                vertical -= 1f;
        }

        if (Mathf.Abs(vertical) < joystickDeadzone)
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

    private void ToggleBlockMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleBlockMenu();
    }

    private void ToggleSettingsMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleSettingsMenu();
    }

    private void ToggleMusicMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleMusicMenu();
    }

    private void ToggleRoomMenu()
    {
        if (twoPanelMenuController != null)
            twoPanelMenuController.ToggleRoomMenu();
    }

    private bool WasPressedThisFrame(InputActionReference actionReference)
    {
        return actionReference != null &&
               actionReference.action != null &&
               actionReference.action.WasPressedThisFrame();
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
}
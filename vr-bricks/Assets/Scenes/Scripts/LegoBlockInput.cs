using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Handles input for rotating a held LEGO block.
/// 
/// Rotation can be triggered by:
/// - a VR controller input action,
/// - the O key in the Unity editor, if keyboard fallback is enabled.
/// </summary>
[RequireComponent(typeof(LegoBlockGhostManager))]
public class LegoBlockInput : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Input Settings
    // -------------------------------------------------------------------------

    [Header("VR Controller Input")]
    [Tooltip("Input action used to rotate the held block clockwise.")]
    [SerializeField] private InputActionReference rotateAction;

    [Header("Editor Keyboard Fallback")]
    [Tooltip("Allows rotating the block with the O key while testing in the editor.")]
    [SerializeField] private bool allowKeyboardFallback = true;

    // -------------------------------------------------------------------------
    // Runtime References
    // -------------------------------------------------------------------------

    private LegoBlockGhostManager ghostManager;
    private XRGrabInteractable grabInteractable;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        ghostManager = GetComponent<LegoBlockGhostManager>();
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    private void OnEnable()
    {
        if (rotateAction != null)
            rotateAction.action.Enable();
    }

    private void OnDisable()
    {
        if (rotateAction != null)
            rotateAction.action.Disable();
    }

    private void Update()
    {
        if (ghostManager == null)
            return;

        if (!IsHeld())
            return;

        if (WasRotatePressed())
            ghostManager.RotateClockwise();
    }

    // -------------------------------------------------------------------------
    // Input Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if this block is currently selected by any interactor.
    /// </summary>
    private bool IsHeld()
    {
        return grabInteractable != null &&
               grabInteractable.interactorsSelecting.Count > 0;
    }

    /// <summary>
    /// Returns true if the rotate input was pressed this frame.
    /// </summary>
    private bool WasRotatePressed()
    {
        if (rotateAction != null && rotateAction.action.WasPressedThisFrame())
            return true;

        if (allowKeyboardFallback && Keyboard.current != null)
            return Keyboard.current.oKey.wasPressedThisFrame;

        return false;
    }
}
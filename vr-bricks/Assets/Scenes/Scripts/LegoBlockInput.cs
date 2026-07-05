using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(LegoBlockGhostManager))]
public class LegoBlockInput : MonoBehaviour
{
    [Header("VR Controller Input")]
    [Tooltip("Input action used to rotate the held block clockwise.")]
    [SerializeField] private InputActionReference rotateAction;

    [Header("Editor Keyboard Fallback")]
    [Tooltip("Allows rotating the block with the O key while testing in the editor.")]
    [SerializeField] private bool allowKeyboardFallback = true;

    private LegoBlockGhostManager ghostManager;
    private XRGrabInteractable grabInteractable;

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

        if (WasRotatePressed())
        {
            Debug.Log("O pressed, IsHeld=" + IsHeld());

            if (IsHeld())
                ghostManager.RotateClockwise();
        }
    }

    private bool IsHeld()
    {
        return grabInteractable != null &&
               grabInteractable.interactorsSelecting.Count > 0;
    }

    private bool WasRotatePressed()
    {
        if (rotateAction != null && rotateAction.action.WasPressedThisFrame())
            return true;

        if (allowKeyboardFallback && Keyboard.current != null)
            return Keyboard.current.oKey.wasPressedThisFrame;

        return false;
    }
}
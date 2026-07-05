using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Deletes the LEGO block currently hovered by the left hand interactor.
/// Triggered by the left Grip button or F key.
/// </summary>
public class LegoBlockDeleter : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionReference deleteAction;

    [Header("Left Hand Interactor")]
    [SerializeField] private XRBaseInteractor leftInteractor;

    private void OnEnable()
    {
        if (deleteAction != null)
            deleteAction.action.Enable();
    }

    private void OnDisable()
    {
        if (deleteAction != null)
            deleteAction.action.Disable();
    }

    private void Update()
    {
        if (!WasDeletePressed())
            return;

        TryDeleteHoveredBlock();
    }

    private bool WasDeletePressed()
    {
        if (deleteAction != null && deleteAction.action.WasPressedThisFrame())
            return true;

        if (Keyboard.current != null)
            return Keyboard.current.fKey.wasPressedThisFrame;

        return false;
    }

    private void TryDeleteHoveredBlock()
    {
        if (leftInteractor == null)
            return;

        // Prüfe alle Objekte über die der Interactor gerade hovert
        foreach (var interactable in leftInteractor.interactablesHovered)
        {
            LegoBlock block = interactable.transform.GetComponentInParent<LegoBlock>();

            if (block == null)
                continue;

            // Block darf nicht aufgehaben sein
            if (block.HasAttachedBlockAbove())
                continue;

            DeleteBlock(block);
            return;
        }
    }

    private void DeleteBlock(LegoBlock block)
    {
        // Sockets freigeben bevor Destroy
        LegoBlockGhostManager ghostManager =
            block.GetComponent<LegoBlockGhostManager>();

        if (ghostManager != null)
            ghostManager.ForceRelease();

        Destroy(block.gameObject);
    }
}
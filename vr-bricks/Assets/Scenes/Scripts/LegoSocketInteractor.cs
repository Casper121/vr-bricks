using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Optional XR Socket Interactor filter for LEGO sockets.
/// 
/// The custom LegoBlockGhostManager handles the final snap placement,
/// but this interactor still prevents invalid XR socket selections.
/// </summary>
[RequireComponent(typeof(LegoSocket))]
public class LegoSocketInteractor : XRSocketInteractor
{
    // -------------------------------------------------------------------------
    // Runtime References
    // -------------------------------------------------------------------------

    private LegoSocket legoSocket;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    protected override void Awake()
    {
        base.Awake();
        legoSocket = GetComponent<LegoSocket>();
    }

    // -------------------------------------------------------------------------
    // XR Socket Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if this socket is allowed to select the given interactable.
    /// </summary>
    public override bool CanSelect(IXRSelectInteractable interactable)
    {
        if (!base.CanSelect(interactable))
            return false;

        if (legoSocket == null)
            return true;

        if (!legoSocket.isInnerSocket)
            return false;

        LegoBlock block = interactable.transform.GetComponentInParent<LegoBlock>();

        if (block == null)
            return false;

        // The block must approach the socket from above.
        if (block.transform.position.y <= legoSocket.transform.position.y + 0.05f)
            return false;

        if (legoSocket.parentGrid == null)
            return true;

        return legoSocket.parentGrid.DoesBlockCoverInnerSocket(
            legoSocket.gridX,
            legoSocket.gridZ,
            block
        );
    }
}
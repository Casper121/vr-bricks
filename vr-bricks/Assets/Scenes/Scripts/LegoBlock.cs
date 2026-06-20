using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Stores the logical LEGO block dimensions and controls the block's physical stability.
/// 
/// A block becomes stable when:
/// - it is snapped to a socket,
/// - another block is attached above it,
/// - another held block is temporarily previewed above it.
/// 
/// A block with another block on top cannot be grabbed directly.
/// A snapped block without a block above it can still be grabbed again.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LegoBlock : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Block Settings
    // -------------------------------------------------------------------------

    [Header("Block Dimensions")]
    [Tooltip("Number of studs in the local X direction.")]
    public int width = 2;

    [Tooltip("Number of studs in the local Z direction.")]
    public int length = 2;

    [Tooltip("Block height in standard block units.")]
    public float height = 1f;

    // -------------------------------------------------------------------------
    // Inspector: Gizmo Settings
    // -------------------------------------------------------------------------

    [Header("Gizmo Display")]
    [Tooltip("Visual gizmo size of one stud in local X direction.")]
    [SerializeField] private float studSizeX = 0.5f;

    [Tooltip("Visual gizmo size of one stud in local Z direction.")]
    [SerializeField] private float studSizeZ = 0.5f;

    [Tooltip("Visual gizmo height scale.")]
    [SerializeField] private float blockHeightScale = 1f;

    [Tooltip("Local offset for the selected-block gizmo.")]
    [SerializeField] private Vector3 gizmoOffset = Vector3.zero;

    // -------------------------------------------------------------------------
    // Runtime References
    // -------------------------------------------------------------------------

    private XRGrabInteractable grabInteractable;
    private Rigidbody rb;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private int attachedBlocksAbove;
    private int temporaryStabilizers;
    private bool isSnappedToSocket;

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        UpdateStabilityAndGrabability();
    }

    // -------------------------------------------------------------------------
    // Public API: Block Stability
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers that another LEGO block has been attached above this block.
    /// </summary>
    public void AddAttachedBlockAbove()
    {
        attachedBlocksAbove++;
        UpdateStabilityAndGrabability();
    }

    /// <summary>
    /// Registers that a previously attached block above this block was removed.
    /// </summary>
    public void RemoveAttachedBlockAbove()
    {
        attachedBlocksAbove = Mathf.Max(0, attachedBlocksAbove - 1);
        UpdateStabilityAndGrabability();
    }

    /// <summary>
    /// Temporarily stabilizes this block while another block is previewed above it.
    /// </summary>
    public void BeginTemporaryStabilization()
    {
        temporaryStabilizers++;
        UpdateStabilityAndGrabability();
    }

    /// <summary>
    /// Removes one temporary stabilization request.
    /// </summary>
    public void EndTemporaryStabilization()
    {
        temporaryStabilizers = Mathf.Max(0, temporaryStabilizers - 1);
        UpdateStabilityAndGrabability();
    }

    /// <summary>
    /// Returns true if another block is currently attached above this block.
    /// </summary>
    public bool HasAttachedBlockAbove()
    {
        return attachedBlocksAbove > 0;
    }

    /// <summary>
    /// Sets whether this block is currently snapped to a socket.
    /// </summary>
    public void SetSnappedToSocket(bool snapped)
    {
        isSnappedToSocket = snapped;
        UpdateStabilityAndGrabability();
    }

    // -------------------------------------------------------------------------
    // Internal Logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Updates Rigidbody stability and whether this block can currently be grabbed.
    /// </summary>
    private void UpdateStabilityAndGrabability()
    {
        bool hasBlockAbove = attachedBlocksAbove > 0;
        bool hasTemporaryStabilizer = temporaryStabilizers > 0;
        bool shouldBeStable = isSnappedToSocket || hasBlockAbove || hasTemporaryStabilizer;

        if (grabInteractable != null)
        {
            // Only blocks with another block above them become ungrabbable.
            // A snapped block without anything on top should still be removable.
            grabInteractable.enabled = !hasBlockAbove;
        }

        if (rb == null)
            return;

        if (shouldBeStable)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        else
        {
            rb.isKinematic = false;
        }
    }

    // -------------------------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;

        Vector3 boxSize = new Vector3(
            width * studSizeX,
            height * blockHeightScale,
            length * studSizeZ
        );

        Vector3 boxCenter =
            transform.position +
            Vector3.up * (height * blockHeightScale * transform.localScale.y / 2f) +
            gizmoOffset;

        Gizmos.matrix = Matrix4x4.TRS(boxCenter, transform.rotation, transform.localScale);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
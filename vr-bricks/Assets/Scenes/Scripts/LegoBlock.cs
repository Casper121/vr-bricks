using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;

/// <summary>
/// Stores the logical LEGO block dimensions / footprint and controls the block's physical stability.
///
/// Rectangular blocks can still use width x length.
/// Irregular blocks, for example an L-shaped 3-stud corner block, can define a custom stud footprint
/// in the Inspector. Example L block footprint cells: (0,0), (1,0), (0,1).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LegoBlock : MonoBehaviour
{
    public enum FootprintMode
    {
        Rectangle,
        CustomCells
    }

    // -------------------------------------------------------------------------
    // Inspector: Block Settings
    // -------------------------------------------------------------------------

    [Header("Block Dimensions")]
    [Tooltip("Number of studs in the local X direction. Used for rectangle mode and fallback gizmos.")]
    public int width = 2;

    [Tooltip("Number of studs in the local Z direction. Used for rectangle mode and fallback gizmos.")]
    public int length = 2;

    [Tooltip("Block height in standard block units.")]
    public float height = 1f;

    [Header("Stud Footprint")]
    [Tooltip("Rectangle = normal width x length block. CustomCells = use Stud Footprint Cells for L/corner/T shapes.")]
    public FootprintMode footprintMode = FootprintMode.Rectangle;

    [Tooltip("Stud cells occupied by this block in local X/Z grid coordinates. For an L-shaped 3er block use: (0,0), (1,0), (0,1).")]
    public List<Vector2Int> studFootprintCells = new List<Vector2Int>
    {
        new Vector2Int(0, 0)
    };

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
    public LegoSocket SnappedSocket { get; set; }

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
    // Public API: Footprint
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the local stud footprint before rotation.
    /// Rectangle mode generates cells from width x length.
    /// CustomCells mode returns the inspector-defined cells after duplicate cleanup.
    /// </summary>
    public List<Vector2Int> GetStudFootprint()
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (footprintMode == FootprintMode.CustomCells && studFootprintCells != null && studFootprintCells.Count > 0)
        {
            for (int i = 0; i < studFootprintCells.Count; i++)
                AddUniqueCell(result, studFootprintCells[i]);
        }
        else
        {
            int safeWidth = Mathf.Max(1, width);
            int safeLength = Mathf.Max(1, length);

            for (int x = 0; x < safeWidth; x++)
            {
                for (int z = 0; z < safeLength; z++)
                    result.Add(new Vector2Int(x, z));
            }
        }

        if (result.Count == 0)
            result.Add(Vector2Int.zero);

        return result;
    }

    /// <summary>
    /// Returns the footprint rotated clockwise around local cell (0,0).
    /// yawStep must be 0, 1, 2, or 3, where 1 means +90 degrees.
    /// Negative grid cells are allowed; the snapper supports them.
    /// </summary>
    public List<Vector2Int> GetRotatedFootprint(int yawStep)
    {
        List<Vector2Int> source = GetStudFootprint();
        List<Vector2Int> result = new List<Vector2Int>(source.Count);

        int step = yawStep % 4;
        if (step < 0)
            step += 4;

        for (int i = 0; i < source.Count; i++)
        {
            Vector2Int cell = source[i];
            Vector2Int rotated;

            switch (step)
            {
                case 1:
                    rotated = new Vector2Int(cell.y, -cell.x);
                    break;
                case 2:
                    rotated = new Vector2Int(-cell.x, -cell.y);
                    break;
                case 3:
                    rotated = new Vector2Int(-cell.y, cell.x);
                    break;
                default:
                    rotated = cell;
                    break;
            }

            AddUniqueCell(result, rotated);
        }

        return result;
    }

    public RectInt GetFootprintBounds()
    {
        return GetFootprintBounds(GetStudFootprint());
    }

    public RectInt GetFootprintBounds(List<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0)
            return new RectInt(0, 0, 1, 1);

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minZ = int.MaxValue;
        int maxZ = int.MinValue;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minZ) minZ = cell.y;
            if (cell.y > maxZ) maxZ = cell.y;
        }

        return new RectInt(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
    }

    private void AddUniqueCell(List<Vector2Int> cells, Vector2Int cell)
    {
        if (!cells.Contains(cell))
            cells.Add(cell);
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

    public float GetWorldHeight()
    {
        return height * transform.localScale.y;
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
        Gizmos.matrix = Matrix4x4.TRS(transform.position + gizmoOffset, transform.rotation, transform.localScale);

        List<Vector2Int> cells = GetStudFootprint();

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];

            Vector3 center = new Vector3(
                cell.x * studSizeX,
                height * blockHeightScale * 0.5f,
                cell.y * studSizeZ
            );

            Vector3 size = new Vector3(studSizeX, height * blockHeightScale, studSizeZ);
            Gizmos.DrawWireCube(center, size);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}

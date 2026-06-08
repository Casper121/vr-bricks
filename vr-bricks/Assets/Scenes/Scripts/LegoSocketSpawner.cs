using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates and manages a rectangular grid of LEGO sockets.
/// 
/// This can be used for:
/// - sockets on LEGO blocks,
/// - invisible floor grids,
/// - later AR-detected real-world surfaces.
/// </summary>
public class LegoBlockSocketSpawner : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Socket Prefab
    // -------------------------------------------------------------------------

    [Header("Socket Prefab")]
    [Tooltip("Prefab that contains a LegoSocket component and optionally a LegoSocketInteractor.")]
    public GameObject anchorPointPrefab;

    // -------------------------------------------------------------------------
    // Inspector: Grid Size
    // -------------------------------------------------------------------------

    [Header("Grid Size")]
    [Tooltip("Number of sockets in local X direction.")]
    public int blockWidth = 4;

    [Tooltip("Number of sockets in local Z direction.")]
    public int blockLength = 2;

    // -------------------------------------------------------------------------
    // Inspector: Grid Position
    // -------------------------------------------------------------------------

    [Header("Grid Position")]
    [Tooltip("Local Y position of every generated socket.")]
    public float socketY = 0.2f;

    [Tooltip("Local X offset of the first generated socket.")]
    public float offsetX = -0.2f;

    [Tooltip("Local Z offset of the first generated socket.")]
    public float offsetZ = -0.8f;

    // -------------------------------------------------------------------------
    // Inspector: Grid Spacing
    // -------------------------------------------------------------------------

    [Header("Grid Spacing")]
    [Tooltip("Distance between sockets in local X direction.")]
    public float studSpacingX = 0.5f;

    [Tooltip("Distance between sockets in local Z direction.")]
    public float studSpacingZ = 0.5f;

    // -------------------------------------------------------------------------
    // Inspector: Visibility and Gizmos
    // -------------------------------------------------------------------------

    [Header("Visibility")]
    [Tooltip("If enabled, all renderers on generated sockets are disabled.")]
    [SerializeField] private bool hideGeneratedSocketRenderers = false;

    [Header("Gizmos")]
    [Tooltip("Draws socket positions when this object is selected.")]
    [SerializeField] private bool drawSocketGizmos = true;

    [Tooltip("Size of the socket gizmo spheres.")]
    [SerializeField] private float gizmoSize = 0.04f;

    // -------------------------------------------------------------------------
    // Runtime State
    // -------------------------------------------------------------------------

    private readonly List<LegoSocket> allSockets = new List<LegoSocket>();
    private readonly Dictionary<Vector2Int, LegoSocket> socketMap = new Dictionary<Vector2Int, LegoSocket>();

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        SpawnSockets();
    }

    // -------------------------------------------------------------------------
    // Public API: Area Queries
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the target area touches at least one inner socket
    /// and none of the touched sockets are occupied.
    /// 
    /// Missing sockets are ignored, which allows overhang outside the grid.
    /// </summary>
    public bool IsAreaClear(int startX, int startZ, int width, int length)
    {
        bool touchesAtLeastOneInnerSocket = false;

        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
            {
                LegoSocket socket = GetSocketAt(x, z);

                if (socket == null || !socket.isInnerSocket)
                    continue;

                touchesAtLeastOneInnerSocket = true;

                if (socket.isOccupied)
                    return false;
            }
        }

        return touchesAtLeastOneInnerSocket;
    }

    /// <summary>
    /// Returns all sockets inside a rectangular grid area.
    /// 
    /// Missing sockets are ignored, which allows overhang outside the grid.
    /// </summary>
    public List<LegoSocket> GetSocketsInArea(int startX, int startZ, int width, int length)
    {
        List<LegoSocket> result = new List<LegoSocket>();

        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
            {
                LegoSocket socket = GetSocketAt(x, z);

                if (socket != null && socket.isInnerSocket)
                    result.Add(socket);
            }
        }

        return result;
    }

    /// <summary>
    /// Marks all sockets inside an area as occupied or free.
    /// Occupied sockets have their colliders disabled.
    /// </summary>
    public void SetSocketsOccupiedInArea(int startX, int startZ, int width, int length, bool occupied)
    {
        List<LegoSocket> sockets = GetSocketsInArea(startX, startZ, width, length);

        foreach (LegoSocket socket in sockets)
        {
            SetSocketOccupied(socket, occupied);
        }
    }

    // -------------------------------------------------------------------------
    // Public API: Position Calculation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calculates the world center position for a non-rotated block.
    /// </summary>
    public Vector3 GetBlockCenterWorldPosition(int gridX, int gridZ, LegoBlock block)
    {
        float midX = gridX + (block.width - 1) * 0.5f;
        float midZ = gridZ + (block.length - 1) * 0.5f;

        Vector3 localCenter = new Vector3(
            midX * studSpacingX + offsetX,
            socketY + block.height,
            midZ * studSpacingZ + offsetZ
        );

        return transform.TransformPoint(localCenter);
    }

    /// <summary>
    /// Calculates the world center position for a block with a yaw rotation.
    /// </summary>
    public Vector3 GetBlockCenterWorldPositionRotated(
        int startGridX,
        int startGridZ,
        LegoBlock block,
        float yawOffset
    )
    {
        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, yawOffset);

        float midX = startGridX + (effectiveWidth - 1) * 0.5f;
        float midZ = startGridZ + (effectiveLength - 1) * 0.5f;

        Vector3 localCenter = new Vector3(
            midX * studSpacingX + offsetX,
            socketY + block.height,
            midZ * studSpacingZ + offsetZ
        );

        return transform.TransformPoint(localCenter);
    }

    // -------------------------------------------------------------------------
    // Public API: Coverage Checks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the rotated block area touches at least one inner socket.
    /// </summary>
    public bool DoesRotatedBlockCoverInnerSocket(
        int startGridX,
        int startGridZ,
        LegoBlock block,
        float yawOffset
    )
    {
        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, yawOffset);

        return DoesAreaCoverInnerSocket(
            startGridX,
            startGridZ,
            effectiveWidth,
            effectiveLength
        );
    }

    /// <summary>
    /// Returns true if the non-rotated block area touches at least one inner socket.
    /// </summary>
    public bool DoesBlockCoverInnerSocket(int startGridX, int startGridZ, LegoBlock block)
    {
        return DoesAreaCoverInnerSocket(
            startGridX,
            startGridZ,
            block.width,
            block.length
        );
    }

    // -------------------------------------------------------------------------
    // Socket Generation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns all sockets for this grid.
    /// </summary>
    private void SpawnSockets()
    {
        if (anchorPointPrefab == null)
        {
            Debug.LogError($"{gameObject.name}: No socket prefab assigned.");
            return;
        }

        ClearGeneratedSockets();

        for (int x = 0; x < blockWidth; x++)
        {
            for (int z = 0; z < blockLength; z++)
            {
                CreateSocket(x, z);
            }
        }
    }

    /// <summary>
    /// Creates a single socket at the given grid coordinate.
    /// </summary>
    private void CreateSocket(int x, int z)
    {
        GameObject socketObject = Instantiate(anchorPointPrefab, transform);

        socketObject.transform.localPosition = GetSocketLocalPosition(x, z);
        socketObject.transform.localRotation = Quaternion.identity;
        socketObject.name = $"Socket_{x}_{z}";

        LegoSocket socket = socketObject.GetComponent<LegoSocket>();

        if (socket == null)
        {
            Debug.LogError($"{socketObject.name}: Socket prefab has no LegoSocket component.");
            return;
        }

        socket.isInnerSocket = true;
        socket.gridX = x;
        socket.gridZ = z;
        socket.parentGrid = this;

        allSockets.Add(socket);
        socketMap[new Vector2Int(x, z)] = socket;

        if (hideGeneratedSocketRenderers)
            HideRenderers(socketObject);
    }

    /// <summary>
    /// Removes previously generated socket children.
    /// </summary>
    private void ClearGeneratedSockets()
    {
        allSockets.Clear();
        socketMap.Clear();

        List<GameObject> childrenToDelete = new List<GameObject>();

        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Socket_"))
                childrenToDelete.Add(child.gameObject);
        }

        foreach (GameObject child in childrenToDelete)
        {
            Destroy(child);
        }
    }

    // -------------------------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the local position of a socket at the given grid coordinate.
    /// </summary>
    private Vector3 GetSocketLocalPosition(int x, int z)
    {
        return new Vector3(
            x * studSpacingX + offsetX,
            socketY,
            z * studSpacingZ + offsetZ
        );
    }

    /// <summary>
    /// Returns the socket at the given grid coordinate, or null if none exists.
    /// </summary>
    private LegoSocket GetSocketAt(int x, int z)
    {
        socketMap.TryGetValue(new Vector2Int(x, z), out LegoSocket socket);
        return socket;
    }

    /// <summary>
    /// Returns true if the rectangular area touches at least one inner socket.
    /// </summary>
    private bool DoesAreaCoverInnerSocket(int startX, int startZ, int width, int length)
    {
        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
            {
                LegoSocket socket = GetSocketAt(x, z);

                if (socket != null && socket.isInnerSocket)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks a socket as occupied or free and updates its collider.
    /// </summary>
    private void SetSocketOccupied(LegoSocket socket, bool occupied)
    {
        if (socket == null)
            return;

        socket.isOccupied = occupied;

        Collider socketCollider = socket.GetComponent<Collider>();

        if (socketCollider != null)
            socketCollider.enabled = !occupied;
    }

    /// <summary>
    /// Disables all renderers on the target object and its children.
    /// Useful for invisible floor grids.
    /// </summary>
    private void HideRenderers(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }
    }

    /// <summary>
    /// Returns effective block width and length after a yaw rotation.
    /// </summary>
    private (int width, int length) GetRotatedDimensions(LegoBlock block, float yawOffset)
    {
        bool rotatedByQuarterTurn =
            Mathf.Approximately(Mathf.Abs(yawOffset) % 180f, 90f);

        if (rotatedByQuarterTurn)
            return (block.length, block.width);

        return (block.width, block.length);
    }

    // -------------------------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (!drawSocketGizmos)
            return;

        Gizmos.color = Color.green;

        for (int x = 0; x < blockWidth; x++)
        {
            for (int z = 0; z < blockLength; z++)
            {
                Vector3 worldPosition = transform.TransformPoint(GetSocketLocalPosition(x, z));
                Gizmos.DrawSphere(worldPosition, gizmoSize);
            }
        }
    }
}
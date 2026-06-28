using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates and manages LEGO snap sockets for one connected LEGO surface.
///
/// Important:
/// - Use one LegoBlockSocketSpawner per physical top surface.
/// - Rectangle mode is for normal rectangular surfaces.
/// - CustomCells mode is for irregular / combined surfaces.
/// - For irregular blocks, customSocketCells should usually match the block's
///   LegoBlock.studFootprintCells.
/// </summary>
[DisallowMultipleComponent]
public class LegoBlockSocketSpawner : MonoBehaviour
{
    public enum SocketGridMode
    {
        Rectangle,
        CustomCells
    }

    [Header("Socket Prefab")]
    public GameObject anchorPointPrefab;

    [Header("Grid Mode")]
    public SocketGridMode gridMode = SocketGridMode.Rectangle;

    [Tooltip("Used in CustomCells mode. Add every socket cell that exists on this surface.")]
    public List<Vector2Int> customSocketCells = new List<Vector2Int>
    {
        Vector2Int.zero
    };

    [Header("Rectangle Grid Size")]
    [Min(1)] public int blockWidth = 4;
    [Min(1)] public int blockLength = 2;

    [Header("Grid Transform")]
    public float socketY = 0.2f;
    public float offsetX = -0.2f;
    public float offsetZ = -0.8f;
    public float studSpacingX = 0.5f;
    public float studSpacingZ = 0.5f;

    [Header("Display")]
    [SerializeField] private bool hideGeneratedSocketRenderers = false;
    [SerializeField] private bool drawSocketGizmos = true;
    [SerializeField] private float gizmoSize = 0.04f;

    private readonly List<LegoSocket> sockets = new List<LegoSocket>();
    private readonly Dictionary<Vector2Int, LegoSocket> socketMap = new Dictionary<Vector2Int, LegoSocket>();

    private void Start()
    {
        SpawnSockets();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        blockWidth = Mathf.Max(1, blockWidth);
        blockLength = Mathf.Max(1, blockLength);
        studSpacingX = Mathf.Max(0.01f, studSpacingX);
        studSpacingZ = Mathf.Max(0.01f, studSpacingZ);
        gizmoSize = Mathf.Max(0.001f, gizmoSize);

        CleanCustomSocketCells();
    }
#endif

    [ContextMenu("Respawn Sockets")]
    public void RespawnSockets()
    {
        SpawnSockets();
    }

    // ---------------------------------------------------------------------
    // Public API used by LegoBlockGhostManager
    // ---------------------------------------------------------------------

    public bool IsFootprintAreaClear(List<Vector2Int> absoluteCells)
    {
        if (absoluteCells == null || absoluteCells.Count == 0)
            return false;

        bool touchedSocket = false;

        for (int i = 0; i < absoluteCells.Count; i++)
        {
            LegoSocket socket = GetSocketAt(absoluteCells[i]);

            if (socket == null || !socket.isInnerSocket)
                continue;

            touchedSocket = true;

            if (socket.isOccupied)
                return false;
        }

        return touchedSocket;
    }

    public bool DoesFootprintCoverInnerSocket(List<Vector2Int> absoluteCells)
    {
        if (absoluteCells == null || absoluteCells.Count == 0)
            return false;

        for (int i = 0; i < absoluteCells.Count; i++)
        {
            LegoSocket socket = GetSocketAt(absoluteCells[i]);

            if (socket != null && socket.isInnerSocket)
                return true;
        }

        return false;
    }

    public List<LegoSocket> GetSocketsInFootprint(List<Vector2Int> absoluteCells)
    {
        List<LegoSocket> result = new List<LegoSocket>();

        if (absoluteCells == null)
            return result;

        for (int i = 0; i < absoluteCells.Count; i++)
        {
            LegoSocket socket = GetSocketAt(absoluteCells[i]);

            if (socket != null && socket.isInnerSocket && !result.Contains(socket))
                result.Add(socket);
        }

        return result;
    }

    public void SetSocketsOccupiedInFootprint(List<Vector2Int> absoluteCells, bool occupied)
    {
        List<LegoSocket> touchedSockets = GetSocketsInFootprint(absoluteCells);

        for (int i = 0; i < touchedSockets.Count; i++)
            SetSocketOccupied(touchedSockets[i], occupied);
    }

    /// <summary>
    /// Returns the world root/center position for a block that occupies absoluteCells.
    /// The center is calculated from the bounding box of those cells.
    /// </summary>
    public Vector3 GetFootprintCenterWorldPosition(List<Vector2Int> absoluteCells, float blockWorldHeight)
    {
        if (absoluteCells == null || absoluteCells.Count == 0)
            return transform.position;

        RectInt bounds = GetBounds(absoluteCells);

        float midX = bounds.xMin + (bounds.width - 1) * 0.5f;
        float midZ = bounds.yMin + (bounds.height - 1) * 0.5f;

        // Compute X/Z center in world space using TransformPoint (respects rotation/position),
        // but pass Y=0 to avoid the parent block's scale shrinking socketY at small scales.
        Vector3 localXZ = new Vector3(
            midX * studSpacingX + offsetX,
            0f,
            midZ * studSpacingZ + offsetZ
        );

        Vector3 worldXZ = transform.TransformPoint(localXZ);

        // socketY is an unscaled world-space height above this spawner's position.
        // We must NOT pass it through TransformPoint: if this spawner is a child of
        // a scaled LegoBlock, TransformPoint would multiply socketY by the block's
        // scale and cause blocks to sink into the floor at scales below 1.
        float worldY = transform.position.y + socketY + blockWorldHeight;

        return new Vector3(worldXZ.x, worldY, worldXZ.z);
    }

    public LegoSocket GetSocketAt(int x, int z)
    {
        return GetSocketAt(new Vector2Int(x, z));
    }

    public List<Vector2Int> GetSocketCells()
    {
        if (gridMode == SocketGridMode.CustomCells)
        {
            CleanCustomSocketCells();
            return new List<Vector2Int>(customSocketCells);
        }

        List<Vector2Int> result = new List<Vector2Int>(blockWidth * blockLength);

        for (int x = 0; x < blockWidth; x++)
        {
            for (int z = 0; z < blockLength; z++)
                result.Add(new Vector2Int(x, z));
        }

        return result;
    }

    // ---------------------------------------------------------------------
    // Socket generation
    // ---------------------------------------------------------------------

    private void SpawnSockets()
    {
        if (anchorPointPrefab == null)
        {
            Debug.LogError($"{name}: Missing socket prefab.", this);
            return;
        }

        ClearGeneratedSockets();

        List<Vector2Int> cells = GetSocketCells();

        for (int i = 0; i < cells.Count; i++)
            CreateSocket(cells[i]);
    }

    private void CreateSocket(Vector2Int cell)
    {
        if (socketMap.ContainsKey(cell))
            return;

        GameObject socketObject = Instantiate(anchorPointPrefab, transform);
        socketObject.name = $"Socket_{cell.x}_{cell.y}";
        socketObject.transform.localPosition = GetSocketLocalPosition(cell);
        socketObject.transform.localRotation = Quaternion.identity;

        LegoSocket socket = socketObject.GetComponent<LegoSocket>();

        if (socket == null)
        {
            Debug.LogError($"{socketObject.name}: Socket prefab needs a LegoSocket component.", socketObject);
            DestroySocketObject(socketObject);
            return;
        }

        socket.gridX = cell.x;
        socket.gridZ = cell.y;
        socket.parentGrid = this;
        socket.isInnerSocket = true;
        socket.isOccupied = false;

        sockets.Add(socket);
        socketMap[cell] = socket;

        if (hideGeneratedSocketRenderers)
            HideRenderers(socketObject);
    }

    private void ClearGeneratedSockets()
    {
        sockets.Clear();
        socketMap.Clear();

        List<GameObject> childrenToDelete = new List<GameObject>();

        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("Socket_"))
                childrenToDelete.Add(child.gameObject);
        }

        for (int i = 0; i < childrenToDelete.Count; i++)
            DestroySocketObject(childrenToDelete[i]);
    }

    // ---------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------

    private Vector3 GetSocketLocalPosition(Vector2Int cell)
    {
        return new Vector3(
            cell.x * studSpacingX + offsetX,
            socketY,
            cell.y * studSpacingZ + offsetZ
        );
    }

    private LegoSocket GetSocketAt(Vector2Int cell)
    {
        socketMap.TryGetValue(cell, out LegoSocket socket);
        return socket;
    }

    private void SetSocketOccupied(LegoSocket socket, bool occupied)
    {
        if (socket == null)
            return;

        socket.isOccupied = occupied;

        Collider socketCollider = socket.GetComponent<Collider>();

        if (socketCollider != null)
            socketCollider.enabled = !occupied;
    }

    private void CleanCustomSocketCells()
    {
        if (customSocketCells == null)
            customSocketCells = new List<Vector2Int>();

        List<Vector2Int> cleaned = new List<Vector2Int>();

        for (int i = 0; i < customSocketCells.Count; i++)
            AddUnique(cleaned, customSocketCells[i]);

        if (cleaned.Count == 0)
            cleaned.Add(Vector2Int.zero);

        customSocketCells = cleaned;
    }

    private static void AddUnique(List<Vector2Int> list, Vector2Int cell)
    {
        if (!list.Contains(cell))
            list.Add(cell);
    }

    private static RectInt GetBounds(List<Vector2Int> cells)
    {
        int minX = cells[0].x;
        int maxX = cells[0].x;
        int minZ = cells[0].y;
        int maxZ = cells[0].y;

        for (int i = 1; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];

            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minZ) minZ = cell.y;
            if (cell.y > maxZ) maxZ = cell.y;
        }

        return new RectInt(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
    }

    private static void HideRenderers(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;
    }

    private static void DestroySocketObject(GameObject target)
    {
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    // ---------------------------------------------------------------------
    // Optional legacy wrappers
    // Keep these only so older scripts do not break.
    // ---------------------------------------------------------------------

    public bool IsAreaClear(int startX, int startZ, int width, int length)
    {
        return IsFootprintAreaClear(BuildRectangleCells(startX, startZ, width, length));
    }

    public List<LegoSocket> GetSocketsInArea(int startX, int startZ, int width, int length)
    {
        return GetSocketsInFootprint(BuildRectangleCells(startX, startZ, width, length));
    }

    public void SetSocketsOccupiedInArea(int startX, int startZ, int width, int length, bool occupied)
    {
        SetSocketsOccupiedInFootprint(BuildRectangleCells(startX, startZ, width, length), occupied);
    }

    public bool DoesBlockCoverInnerSocket(int startGridX, int startGridZ, LegoBlock block)
    {
        if (block == null)
            return false;

        List<Vector2Int> localFootprint = block.GetStudFootprint();
        List<Vector2Int> absoluteCells = new List<Vector2Int>(localFootprint.Count);

        for (int i = 0; i < localFootprint.Count; i++)
        {
            Vector2Int cell = localFootprint[i];
            absoluteCells.Add(new Vector2Int(startGridX + cell.x, startGridZ + cell.y));
        }

        return DoesFootprintCoverInnerSocket(absoluteCells);
    }

    public bool DoesRotatedBlockCoverInnerSocket(
        int startGridX,
        int startGridZ,
        LegoBlock block,
        float yawOffset
    )
    {
        if (block == null)
            return false;

        int yawStep = Mathf.RoundToInt(yawOffset / 90f);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);
        List<Vector2Int> absoluteCells = new List<Vector2Int>(rotatedFootprint.Count);

        for (int i = 0; i < rotatedFootprint.Count; i++)
        {
            Vector2Int cell = rotatedFootprint[i];
            absoluteCells.Add(new Vector2Int(startGridX + cell.x, startGridZ + cell.y));
        }

        return DoesFootprintCoverInnerSocket(absoluteCells);
    }

    public Vector3 GetBlockCenterWorldPosition(int gridX, int gridZ, LegoBlock block)
    {
        if (block == null)
            return transform.position;

        List<Vector2Int> localFootprint = block.GetStudFootprint();
        List<Vector2Int> absoluteCells = new List<Vector2Int>(localFootprint.Count);

        for (int i = 0; i < localFootprint.Count; i++)
        {
            Vector2Int cell = localFootprint[i];
            absoluteCells.Add(new Vector2Int(gridX + cell.x, gridZ + cell.y));
        }

        return GetFootprintCenterWorldPosition(absoluteCells, block.GetWorldHeight());
    }

    public Vector3 GetBlockCenterWorldPositionRotated(
        int startGridX,
        int startGridZ,
        LegoBlock block,
        float yawOffset
    )
    {
        if (block == null)
            return transform.position;

        int yawStep = Mathf.RoundToInt(yawOffset / 90f);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);
        List<Vector2Int> absoluteCells = new List<Vector2Int>(rotatedFootprint.Count);

        for (int i = 0; i < rotatedFootprint.Count; i++)
        {
            Vector2Int cell = rotatedFootprint[i];
            absoluteCells.Add(new Vector2Int(startGridX + cell.x, startGridZ + cell.y));
        }

        return GetFootprintCenterWorldPosition(absoluteCells, block.GetWorldHeight());
    }

    private static List<Vector2Int> BuildRectangleCells(int startX, int startZ, int width, int length)
    {
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
                cells.Add(new Vector2Int(x, z));
        }

        return cells;
    }

    // ---------------------------------------------------------------------
    // Gizmos
    // ---------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (!drawSocketGizmos)
            return;

        Gizmos.color = Color.green;

        List<Vector2Int> cells = GetSocketCells();

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 world = transform.TransformPoint(GetSocketLocalPosition(cells[i]));
            Gizmos.DrawSphere(world, gizmoSize);
        }
    }
}
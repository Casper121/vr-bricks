using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates and manages LEGO snap sockets for one connected LEGO surface.
///
/// Important:
/// - Use one LegoBlockSocketSpawner per physical top surface (e.g. one per room floor).
/// - Rectangle mode is for normal rectangular surfaces.
/// - CustomCells mode is for irregular / combined surfaces.
/// - For irregular blocks, customSocketCells should usually match the block's
///   LegoBlock.studFootprintCells.
///
/// NEW: Auto Fit To Floor
/// If enabled, blockWidth/blockLength are calculated automatically from the
/// bounds of an assigned floor Collider, so each room can have its own grid
/// size without manually typing numbers in.
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
    [Tooltip("Ignored if Auto Fit To Floor is enabled - the value is calculated automatically instead.")]
    [Min(1)] public int blockWidth = 4;
    [Min(1)] public int blockLength = 2;

    [Header("Auto Fit To Floor (Rectangle mode only)")]
    [Tooltip("If enabled, blockWidth and blockLength are calculated automatically from the floor's size and the stud spacing below, every time sockets are (re)spawned.")]
    [SerializeField] private bool autoFitToFloor = false;

    [Tooltip("Upper bound for blockWidth/blockLength (from Auto Fit To Floor). Since sockets are now created LAZILY (only when actually queried, not all upfront), this is mostly just a sanity cap against absurd numbers - not a hard performance lever anymore. 100-150 is a generous, safe range.")]
    [SerializeField] private int maxGridStuds = 100;

    [Tooltip("PREFERRED: The floor's MeshRenderer. Automatically matches the real visible floor size, no manual sizing needed. Assign this if possible.")]
    [SerializeField] private Renderer floorRenderer;

    [Tooltip("FALLBACK: Only used if Floor Renderer is empty. Warning: a Box Collider added to a non-primitive mesh does NOT auto-size to the mesh - you must manually set its Size/Center to match the real floor, otherwise this will compute a near-zero grid.")]
    [SerializeField] private Collider floorCollider;

    [Tooltip("Small safety margin (in studs) subtracted on each side so sockets don't spawn exactly on/over the room walls.")]
    [SerializeField] private int edgeMarginStuds = 1;

    [Tooltip("Extra rows/columns of sockets added BEYOND the computed floor size on each side, just in case the auto-fit calculation ends up a little too tight at the edges.")]
    [SerializeField] private int extraPaddingStuds = 2;

    [Tooltip("If enabled, offsetX/offsetZ are automatically recalculated every time Auto Fit To Floor runs, so the grid is always centered symmetrically around this spawner's own local origin (0,0,0). Enable this if the spawner sits at the room's physical center - it fixes a grid that looks shifted to one side of the floor.")]
    [SerializeField] private bool autoCenterOnOrigin = true;

    [Tooltip("Multiplies the computed grid size beyond what's needed to just cover the floor when centered. Needed because LegoScaleMenu can anchor scaling to an off-center build instead of the room's center - without extra slack, the grid can fall short of reaching the far wall on one side. IMPORTANT: this multiplies socket count in BOTH X and Z, so 1.3 here means ~1.7x total sockets, 1.6 means ~2.6x. Keep this as LOW as your off-center scaling actually needs - every extra socket costs runtime performance elsewhere (e.g. the ghost/snap search).")]
    [SerializeField] private float offCenterSafetyMultiplier = 1.2f;

    [Header("Grid Transform")]
    public float socketY = 0.2f;
    public float offsetX = -0.2f;
    public float offsetZ = -0.8f;
    public float studSpacingX = 0.5f;
    public float studSpacingZ = 0.5f;

    [Header("Display")]
    [SerializeField] private bool hideGeneratedSocketRenderers = false;
    [SerializeField] private bool drawSocketGizmos = true;

    [Tooltip("If enabled, socket gizmos are visible even when the object is not selected. Use this to debug shifted block sockets in the Scene view.")]
    [SerializeField] private bool drawSocketGizmosAlways = true;

    [Tooltip("Draws every logical socket cell, even if its GameObject was not lazily created yet.")]
    [SerializeField] private bool drawAllLogicalSocketCells = true;

    [SerializeField] private float gizmoSize = 0.04f;

    [Tooltip("Should match LegoScaleMenu's Min Scale value. Used only once, at startup, to compute how large the LOGICAL grid bounds need to be so they never need to shrink later. This is now essentially free (no per-object cost) since sockets are created lazily - see below.")]
    [SerializeField] private float worstCaseReferenceScale = 0.5f;

    [Header("Automatic Cleanup (floor only - keeps lazy loading actually lazy over time)")]
    [Tooltip("If enabled, unoccupied sockets far from the player are periodically destroyed again, so wandering around a large room over time doesn't slowly accumulate thousands of never-used sockets. Occupied sockets (with a block snapped to them) are NEVER removed.")]
    [SerializeField] private bool enableSocketCleanup = true;

    [Tooltip("Assign your XR camera / player root here. Used to measure which sockets are 'far away' and safe to remove. If left empty, cleanup is skipped entirely.")]
    [SerializeField] private Transform playerReferenceForCleanup;

    [Tooltip("How often (seconds) to check for far-away unused sockets to clean up. Doesn't need to be frequent - this is a background tidy-up, not a per-frame cost.")]
    [SerializeField] private float cleanupCheckIntervalSeconds = 2f;

    [Tooltip("Unoccupied sockets further than this from the player get removed. Keep this noticeably LARGER than your ghost/candidate search radius, so sockets aren't destroyed and immediately recreated right at the edge of where you're standing.")]
    [SerializeField] private float cleanupDistance = 12f;

    [Tooltip("How many sockets to check/remove per frame during a cleanup pass. Lower = smoother (no stutter) but a full pass takes longer to finish; higher = faster pass but a bit more frame cost while it runs.")]
    [SerializeField] private int cleanupCheckedPerFrame = 150;

    /// <summary>True as soon as the grid's logical bounds are known (this is now instant - no expensive build phase).</summary>
    public bool IsGridReady { get; private set; }

    private readonly List<LegoSocket> sockets = new List<LegoSocket>();
    private readonly Dictionary<Vector2Int, LegoSocket> socketMap = new Dictionary<Vector2Int, LegoSocket>();

    private void Start()
    {
        if (gridMode == SocketGridMode.Rectangle && autoFitToFloor)
        {
            // FLOOR CASE: potentially huge grid (whole room). Only compute the logical
            // bounds here - no GameObjects yet. Sockets are created lazily, one at a
            // time, the first time a specific cell is actually queried (see the private
            // GetSocketAt below), so the real object count stays proportional to how
            // much area has actually been visited/built on, not the theoretical maximum.
            float normalSpacingX = studSpacingX;
            float normalSpacingZ = studSpacingZ;

            studSpacingX = normalSpacingX * Mathf.Max(0.01f, worstCaseReferenceScale);
            studSpacingZ = normalSpacingZ * Mathf.Max(0.01f, worstCaseReferenceScale);
            AutoFitToFloorNow();

            studSpacingX = normalSpacingX;
            studSpacingZ = normalSpacingZ;

            if (autoCenterOnOrigin && floorRenderer != null)
            {
                Bounds worldBounds = floorRenderer.bounds;
                Vector3 targetLocalCenter = transform.InverseTransformPoint(worldBounds.center);
                CenterGridOn(targetLocalCenter.x, targetLocalCenter.z);
            }

            // Only the floor needs periodic cleanup - block-mounted grids have only a
            // handful of sockets each and are built eagerly, nothing to clean up there.
            if (enableSocketCleanup)
                StartCoroutine(PeriodicSocketCleanup());
        }
        else
        {
            // BLOCK / SMALL GRID CASE: this spawner has a small, fixed number of sockets
            // (e.g. 4 for a 2x2 block's stacking surface) - build them immediately, like
            // before. There is no lag concern here; laziness only matters for the huge
            // Auto Fit To Floor case above.
            SpawnSockets();
        }

        IsGridReady = true;
    }

    /// <summary>
    /// Runs in the background, periodically removing unoccupied sockets that are far
    /// from the player. This is what keeps "lazy loading" actually lazy over a long play
    /// session - without it, wandering around a big room would slowly create sockets
    /// everywhere you've ever been and never remove them again.
    /// 
    /// Occupied sockets (anything with a block snapped to it) are NEVER touched here,
    /// since a placed block's LegoBlock.SnappedSocket reference depends on that exact
    /// socket object continuing to exist.
    /// </summary>
    private IEnumerator PeriodicSocketCleanup()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(1f, cleanupCheckIntervalSeconds));

        while (true)
        {
            yield return wait;

            if (playerReferenceForCleanup == null)
                continue;

            Vector3 refPos = playerReferenceForCleanup.position;
            float keepDistanceSqr = cleanupDistance * cleanupDistance;
            int removedCount = 0;
            int checkedThisFrame = 0;

            for (int i = sockets.Count - 1; i >= 0; i--)
            {
                LegoSocket socket = sockets[i];

                if (socket == null)
                {
                    sockets.RemoveAt(i);
                }
                else if (!socket.isOccupied)
                {
                    float distanceSqr = (socket.transform.position - refPos).sqrMagnitude;

                    if (distanceSqr > keepDistanceSqr)
                    {
                        Vector2Int cell = new Vector2Int(socket.gridX, socket.gridZ);
                        socketMap.Remove(cell);
                        sockets.RemoveAt(i);
                        DestroySocketObject(socket.gameObject);
                        removedCount++;
                    }
                }

                checkedThisFrame++;

                if (checkedThisFrame >= cleanupCheckedPerFrame)
                {
                    checkedThisFrame = 0;
                    yield return null;
                }
            }

            if (removedCount > 0)
                Debug.Log($"{name}: Cleanup removed {removedCount} far-away unused socket(s). {sockets.Count} remain active.", this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        blockWidth = Mathf.Max(1, blockWidth);
        blockLength = Mathf.Max(1, blockLength);
        studSpacingX = Mathf.Max(0.01f, studSpacingX);
        studSpacingZ = Mathf.Max(0.01f, studSpacingZ);
        gizmoSize = Mathf.Max(0.001f, gizmoSize);
        edgeMarginStuds = Mathf.Max(0, edgeMarginStuds);

        CleanCustomSocketCells();
    }
#endif

    [ContextMenu("Respawn Sockets")]
    public void RespawnSockets()
    {
        SpawnSockets();
    }

    /// <summary>
    /// Recalculates offsetX/offsetZ so the grid is centered symmetrically around this
    /// spawner's own local origin (0,0,0). Convenience shortcut for CenterGridOn(0, 0).
    /// </summary>
    [ContextMenu("Center Grid On Origin")]
    public void CenterGridOnOrigin()
    {
        CenterGridOn(0f, 0f);
    }

    /// <summary>
    /// Recalculates offsetX/offsetZ so the grid is centered symmetrically around the
    /// given point, expressed in this spawner's LOCAL space. Use this (instead of
    /// CenterGridOnOrigin) when the floor's visual center does not coincide with the
    /// spawner's own local (0,0) - e.g. because the floor mesh's pivot sits in a corner
    /// rather than at its center.
    /// </summary>
    public void CenterGridOn(float targetLocalX, float targetLocalZ)
    {
        offsetX = targetLocalX - (blockWidth - 1) * studSpacingX * 0.5f;
        offsetZ = targetLocalZ - (blockLength - 1) * studSpacingZ * 0.5f;
    }

    /// <summary>
    /// FAST scale-change update: repositions all EXISTING sockets using the current
    /// studSpacingX/Z, offsetX/offsetZ and socketY values, WITHOUT destroying or
    /// recreating any GameObjects. This keeps every socket's identity (and therefore
    /// every snapped block's SnappedSocket reference) intact.
    /// 
    /// Call this from LegoScaleMenu on every scale change instead of RespawnSockets().
    /// Only use RespawnSockets() once, when the sockets are first built (ideally sized
    /// generously enough via AutoFitToFloorNow at the smallest possible build scale so
    /// it never needs to be rebuilt again).
    /// </summary>
    public void RepositionSockets()
    {
        for (int i = 0; i < sockets.Count; i++)
        {
            LegoSocket socket = sockets[i];

            if (socket == null)
                continue;

            Vector2Int cell = new Vector2Int(socket.gridX, socket.gridZ);
            socket.transform.localPosition = GetSocketLocalPosition(cell);
        }
    }

    // ---------------------------------------------------------------------
    // Auto Fit To Floor
    // ---------------------------------------------------------------------

    /// <summary>
    /// Calculates blockWidth/blockLength from floorCollider's local-space size
    /// divided by the stud spacing. Only used in Rectangle mode.
    /// </summary>
    [ContextMenu("Auto Fit To Floor Now")]
    public void AutoFitToFloorNow()
    {
        Bounds? worldBoundsNullable = GetFloorWorldBounds();

        if (worldBoundsNullable == null)
        {
            Debug.LogWarning($"{name}: Auto Fit To Floor is enabled but neither floorRenderer nor floorCollider is assigned.", this);
            return;
        }

        Bounds worldBounds = worldBoundsNullable.Value;

        float worldWidth = worldBounds.size.x;
        float worldDepth = worldBounds.size.z;

        // Convert world size to local units of this transform (accounts for parent scale).
        float scaleX = Mathf.Max(0.0001f, transform.lossyScale.x);
        float scaleZ = Mathf.Max(0.0001f, transform.lossyScale.z);

        float localWidth = worldWidth / scaleX;
        float localDepth = worldDepth / scaleZ;

        int computedWidth = Mathf.FloorToInt(localWidth / studSpacingX) - edgeMarginStuds * 2 + extraPaddingStuds * 2;
        int computedLength = Mathf.FloorToInt(localDepth / studSpacingZ) - edgeMarginStuds * 2 + extraPaddingStuds * 2;

        // Extra slack beyond the "perfectly centered" size, so an off-center scaling
        // anchor (e.g. LegoScaleMenu anchoring to a build near one wall instead of the
        // room's exact center) still leaves the grid reaching every wall, not just the
        // side closer to the anchor.
        computedWidth = Mathf.CeilToInt(computedWidth * Mathf.Max(1f, offCenterSafetyMultiplier));
        computedLength = Mathf.CeilToInt(computedLength * Mathf.Max(1f, offCenterSafetyMultiplier));

        blockWidth = Mathf.Max(1, computedWidth);
        blockLength = Mathf.Max(1, computedLength);

        // HARD CAP: never let the grid exceed maxGridStuds in either dimension. This is
        // the primary safeguard against the socket count exploding into the tens/hundreds
        // of thousands on a large room combined with a small worst-case scale. If the
        // floor is bigger than this cap covers, the buildable area simply becomes a
        // centered zone smaller than the full room - a deliberate, necessary trade-off.
        if (blockWidth > maxGridStuds || blockLength > maxGridStuds)
        {
            Debug.LogWarning($"{name}: Auto Fit To Floor wanted {blockWidth}x{blockLength} studs, but Max Grid Studs caps it at {maxGridStuds}x{maxGridStuds}. The buildable area will be smaller than the full room. Raise Max Grid Studs if you need more (at the cost of performance), or reduce Worst Case Reference Scale / Off Center Safety Multiplier.", this);
        }

        blockWidth = Mathf.Min(blockWidth, maxGridStuds);
        blockLength = Mathf.Min(blockLength, maxGridStuds);

        if (autoCenterOnOrigin)
        {
            // IMPORTANT: Center on the floor's ACTUAL measured bounds center, not blindly
            // on this spawner's own local (0,0). If the floor mesh's pivot isn't at its
            // own visual center (very common with imported models - pivot often sits in
            // a corner), centering on the spawner origin would be wrong even though the
            // spawner GameObject itself sits at world (0,0,0).
            Vector3 targetLocalCenter = transform.InverseTransformPoint(worldBounds.center);
            CenterGridOn(targetLocalCenter.x, targetLocalCenter.z);
        }

        Debug.Log($"{name}: Auto Fit To Floor -> blockWidth={blockWidth}, blockLength={blockLength} (measured floor size: {worldWidth:F2} x {worldDepth:F2}m, floor center local: {transform.InverseTransformPoint(worldBounds.center)})", this);
    }

    private Bounds? GetFloorWorldBounds()
    {
        if (floorRenderer != null)
            return floorRenderer.bounds;

        if (floorCollider != null)
            return floorCollider.bounds;

        return null;
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

        Vector3 localXZ = new Vector3(
            midX * studSpacingX + offsetX,
            0f,
            midZ * studSpacingZ + offsetZ
        );

        Vector3 worldXZ = transform.TransformPoint(localXZ);

        float worldY = transform.position.y + socketY + blockWorldHeight;

        return new Vector3(worldXZ.x, worldY, worldXZ.z);
    }

    public LegoSocket GetSocketAt(int x, int z)
    {
        return GetSocketAt(new Vector2Int(x, z));
    }

    /// <summary>
    /// Fast alternative to scanning every socket in the scene: returns only the sockets
    /// within the given grid-index radius around (centerGridX, centerGridZ), using direct
    /// dictionary lookups. Cost is roughly (2*radius)^2, completely independent of how
    /// many thousands of sockets exist in total elsewhere on this grid.
    /// 
    /// Use this instead of "get all sockets then filter by distance" wherever possible -
    /// e.g. from LegoBlockGhostManager's held-block candidate search - to avoid the search
    /// cost scaling with total grid size (which matters a lot once Auto Fit To Floor /
    /// worst-case building creates tens of thousands of sockets).
    /// </summary>
    public List<LegoSocket> GetSocketsNearIndex(float centerGridX, float centerGridZ, float radiusX, float radiusZ)
    {
        List<LegoSocket> result = new List<LegoSocket>();

        int minX = Mathf.FloorToInt(centerGridX - radiusX);
        int maxX = Mathf.CeilToInt(centerGridX + radiusX);
        int minZ = Mathf.FloorToInt(centerGridZ - radiusZ);
        int maxZ = Mathf.CeilToInt(centerGridZ + radiusZ);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                LegoSocket socket = GetSocketAt(x, z);

                if (socket != null)
                    result.Add(socket);
            }
        }

        return result;
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

        if (gridMode == SocketGridMode.Rectangle && autoFitToFloor)
            AutoFitToFloorNow();

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
        if (socketMap.TryGetValue(cell, out LegoSocket existing))
            return existing;

        if (!IsCellWithinGridBounds(cell))
            return null;

        // LAZY CREATION: this cell is logically valid but has never been asked for
        // before - create its socket GameObject right now, on the spot. This is the key
        // optimization: instead of pre-building potentially tens of thousands of sockets
        // up front, only the (much smaller) subset that is ever actually queried - near
        // the player, near a held block, near a placed block - ever becomes a real object.
        CreateSocket(cell);
        socketMap.TryGetValue(cell, out LegoSocket created);

        return created;
    }

    private bool IsCellWithinGridBounds(Vector2Int cell)
    {
        if (gridMode == SocketGridMode.CustomCells)
        {
            CleanCustomSocketCells();
            return customSocketCells.Contains(cell);
        }

        return cell.x >= 0 && cell.x < blockWidth && cell.y >= 0 && cell.y < blockLength;
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

    private void OnDrawGizmos()
    {
        if (!drawSocketGizmosAlways)
            return;

        DrawSocketGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (drawSocketGizmosAlways)
            return;

        DrawSocketGizmos();
    }

    private void DrawSocketGizmos()
    {
        if (!drawSocketGizmos)
            return;

        List<Vector2Int> cells = GetSocketCells();

        // Draw every logical socket position. This shows the real grid even when
        // sockets are created lazily and no Socket_ objects exist yet.
        if (drawAllLogicalSocketCells && cells != null)
        {
            Gizmos.color = Color.cyan;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector3 worldPosition = transform.TransformPoint(GetSocketLocalPosition(cells[i]));
                Gizmos.DrawSphere(worldPosition, gizmoSize);
            }
        }

        // Draw the physically created socket objects in green.
        Gizmos.color = Color.green;

        for (int i = 0; i < sockets.Count; i++)
        {
            if (sockets[i] == null)
                continue;

            Gizmos.DrawWireSphere(sockets[i].transform.position, gizmoSize * 1.35f);
        }

        // Draw cell (0,0) in red. This is the important anchor/reference socket.
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.TransformPoint(GetSocketLocalPosition(Vector2Int.zero)), gizmoSize * 1.8f);

        // Draw the full logical grid bounds.
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);

        if (cells != null && cells.Count > 0)
        {
            RectInt bounds = GetBounds(cells);

            Vector3 corner00 = transform.TransformPoint(GetSocketLocalPosition(new Vector2Int(bounds.xMin, bounds.yMin)));
            Vector3 corner10 = transform.TransformPoint(GetSocketLocalPosition(new Vector2Int(bounds.xMax - 1, bounds.yMin)));
            Vector3 corner01 = transform.TransformPoint(GetSocketLocalPosition(new Vector2Int(bounds.xMin, bounds.yMax - 1)));
            Vector3 corner11 = transform.TransformPoint(GetSocketLocalPosition(new Vector2Int(bounds.xMax - 1, bounds.yMax - 1)));

            Gizmos.DrawLine(corner00, corner10);
            Gizmos.DrawLine(corner10, corner11);
            Gizmos.DrawLine(corner11, corner01);
            Gizmos.DrawLine(corner01, corner00);
        }
    }
}
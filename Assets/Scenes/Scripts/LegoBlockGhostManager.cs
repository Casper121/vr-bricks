using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Handles LEGO block snapping, ghost preview, placement validation,
/// held-block rotation, and socket occupation.
/// 
/// Works with arbitrary stud footprints (see LegoBlock.StudFootprint), so this
/// supports both simple rectangular blocks (2x2, 1x4, ...) and non-rectangular
/// blocks (e.g. an L-shaped block made of a 1x2 plus an extra 1x1 stud).
/// 
/// Important XR setup for the block:
/// - XR Grab Interactable Movement Type: Velocity Tracking
/// - Track Position: enabled
/// - Track Rotation: disabled
/// 
/// This allows Unity physics to move the block while this script controls
/// 90-degree yaw rotation manually.
/// </summary>
[RequireComponent(typeof(LegoBlock))]
[RequireComponent(typeof(Rigidbody))]
public class LegoBlockGhostManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector: Ghost Settings
    // -------------------------------------------------------------------------

    [Header("Ghost Settings")]
    [Tooltip("Maximum horizontal distance from a socket before the block can snap.")]
    [SerializeField] private float snapDistanceThreshold = 1.60f;

    [Tooltip("Maximum allowed X/Z axis offset from the target socket.")]
    [SerializeField] private float maxAxisOffset = 2.25f;

    [Tooltip("Multiplier used to keep an already locked socket reachable slightly longer.")]
    [SerializeField] private float releaseDistanceMultiplier = 1.1f;

    [Tooltip("How much better a new candidate must be before the ghost switches to it. Higher = less flicker, lower = more responsive.")]
    [SerializeField] private float candidateSwitchMargin = 0.22f;

    [Header("Performance / Large Block Accuracy")]
    [Tooltip("Minimum time between expensive full snap searches while a block is held. 0.02 = 50 searches/sec, 0.03 = 33 searches/sec. This removes VR lag from huge socket grids.")]
    [SerializeField] private float snapSearchInterval = 0.025f;

    [Tooltip("Only tests sockets near the held block instead of every socket in the scene. Keep this ON for large floor grids.")]
    [SerializeField] private bool useNearbySocketPruning = true;

    [Tooltip("Extra search range around the held block footprint, in studs. Increase to 3 if ghosts disappear too early at the edge of big blocks.")]
    [SerializeField] private int nearbySocketExtraRadiusInStuds = 5;

    [Tooltip("How often the global socket list is refreshed. Keeps height-based all-level scanning from calling FindObjectsOfType every frame.")]
    [SerializeField] private float socketCacheRefreshInterval = 0.35f;

    [Header("Current Candidate Smoothing")]
    [Tooltip("Keeps the current valid socket while it is still actually valid. Prevents ghost disappearing on tiny hand movement.")]
    [SerializeField] private bool keepCurrentValidCandidate = true;

    [Tooltip("How far the current valid candidate may remain active before searching forces a switch.")]
    [SerializeField] private float currentCandidateHoldDistanceMultiplier = 1.45f;


    [Header("Ghost Level Selection")]
    [Tooltip("If a candidate is invalid/red, no ghost is shown. This prevents red ghosts inside/halfway through blocks.")]
    [SerializeField] private bool hideInvalidGhostPreview = true;

    [Tooltip("Uses hand/block height to choose between lower and upper socket levels when several blocks are nearby.")]
    [SerializeField] private bool useVerticalLevelSelection = true;

    [Tooltip("How strongly vertical distance affects socket selection. Higher = better top/bottom level recognition.")]
    [SerializeField] private float verticalLevelWeight = 0.85f;

    [Tooltip("If enabled, the held block height decides whether floor sockets or upper block sockets win. This prevents the highest raycast surface from stealing placement.")]
    [SerializeField] private bool useHeldHeightForLevelSelection = true;

    [Tooltip("When height based level selection is active, candidates farther away from the held block height are ignored. 0 = disabled.")]
    [SerializeField] private float maxHeldHeightDifference = 1.35f;


    [Tooltip("How far below a socket the held block center may be while the socket is still considered reachable.")]
    [SerializeField] private float allowedBelowSocket = 0.45f;

    [Header("Surface Targeting")]
    [Tooltip("If enabled, snapping first chooses the top surface under the held block by raycast, then searches only sockets on that surface. This prevents floor/lower block sockets from stealing the ghost through plates.")]
    [SerializeField] private bool useSurfaceRaycastTargeting = true;

    [Tooltip("How high above the held block root the downward surface ray starts.")]
    [SerializeField] private float surfaceRayStartHeight = 1.0f;

    [Tooltip("How far downward the surface ray searches.")]
    [SerializeField] private float surfaceRayDistance = 4.0f;

    [Tooltip("Layers that can be hit as snap surfaces. Put LEGO blocks and floor grid on these layers. Everything is okay for testing.")]
    [SerializeField] private LayerMask surfaceRayMask = ~0;

    [Tooltip("Draws/logs which surface grid was selected.")]
    [SerializeField] private bool debugSurfaceTargeting = false;

    [Tooltip("TEMPORARY DIAGNOSTIC: logs grid position + bounds whenever no valid placement candidate can be found at all. Turn on to check whether 'impossible to place' spots are near the edge of the floor grid. Turn off afterward, it spams the console.")]
    [SerializeField] private bool debugPlacementFailures = false;

    private float nextAllowedFailureLogTime;

    [Header("Floor Socket Blocking")]
    [Tooltip("If enabled, floor sockets can also be blocked by a short physical raycast. Leave this OFF if free cells next to blocks sometimes become unsnappable.")]
    [SerializeField] private bool usePhysicalFloorSocketBlocker = false;

    [Tooltip("Only used when Use Physical Floor Socket Blocker is enabled. Keep this small, otherwise neighboring floor sockets may be blocked by oversized colliders.")]
    [SerializeField] private float floorSocketBlockerRayDistance = 0.35f;

    [Tooltip("Small lift from the floor socket before checking upward for a directly covering block.")]
    [SerializeField] private float floorSocketBlockerStartLift = 0.03f;

    [Tooltip("Absolute UPPER CAP (world units) for the rotation anchor unlock distance - see Rotation Anchor Unlock Stud Fraction below for the actual scale-aware value used in practice.")]
    [SerializeField] private float rotationAnchorUnlockDistance = 0.35f;

    [Tooltip("The rotation anchor lock releases once the hand has moved this FRACTION of one stud's spacing, instead of a fixed world-space distance. This keeps the lock feeling consistent regardless of block/scale size - a fixed distance like 35cm can block ALL normal hand movement between adjacent studs on small/scaled-down blocks, making points feel 'skipped'. 0.4-0.6 is a good range: enough to suppress tiny jitter right after rotating, but small enough that real repositioning still registers.")]
    [SerializeField] private float rotationAnchorUnlockStudFraction = 0.9f;

    [Tooltip("Ghost color for valid placement.")]
    [SerializeField] private Color ghostColorValid = new Color(0.3f, 0.6f, 1f, 0.4f);

    [Tooltip("Ghost color for invalid placement.")]
    [SerializeField] private Color ghostColorInvalid = new Color(1f, 0.2f, 0.2f, 0.4f);

    [Header("Collision Blocking")]
    [Tooltip("If enabled, the ghost becomes invalid when the block body would intersect another already placed block.")]
    [SerializeField] private bool preventGhostIntersectingBlocks = true;

    [Tooltip("Physics layers that contain LEGO block colliders. Leave on Everything if unsure, but it is better to use a LEGO layer.")]
    [SerializeField] private LayerMask blockCollisionMask = ~0;

    [Tooltip("Shrinks the overlap test slightly so touching faces are allowed, but real intersections are blocked.")]
    [SerializeField] private float collisionCheckShrink = 0.05f;

    [Tooltip("Small vertical tolerance so a block can sit on the support block without being counted as intersecting it.")]
    [SerializeField] private float verticalTouchTolerance = 0.05f;

    [Header("False Red Ghost Fix")]
    [Tooltip("Uses more forgiving overlap values for scaled blocks so valid snap places do not turn red.")]
    [SerializeField] private bool useFalseRedGhostFix = true;

    [Tooltip("V2 shrink for the collision preview box. Higher = less false red. This does not change the real block size.")]
    [SerializeField] private float collisionCheckShrinkV2 = 0.10f;

    [Tooltip("V2 vertical tolerance for stacked/touching blocks. Higher = less false red on valid top placements.")]
    [SerializeField] private float verticalTouchToleranceV2 = 0.12f;

    [Tooltip("Extra tolerance for blocks that are below the candidate and act as neighbouring support. This fixes bridge/rand placements across two side-by-side blocks where the second support block was counted as a collision because its studs protrude slightly into the preview box.")]
    [SerializeField] private float neighbourSupportVerticalTolerance = 0.30f;


    [Header("Plate Scaled SocketY Fix")]
    [Tooltip("Fixes plate grids where socketY is negative. It makes socketY scale with the support block instead of staying constant.")]
    [SerializeField] private bool useScaledNegativeSocketYFix = true;

    [Tooltip("1 = mathematically correct compensation. Lower if plates become too high, higher if plates stay too low.")]
    [SerializeField] private float scaledNegativeSocketYMultiplier = 1.0f;

    [Tooltip("Tiny final plate-only offset. Negative = lower, positive = higher. Start with 0.")]
    [SerializeField] private float scaledNegativeSocketYFineTune = 0.0f;


    // -------------------------------------------------------------------------
    // Inspector: Visual Root
    // -------------------------------------------------------------------------

    [Header("Visual Root")]
    [Tooltip("Optional visual root. If empty, the script searches for a child named 'VisualRoot'.")]
    [SerializeField] private Transform visualRoot;

    // -------------------------------------------------------------------------
    // Public State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Current yaw offset in degrees. Usually 0, 90, 180, or 270.
    /// </summary>
    public float CurrentYawOffset { get; private set; }

    /// <summary>
    /// Current socket that the held block is locked onto for preview.
    /// This is the socket under the block's footprint anchor stud (local cell 0,0).
    /// </summary>
    public LegoSocket TargetSocket { get; private set; }

    // -------------------------------------------------------------------------
    // Runtime References
    // -------------------------------------------------------------------------

    private XRGrabInteractable grabInteractable;
    private Rigidbody rb;
    private LegoBlock block;

    // -------------------------------------------------------------------------
    // Runtime State: Ghost and Sockets
    // -------------------------------------------------------------------------

    private GameObject ghostRoot;
    private readonly List<MeshRenderer> ghostRenderers = new List<MeshRenderer>();
    private Material ghostValidMaterial;
    private Material ghostInvalidMaterial;
    private bool ghostLastValidState;

    private LegoSocket currentSocket;
    private readonly List<LegoSocket> currentOccupiedSockets = new List<LegoSocket>();
    private readonly List<LegoBlock> currentlyNotifiedSupportBlocks = new List<LegoBlock>();
    private readonly List<LegoSocketInteractor> allSocketInteractors = new List<LegoSocketInteractor>();

    // -------------------------------------------------------------------------
    // Runtime State: Visuals and Rotation
    // -------------------------------------------------------------------------

    private Vector3 visualRootOriginalLocalPosition;
    private Quaternion visualRootOriginalLocalRotation;
    private Quaternion heldBaseRotation;

    // -------------------------------------------------------------------------
    // Runtime State: Placement
    // -------------------------------------------------------------------------

    private bool wasHeld;
    private readonly List<Collider> heldOwnColliders = new List<Collider>();
    private readonly HashSet<Collider> ignoredOtherColliders = new HashSet<Collider>();
    private LegoSocket lastLoggedCandidateSocket;
    private Vector2Int lastKnownPivotForTargetSocket;
    private bool currentPlacementValid;
    private float lastBuiltYaw = -999f;

    private bool hasActiveCandidate;
    private SnapCandidate activeCandidate;
    private float nextAllowedSnapSearchTime;
    private LegoSocket[] cachedAllSockets = new LegoSocket[0];
    private float nextSocketCacheRefreshTime;

    // While true, the every-frame search keeps the current activeCandidate's
    // footprint anchor instead of jumping to a different nearby socket. This is
    // set right after a rotation (O press) and released once the hand has moved
    // far enough. This is what makes "rotate in place" feel stable.
    private bool rotationAnchorLockActive;

    [Tooltip("After rotating, no other candidate search (full or lightweight) runs at all until EITHER this many seconds have passed AND the hand has moved past rotationSearchGraceMoveDistance. RotateActiveCandidateInPlace's own result is left completely untouched during this window, which is what actually guarantees rotation can't get silently overridden mid-sequence - including no automatic switching to a different valid socket while you're just holding still looking at a red (invalid) ghost.")]
    [SerializeField] private float rotationSearchGracePeriod = 0.15f;
    private float rotationSearchGracePeriodEndTime;

    [Tooltip("How far the hand needs to move (in world units) after rotating before any other search is allowed to resume, in addition to the time-based grace period above.")]
    [SerializeField] private float rotationSearchGraceMoveDistance = 0.15f;
    private Vector3 rotationSearchGraceHandPosition;
    private Vector3 rotationAnchorLockHandPosition;

    // Rotation is queued and applied from HandleHeldState only.
    // This prevents a short wrong/opposite ghost flash caused by rebuilding
    // once from the input callback and again from the normal every-frame search.
    private int pendingRotationSteps;
    private int lastRotationRequestFrame = -999;

    private LegoBlock temporarilyStabilizedBlock;

    // -------------------------------------------------------------------------
    // Runtime State: Ghost Mesh Cache
    // -------------------------------------------------------------------------

    private readonly List<MeshSnapshot> meshSnapshots = new List<MeshSnapshot>();

    private struct MeshSnapshot
    {
        public Mesh mesh;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public MeshRenderer originalRenderer;
    }

    /// <summary>
    /// A possible placement of the whole block.
    ///
    /// FIX: anchorGridX/anchorGridZ used to be treated as "the grid position of
    /// footprint cell (0,0), held fixed across rotation" - that is mathematically
    /// stable, but cell (0,0) is usually just one arbitrary corner of the
    /// footprint, not necessarily the stud that is actually anchored to the
    /// socket. When rotating, the OLD code recomputed pivotCell as
    /// (socket.gridX - anchorGridX), which after a rotation is no longer even a
    /// member of the NEW rotated footprint's cell list - i.e. it silently
    /// invented a cell that doesn't exist on the rotated block anymore. The
    /// footprint would then be positioned so cell (0,0) stays put, while the
    /// stud that was actually supposed to stay on the socket swings away to a
    /// completely different spot. That is the exact "shift on rotate" bug.
    ///
    /// trueLocalPivotCell fixes this: it stores the block's OWN unrotated local
    /// footprint cell (from LegoBlock.GetStudFootprint(), BEFORE any rotation)
    /// that is actually anchored to the socket. On every rotation, this fixed
    /// local cell is re-rotated to the new yaw (RotateSingleCell) and
    /// anchorGridX/anchorGridZ are recomputed from THAT - so the physical stud
    /// that is touching the socket stays touching the socket, and the rest of
    /// the footprint (including cell 0,0) rotates around it instead.
    /// </summary>
    private struct SnapCandidate
    {
        public LegoSocket socket;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public float distance;
        public int anchorGridX;
        public int anchorGridZ;
        public Vector2Int pivotCell;
        public Vector2Int trueLocalPivotCell;
        public List<Vector2Int> rotatedFootprint;
        public LegoBlockSocketSpawner parentGrid;
        public int yawStepWhenBuilt;
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        block = GetComponent<LegoBlock>();

        ApplyResponsiveCandidateTuning();

        FindVisualRoot();
        StoreVisualRootDefaults();
        RefreshSocketInteractors();
        FindCurrentSocket();
    }

    /// <summary>
    /// Forces the candidate-hysteresis tuning values to a more responsive/intuitive
    /// feel on EVERY block, regardless of whatever value happens to be serialized in
    /// the Inspector on a given prefab. Previously the ghost could keep "sticking" to
    /// a previously-targeted socket even when the block was clearly held over a
    /// different, closer one, because the old candidate was allowed to stay active
    /// almost as good as (candidateSwitchMargin) or reachable within 1.45x
    /// (currentCandidateHoldDistanceMultiplier) the normal snap distance. Lowering
    /// these makes the ghost track the closest valid socket much more directly.
    ///
    /// Remove this call (and this method) if you want per-block Inspector control again.
    /// </summary>
    private void ApplyResponsiveCandidateTuning()
    {
        // FIX: 0.05 was too small a margin - real, structural near-ties between
        // two valid pivot choices (observed consistently at a ~0.28 distance
        // gap, e.g. an anchor at cell (0,0) vs a neighboring stud on the same
        // footprint) could make the hysteresis check in the "keep current
        // candidate" logic fail to protect the currently-locked pivot,
        // letting a fresh search's "equally good" alternative silently win -
        // exactly what caused the block to jump to a different stud partway
        // through a sequence of in-place rotations. Raising this well above
        // that gap makes "stick with the pivot you already have" reliably win
        // over a near-tied alternative, while still being small enough that a
        // GENUINELY closer socket (a real, deliberate hand movement) still
        // takes over normally.
        // FIX: 1.05 was too tight - legitimate candidates (especially for
        // larger/asymmetric footprints, or when tracking a non-origin pivot
        // cell) regularly measured distances of 0.64-0.75 against a
        // maxHoldDistance of only ~0.63, causing KeepCurrentCandidateLightweight
        // to silently drop a perfectly valid, correctly-tracked candidate mid-
        // rotation-sequence purely because of this too-tight distance cutoff -
        // not because anything was actually wrong with it.
        // Tuned down slightly from 0.4/3.0 - those values fixed the original
        // bugs (a real ~0.28 tie-gap between pivots, and distances up to ~0.75
        // for legitimate candidates) but made normal aiming feel sticky/
        // unresponsive as a side effect. These are still comfortably above
        // both those thresholds while feeling snappier day to day.
        candidateSwitchMargin = 0.3f;
        currentCandidateHoldDistanceMultiplier = 2.0f;
    }

    /// <summary>
    /// Returns the world position that all candidate-search / distance math should be
    /// measured from.
    ///
    /// Normally this is just transform.position (the block's own physical position,
    /// correct for direct hand-grab). But when the block is currently being held by
    /// an XRRayInteractor (distance placement via laser pointer), the block's own
    /// transform does not necessarily sit exactly at the spot the ray is currently
    /// pointing at - so instead we ask the ray interactor for its LIVE current
    /// raycast hit point and use that instead. This makes the ghost preview track
    /// wherever the pointer is actually aiming, instead of the held object's own
    /// (possibly lagging/offset) position.
    /// </summary>
    private Vector3 GetHeldReferencePosition()
    {
        if (grabInteractable != null)
        {
            var interactorsSelecting = grabInteractable.interactorsSelecting;

            for (int i = 0; i < interactorsSelecting.Count; i++)
            {
                XRRayInteractor rayInteractor = interactorsSelecting[i] as XRRayInteractor;

                if (rayInteractor == null)
                    continue;

                if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                    return hit.point;
            }
        }

        return transform.position;
    }

    /// <summary>
    /// TEMPORARY DIAGNOSTIC: prints the held grid position, the nearest floor grid's
    /// bounds, and the current rotation whenever placement fails. Rate-limited to
    /// avoid spamming the console every frame. Enable "Debug Placement Failures" in
    /// the Inspector to use this; the resulting log tells you whether a failed
    /// placement sits right at (or past) the edge of the floor grid's valid
    /// [0, blockWidth) x [0, blockLength) index range, which is a hard cutoff -
    /// cells outside it never exist, regardless of the block's rotation.
    /// </summary>
    private void LogPlacementFailureDebugInfo(string reason)
    {
        if (Time.time < nextAllowedFailureLogTime)
            return;

        nextAllowedFailureLogTime = Time.time + 0.5f;

        LegoBlockSocketSpawner[] allSpawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        int yawStep = GetEffectiveYawStep();

        foreach (LegoBlockSocketSpawner spawner in allSpawners)
        {
            if (spawner == null || spawner.GetComponentInParent<LegoBlock>() != null)
                continue; // only report on floor-level grids, same ones used for placement

            float safeSpacingX = Mathf.Max(0.0001f, spawner.studSpacingX);
            float safeSpacingZ = Mathf.Max(0.0001f, spawner.studSpacingZ);

            Vector3 localHeld = spawner.transform.InverseTransformPoint(GetHeldReferencePosition());
            float heldGridX = (localHeld.x - spawner.offsetX) / safeSpacingX;
            float heldGridZ = (localHeld.z - spawner.offsetZ) / safeSpacingZ;

            bool nearMinX = heldGridX < 2f;
            bool nearMaxX = heldGridX > spawner.blockWidth - 2f;
            bool nearMinZ = heldGridZ < 2f;
            bool nearMaxZ = heldGridZ > spawner.blockLength - 2f;
            bool nearAnyEdge = nearMinX || nearMaxX || nearMinZ || nearMaxZ;

            Debug.Log(
                "[PLACEMENT FAIL] " + reason +
                " | grid=" + spawner.name +
                " yawStep=" + yawStep +
                " heldGridX=" + heldGridX.ToString("F2") +
                " heldGridZ=" + heldGridZ.ToString("F2") +
                " gridBounds=[0.." + spawner.blockWidth + ") x [0.." + spawner.blockLength + ")" +
                " nearEdge=" + nearAnyEdge +
                (nearAnyEdge ? " (X:" + (nearMinX ? "min" : nearMaxX ? "max" : "-") + " Z:" + (nearMinZ ? "min" : nearMaxZ ? "max" : "-") + ")" : "")
            );
        }
    }

    /// <summary>
    /// TEMPORARY DIAGNOSTIC: pinpoints exactly which sub-check rejected a candidate,
    /// instead of just "invalid". Enable "Debug Placement Failures" to see this.
    /// </summary>
    private void LogCandidateInvalidReason(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
        {
            Debug.Log("[PLACEMENT FAIL REASON] candidate has no socket/parentGrid at all.");
            return;
        }

        bool socketUsable = IsSocketUsableForInitialSearch(candidate.socket);
        List<Vector2Int> cells = GetAbsoluteCells(candidate);
        bool coversInnerSocket = candidate.parentGrid.DoesFootprintCoverInnerSocket(cells);
        bool areaClear = candidate.parentGrid.IsFootprintAreaClear(cells);
        bool wouldIntersect = CandidateWouldIntersectOtherBlocks(candidate);

        Debug.Log(
            "[PLACEMENT FAIL REASON] socketUsable=" + socketUsable +
            " (false = socket occupied/below-hand/hidden-under-block)" +
            " | coversInnerSocket=" + coversInnerSocket +
            " (false = footprint doesn't land on any real socket at all)" +
            " | areaClear=" + areaClear +
            " (false = a touched socket is already occupied)" +
            " | wouldIntersectOtherBlocks=" + wouldIntersect +
            " (true = PHYSICAL COLLISION with another block's collider - this is your 'collides' case)" +
            " | cellCount=" + (cells != null ? cells.Count : 0)
        );
    }

    /// <summary>
    /// TEMPORARY DIAGNOSTIC: logs exactly where an ACCEPTED (rendered) ghost candidate
    /// is being placed, and which grid it came from. Useful for tracking down "ghost is
    /// floating in the air" type bugs, where the X/Z targeting is fine but the resulting
    /// world Y position doesn't match any real surface. Only logs when the candidate's
    /// socket actually changes, so it won't spam every frame.
    /// </summary>
    private void LogAcceptedCandidateDebugInfo(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return;

        LegoBlockSocketSpawner grid = candidate.parentGrid;
        LegoBlock ownerBlock = grid.GetComponentInParent<LegoBlock>();

        Debug.Log(
            "[GHOST ACCEPTED] grid=" + grid.name +
            " gridOwnerBlock=" + (ownerBlock != null ? ownerBlock.name : "Floor/NoOwner") +
            " gridTransformY=" + grid.transform.position.y.ToString("F3") +
            " socketY=" + grid.socketY.ToString("F3") +
            " blockWorldHeight=" + block.GetWorldHeight().ToString("F3") +
            " candidateWorldPosition=" + candidate.worldPosition.ToString("F3") +
            " anchorGridX=" + candidate.anchorGridX +
            " anchorGridZ=" + candidate.anchorGridZ +
            " heldBaseRotationEuler=" + heldBaseRotation.eulerAngles.ToString("F2") +
            " currentYawOffset=" + CurrentYawOffset.ToString("F2") +
            " candidateWorldRotationEuler=" + candidate.worldRotation.eulerAngles.ToString("F2") +
            " rayHitPoint=" + GetHeldReferencePosition().ToString("F3")
        );
    }

    /// <summary>
    /// Returns the current global LEGO build scale (from LegoScaleMenu.CurrentScale, or
    /// 1 if no scale menu is present/active). Used to scale distance-tolerance fields
    /// (snapDistanceThreshold, maxAxisOffset, candidateSwitchMargin, etc.) so their FEEL
    /// stays consistent regardless of block size - a fixed world-space tolerance tuned
    /// for normal-size blocks becomes wildly oversized (and overly "sticky"/unresponsive)
    /// once blocks are scaled down, since it then covers several stud-widths instead of
    /// a small fraction of one.
    /// </summary>
    private float GetScaleAwareFactor()
    {
        // IMPORTANT: capped at 1. This should only ever SHRINK the tolerances for
        // smaller-than-normal blocks (fixing the "sticky at small scale" issue) - it
        // must NEVER grow them beyond their original tuned values at scale >= 1, or
        // placement gets sloppy enough that blocks can snap into overlapping positions.
        return Mathf.Clamp(LegoScaleMenu.CurrentScale, 0.01f, 1f);
    }

    /// <summary>
    /// Plate seam support probes are different from general snap tolerances:
    /// they MUST grow when the build is scaled up, otherwise the support probe /
    /// vertical forgiveness that works at scale 1 becomes too small to catch the
    /// collider of a larger scaled plate at a seam. Do not use this for normal
    /// snap distance, only for plate/support collision allowance.
    /// </summary>
    private float GetPlateSeamScaleFactor()
    {
        return Mathf.Max(1f, Mathf.Abs(LegoScaleMenu.CurrentScale));
    }

    private void Start()
    {
        CacheMeshSnapshots();
    }

    private void Update()
    {
        bool isHeld = grabInteractable != null && grabInteractable.isSelected;

        if (!isHeld && wasHeld)
        {
            OnRelease();
            wasHeld = false;
            return;
        }

        if (!isHeld)
        {
            ClearTemporaryStabilization();
            ClearTargetAndGhost();
            return;
        }

        HandleHeldState();
    }

    private void LateUpdate()
    {
        if (!wasHeld)
            return;

        ApplyHeldRootRotation();
    }

    // -------------------------------------------------------------------------
    // Public API: Rotation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queues a clockwise 90-degree rotation, applied centrally in HandleHeldState.
    /// </summary>
    public void RotateClockwise()
    {
        QueueRotationStep(1);
    }

    /// <summary>
    /// Queues a counter-clockwise 90-degree rotation, applied centrally in HandleHeldState.
    /// </summary>
    public void RotateCounterClockwise()
    {
        QueueRotationStep(-1);
    }

    /// <summary>
    /// Queues a rotation step. Multiple presses within the same frame are clamped
    /// to a single step in that direction, which is enough for a responsive button
    /// press without the ghost being asked to rebuild more than once per frame.
    /// </summary>
    private void QueueRotationStep(int step)
    {
        lastRotationRequestFrame = Time.frameCount;

        pendingRotationSteps += step;

        if (pendingRotationSteps > 1)
            pendingRotationSteps = 1;
        else if (pendingRotationSteps < -1)
            pendingRotationSteps = -1;
    }

    private void RotateActiveCandidateInPlace(int oldYawStep)
    {
        // Pure rotation around the block's own origin/pivot. No manual position
        // compensation of any kind - see ApplyHeldRootRotation() for why the
        // rotation itself must be applied INSTANTLY (not via a deferred
        // rb.MoveRotation) to avoid a one-frame visual mismatch between the real
        // block and the ghost/candidate preview, which is what actually caused
        // the visible "shift" on rotate.
        ApplyHeldRootRotation();

        if (!hasActiveCandidate || activeCandidate.socket == null)
        {
            RebuildGhostIfLocked();
            return;
        }

        // TEMPORARY DIAGNOSTIC: dump the exact numbers behind the candidate BEFORE
        // it gets refreshed for the new yaw, so we can compare against the
        // refreshed numbers below and see exactly which value causes the jump.
        SnapCandidate diagOld = activeCandidate;
        List<Vector2Int> diagOldFootprint = diagOld.rotatedFootprint;

        SnapCandidate rotatedCandidate = RefreshCandidate(activeCandidate, allowYawChange: true);

        if (rotatedCandidate.socket == null)
        {
            Debug.Log("[CANDIDATE ROTATE DIAG] RefreshCandidate returned null socket after rotation.");
            RebuildGhostIfLocked();
            return;
        }

        Vector3 diagPosDelta = rotatedCandidate.worldPosition - diagOld.worldPosition;

        Debug.Log(
            "[CANDIDATE ROTATE DIAG] oldYawStep=" + oldYawStep +
            " newYawStep=" + GetEffectiveYawStep() +
            " anchorGridX=" + diagOld.anchorGridX + " anchorGridZ=" + diagOld.anchorGridZ +
            " (unchanged=" + (diagOld.anchorGridX == rotatedCandidate.anchorGridX && diagOld.anchorGridZ == rotatedCandidate.anchorGridZ) + ")" +
            " oldPivotCell=" + diagOld.pivotCell + " newPivotCell=" + rotatedCandidate.pivotCell +
            " oldFootprint=[" + string.Join(",", diagOldFootprint) + "]" +
            " newFootprint=[" + string.Join(",", rotatedCandidate.rotatedFootprint) + "]" +
            " oldWorldPos=" + diagOld.worldPosition.ToString("F4") +
            " newWorldPos=" + rotatedCandidate.worldPosition.ToString("F4") +
            " deltaMagnitude=" + diagPosDelta.magnitude.ToString("F4") +
            " delta=" + diagPosDelta.ToString("F4") +
            " socketGridX=" + rotatedCandidate.socket.gridX + " socketGridZ=" + rotatedCandidate.socket.gridZ
        );

        activeCandidate = rotatedCandidate;
        hasActiveCandidate = true;
        TargetSocket = rotatedCandidate.socket;
        lastKnownPivotForTargetSocket = rotatedCandidate.pivotCell;

        rotationAnchorLockActive = true;
        rotationAnchorLockHandPosition = GetHeldReferencePosition();

        // RebuildGhostForCandidate shows a valid (blue) or invalid (red) ghost
        // depending on whether the rotated footprint still fits, instead of
        // silently discarding the preview. Rotation itself always keeps
        // progressing freely through every step (never reverted), and there's
        // no automatic switching to an alternative socket either - if the
        // rotated position is invalid, it just shows red and stays exactly
        // there; finding a valid spot from there is entirely up to the player
        // (rotate further or move the hand).
        RebuildGhostForCandidate(rotatedCandidate);
    }

    // -------------------------------------------------------------------------
    // Public API: State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if this block is selected by a hand/controller and not by an XR socket.
    /// </summary>
    public bool IsHeldByHand()
    {
        if (grabInteractable == null || !grabInteractable.isSelected)
            return false;

        foreach (var interactor in grabInteractable.interactorsSelecting)
        {
            if (!(interactor is XRSocketInteractor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the current ghost preview if one exists.
    /// </summary>
    public void HideGhost()
    {
        if (ghostRoot == null)
            return;

        // Important for rotation stability:
        // Destroy() removes the object only at the end of the frame.
        // If we create the new rotated ghost in the same frame, the old ghost
        // can remain visible for one frame and looks like a short wrong/opposite
        // rotation before the correct ghost appears.
        // Disabling it first hides it immediately.
        ghostRoot.SetActive(false);
        Destroy(ghostRoot);
        ghostRoot = null;
        ghostRenderers.Clear();
    }

    /// <summary>
    /// Event to listen to when snapping
    /// </summary>
    public UnityEvent Snapped;

    /// <summary>
    /// Called after this block has been successfully snapped.
    /// </summary>
    public void OnSnapped()
    {
        CurrentYawOffset = 0f;
        ResetVisualRoot();

        ClearTargetAndGhost();
        lastBuiltYaw = -999f;
        Snapped.Invoke();
    }

    // -------------------------------------------------------------------------
    // Main Held-State Logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles all logic while the block is currently being held.
    /// </summary>
    private void HandleHeldState()
    {
        if (!wasHeld)
            BeginHold();

        // Always enforce the floor limit, regardless of whether this frame is
        // handling a rotation or a normal update - see EnforceFloorLimit() for
        // why this is needed at all.
        EnforceFloorLimit();

        // Keep ignoring collision against any newly-nearby placed blocks (never
        // against the floor) - see UpdateIgnoredCollisionsWithNearbyBlocks().
        UpdateIgnoredCollisionsWithNearbyBlocks();

        if (pendingRotationSteps != 0)
        {
            ApplyQueuedRotationStep();

            // FIX: this used to `return` immediately here, skipping
            // ApplyHeldRootRotation() (and therefore ResetVisualRoot())
            // entirely on the exact frame a rotation happens. Rotating
            // quickly enough could mean the next queued rotation step fires
            // before a "normal" frame ever got a chance to run
            // ApplyHeldRootRotation() - leaving the visual mesh child stuck
            // wherever RotateActiveCandidateInPlace() last left it, never
            // reset back to its correct default local transform. This is
            // exactly what caused the visible mesh to drift away from the
            // (always-correct, root-based) red footprint gizmo specifically
            // at 90/180/270 degrees.
            ApplyHeldRootRotation();

            wasHeld = true;
            return;
        }

        ApplyHeldRootRotation();

        // FIX: after everything we tried (allowYawChange fixes, larger switch
        // margins, larger hand-movement tolerance) still occasionally let some
        // OTHER candidate-refresh path silently replace what
        // RotateActiveCandidateInPlace had just correctly set up - including
        // auto-switching to a different valid socket while the player was
        // simply holding still looking at a red (invalid) ghost, which is not
        // wanted at all. Now BOTH the time window AND a real hand movement
        // (rotationSearchGraceMoveDistance) are required before any other
        // search runs again - holding perfectly still, no matter how long,
        // never triggers a switch; only a genuine, deliberate hand movement
        // does.
        float handMovedSinceRotate = Vector3.Distance(GetHeldReferencePosition(), rotationSearchGraceHandPosition);

        if (Time.time < rotationSearchGracePeriodEndTime || handMovedSinceRotate < rotationSearchGraceMoveDistance)
        {
            wasHeld = true;
            return;
        }

        // Expensive socket search is throttled. The ghost itself is reused and
        // remains stable between searches, so this feels smoother in VR and avoids
        // lag on large floor grids with many sockets.
        if (ShouldRunSnapSearch())
        {
            nextAllowedSnapSearchTime = Time.time + Mathf.Max(0f, snapSearchInterval);
            UpdateBestSnapCandidate();
        }
        else
        {
            KeepCurrentCandidateLightweight();
        }

        wasHeld = true;
    }

    // -------------------------------------------------------------------------
    // Runtime State: Floor Clamp
    // -------------------------------------------------------------------------

    private bool hasCachedFloorTopY;
    private float cachedFloorTopY;

    /// <summary>
    /// Extra safety net that stops the held block from ending up below the
    /// floor's surface.
    ///
    /// This used to be the ONLY thing preventing that: the block's colliders
    /// were all turned into triggers while held (to pass through other placed
    /// blocks), which also disabled solid collision against the FLOOR as a side
    /// effect (Unity only produces a physical collision response when BOTH
    /// colliders are non-trigger). Now that collision is instead selectively
    /// ignored only against OTHER PLACED BLOCKS (see
    /// UpdateIgnoredCollisionsWithNearbyBlocks/CacheHeldOwnColliders), the
    /// floor's own collider is never touched and stays fully solid - physics
    /// itself should already stop the block from being pushed through it. This
    /// clamp is kept only as a cheap backup for edge cases (e.g. a very fast
    /// downward hand motion in a single physics step).
    /// </summary>
    private void EnforceFloorLimit()
    {
        if (rb == null || block == null)
            return;

        if (!hasCachedFloorTopY)
        {
            LegoBlockSocketSpawner[] allSpawners = FindObjectsByType<LegoBlockSocketSpawner>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

            foreach (LegoBlockSocketSpawner spawner in allSpawners)
            {
                if (spawner == null || spawner.GetComponentInParent<LegoBlock>() != null)
                    continue; // only an independent floor/grid counts as "the floor"

                cachedFloorTopY = spawner.transform.position.y;
                hasCachedFloorTopY = true;
                break;
            }
        }

        if (!hasCachedFloorTopY)
            return;

        float halfHeight = block.height * Mathf.Abs(transform.localScale.y) * 0.5f;
        float minY = cachedFloorTopY + halfHeight;

        if (transform.position.y < minY)
        {
            Vector3 clampedPosition = transform.position;
            clampedPosition.y = minY;
            rb.MovePosition(clampedPosition);
        }
    }

    private bool ShouldRunSnapSearch()
    {
        if (!hasActiveCandidate || activeCandidate.socket == null || ghostRoot == null)
            return true;

        return Time.time >= nextAllowedSnapSearchTime;
    }

    private void KeepCurrentCandidateLightweight()
    {
        if (!hasActiveCandidate || activeCandidate.socket == null)
            return;

        // FIX: same root cause as the rotationAnchorLockActive fix above - this
        // per-frame lightweight refresh used to call RefreshCandidate WITHOUT
        // allowYawChange, so the instant the player rotated, this saw a yaw
        // mismatch and treated the candidate as gone (refreshed.socket == null
        // below), calling ClearTargetAndGhost() and forcing a brand new search
        // next frame - which could land on a different stud/pivot than the one
        // that was actually being tracked. This function's whole purpose is to
        // cheaply keep following the SAME candidate frame to frame, including
        // through rotations - it should never treat "yaw changed" as "candidate
        // is gone".
        SnapCandidate refreshed = RefreshCandidate(activeCandidate, allowYawChange: true);

        if (refreshed.socket == null)
            return;

        float maxHoldDistance = snapDistanceThreshold * GetScaleAwareFactor() * Mathf.Max(1f, currentCandidateHoldDistanceMultiplier);

        // This check is cheap: no full socket search and no overlap tests.
        // It only prevents an old ghost from sticking when the hand moved far away.
        if (refreshed.distance > maxHoldDistance || refreshed.distance == float.MaxValue)
        {
            Debug.LogWarning(
                "[LIGHTWEIGHT CLEAR DIAG] Dropping candidate here! " +
                "refreshed.distance=" + refreshed.distance +
                " maxHoldDistance=" + maxHoldDistance +
                " pivotCell=" + refreshed.pivotCell +
                " yawStepWhenBuilt=" + refreshed.yawStepWhenBuilt,
                this
            );
            ClearTargetAndGhost();
            return;
        }

        activeCandidate = refreshed;
        TargetSocket = refreshed.socket;
        lastKnownPivotForTargetSocket = refreshed.pivotCell;

        // Move the existing ghost every frame, even while the expensive socket
        // search is throttled. This keeps the preview visually smooth without
        // doing full grid scans or physics checks every Update.
        if (ghostRoot != null)
            BuildGhostAtPosition(refreshed.worldPosition, refreshed.worldRotation, currentPlacementValid);
    }

    /// <summary>
    /// Applies a queued 90-degree rotation in one controlled place.
    /// This avoids the short wrong-direction flash that happened when rotation
    /// rebuilt the ghost immediately while the normal Update search was also
    /// rebuilding it.
    /// </summary>
    private void ApplyQueuedRotationStep()
    {
        int oldYawStep = GetEffectiveYawStep();
        int step = pendingRotationSteps;
        pendingRotationSteps = 0;

        CurrentYawOffset += step * 90f;
        CurrentYawOffset %= 360f;

        if (CurrentYawOffset < 0f)
            CurrentYawOffset += 360f;

        RotateActiveCandidateInPlace(oldYawStep);

        rotationSearchGracePeriodEndTime = Time.time + Mathf.Max(0f, rotationSearchGracePeriod);
        rotationSearchGraceHandPosition = GetHeldReferencePosition();
    }

    /// <summary>
    /// Initializes physics and socket state when the block starts being held.
    /// </summary>
    private void BeginHold()
    {
        pendingRotationSteps = 0;
        CurrentYawOffset = 0f;
        nextAllowedSnapSearchTime = 0f;
        nextSocketCacheRefreshTime = 0f;

        // Beim Aufheben wird der Block wieder gerade gemacht.
        // Es bleibt nur die aktuelle Y-Richtung erhalten.
        // X/Z-Kippen vom Umfallen wird entfernt.
        heldBaseRotation = GetUprightRotationFromCurrentYaw();

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.rotation = heldBaseRotation;

        // FIX: force the physics engine to immediately adopt this new rotation for
        // its own internal collider representation. Without this, the Collider's
        // physics-side pose (used by gizmos, raycasts, everything Physics-related)
        // can lag one frame behind the rendered Transform, which is exactly the
        // "collider/outline sits somewhere else than the visible block" symptom -
        // and since it only shows up if you happen to look during that lag window,
        // it appears completely random ("manchmal").
        Physics.SyncTransforms();

        block.SetSnappedToSocket(false);

        ReleaseCurrentOccupiedSocketsAndNotifyParent();
        DisableAllSocketInteractors(true);

        // FIX: without this reset, a pivot cell remembered from a PREVIOUS hold
        // session (e.g. placing this block on the floor) could get reused as a
        // fallback anchor when this same block is picked up again and hovered over
        // a completely different target (e.g. stacking on another block) - the old
        // pivot value has no meaningful relationship to the new target grid's
        // footprint, producing a ghost that doesn't line up with the new block's
        // studs. Every new hold session must start with a clean slate.
        lastKnownPivotForTargetSocket = Vector2Int.zero;

        // FIX: previously this made EVERY collider on the block a trigger while
        // held, so it could pass through already-placed blocks while aiming an
        // overhang. That also disabled solid collision against the FLOOR (Unity
        // only produces a physical collision response when BOTH colliders are
        // non-trigger) - meaning nothing stopped the hand from pushing the held
        // block below ground. The replacement below (UpdateIgnoredCollisions,
        // called every held frame) instead selectively ignores collision only
        // against OTHER PLACED LEGO BLOCKS (via Physics.IgnoreCollision), and
        // never against the floor - so the floor still physically blocks
        // downward movement, exactly like a normal Rigidbody, while the block
        // still passes through other bricks for aiming.
        CacheHeldOwnColliders();
        ignoredOtherColliders.Clear();
    }

    /// <summary>
    /// Caches this block's own solid (non-trigger) colliders once per hold
    /// session, so we know exactly which colliders to pair up with nearby
    /// blocks' colliders for Physics.IgnoreCollision.
    /// </summary>
    private void CacheHeldOwnColliders()
    {
        heldOwnColliders.Clear();

        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
                heldOwnColliders.Add(colliders[i]);
        }
    }

    /// <summary>
    /// While held, finds nearby OTHER placed LEGO blocks and tells Unity's
    /// physics engine to ignore collision between this block's own colliders
    /// and theirs - letting you freely aim/overhang through them. Deliberately
    /// never touches the floor grid's collider (it has no LegoBlock component,
    /// so it's simply never matched here), so the floor keeps blocking downward
    /// movement normally.
    ///
    /// Ignored pairs accumulate for the whole hold session (never re-enabled
    /// mid-hold, only on release) - once ignored, a pair stays ignored even if
    /// you move away and back, which is harmless and avoids doing this
    /// expensive pairing work more than once per nearby block per hold.
    /// </summary>
    private void UpdateIgnoredCollisionsWithNearbyBlocks()
    {
        if (heldOwnColliders.Count == 0)
            return;

        RectInt bounds = block.GetFootprintBounds(block.GetRotatedFootprint(GetEffectiveYawStep()));

        float studSpacing = 0.5f;
        LegoBlockSocketSpawner anyGrid = FindObjectOfType<LegoBlockSocketSpawner>();

        if (anyGrid != null)
            studSpacing = Mathf.Max(anyGrid.studSpacingX, anyGrid.studSpacingZ);

        float searchRadius = (Mathf.Max(bounds.width, bounds.height) * 0.5f + 3f) * studSpacing;

        int hitCount = Physics.OverlapSphereNonAlloc(
            GetHeldReferencePosition(),
            Mathf.Max(0.5f, searchRadius),
            nearbyBlockColliderBuffer,
            blockCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = nearbyBlockColliderBuffer[i];

            if (hitCollider == null || hitCollider.isTrigger)
                continue;

            // Never matches the floor grid - it has no LegoBlock component, so
            // it simply falls through here and keeps its normal solid collision.
            LegoBlock otherBlock = hitCollider.GetComponentInParent<LegoBlock>();

            if (otherBlock == null || otherBlock == block)
                continue;

            if (ignoredOtherColliders.Contains(hitCollider))
                continue;

            for (int c = 0; c < heldOwnColliders.Count; c++)
            {
                if (heldOwnColliders[c] != null)
                    Physics.IgnoreCollision(heldOwnColliders[c], hitCollider, true);
            }

            ignoredOtherColliders.Add(hitCollider);
        }
    }

    /// <summary>
    /// Restores normal collision between this block and every other block's
    /// collider that was ignored during this hold session. Called on release,
    /// both for a successful snap and for a plain drop.
    /// </summary>
    private void RestoreIgnoredCollisions()
    {
        foreach (Collider otherCollider in ignoredOtherColliders)
        {
            if (otherCollider == null)
                continue;

            for (int c = 0; c < heldOwnColliders.Count; c++)
            {
                if (heldOwnColliders[c] != null)
                    Physics.IgnoreCollision(heldOwnColliders[c], otherCollider, false);
            }
        }

        ignoredOtherColliders.Clear();
    }

    /// <summary>
    /// Updates the current preview by searching all sockets every frame.
    ///
    /// This is intentionally different from a hard socket lock: the old lock made
    /// the ghost ignore nearby sockets, especially on 1x1 blocks or when trying
    /// to place a middle stud of the held block onto an existing socket.
    /// </summary>
    private void UpdateBestSnapCandidate()
    {
        // After pressing O, keep the same socket/anchor candidate active until
        // the user actually moves the hand away. Without this, the every-frame
        // search may choose a different anchor based on hand position, making
        // rotation work only from certain holding positions.
        if (rotationAnchorLockActive && TargetSocket != null && hasActiveCandidate && activeCandidate.socket != null)
        {
            float handMoveDistance = Vector3.Distance(GetHeldReferencePosition(), rotationAnchorLockHandPosition);

            // Scale-aware unlock distance: a fixed world-space distance (like the old
            // 0.35 default) can be much bigger than the actual gap between adjacent
            // studs on small/scaled-down blocks, effectively suppressing ALL normal hand
            // movement and making valid neighboring points feel "skipped" after rotating.
            // Basing it on a fraction of the current grid's actual stud spacing keeps the
            // lock's real-world feel consistent regardless of block/scale size.
            float effectiveUnlockDistance = rotationAnchorUnlockDistance;

            if (activeCandidate.parentGrid != null)
            {
                float studSpacing = Mathf.Max(
                    activeCandidate.parentGrid.studSpacingX,
                    activeCandidate.parentGrid.studSpacingZ
                );

                effectiveUnlockDistance = Mathf.Min(
                    rotationAnchorUnlockDistance,
                    studSpacing * Mathf.Max(0.05f, rotationAnchorUnlockStudFraction)
                );
            }

            if (handMoveDistance <= effectiveUnlockDistance)
            {
                // FIX: this used to call RefreshCandidate(activeCandidate) WITHOUT
                // allowYawChange - but the entire point of rotationAnchorLockActive
                // is to keep the SAME anchor/pivot stable ACROSS rotations. Without
                // allowYawChange, RefreshCandidate's internal yaw-mismatch check
                // (meant for a completely different situation - deciding whether an
                // OLD, stale candidate is still valid at a NEW yaw) rejected this
                // locked candidate the moment its yaw no longer matched
                // yawStepWhenBuilt, silently dropping the lock and letting a fresh
                // FindInitialSnapCandidate() search run instead - which can pick a
                // DIFFERENT stud as its "best" pivot. That is exactly what produced
                // the reproducible "only 2 of 4 rotations show a valid ghost"
                // pattern: the lock was supposed to prevent this exact kind of
                // mid-rotation-sequence candidate replacement.
                SnapCandidate lockedCandidate = RefreshCandidate(activeCandidate, allowYawChange: true);

                bool socketOk = lockedCandidate.socket != null;
                bool logicallyValid = socketOk && IsCandidateLogicallyValidForPreview(lockedCandidate);
                bool noIntersect = socketOk && !CandidateWouldIntersectOtherBlocks(lockedCandidate);
                bool fullyValid = socketOk && IsCandidateValidForPreview(lockedCandidate);

                if (!fullyValid)
                {
                    Debug.LogWarning(
                        "[LOCK DROP DIAG] Rotation anchor lock is being dropped! " +
                        "socketOk=" + socketOk +
                        " logicallyValid=" + logicallyValid +
                        " noIntersect=" + noIntersect +
                        " pivotCell=" + lockedCandidate.pivotCell +
                        " anchorGridX=" + lockedCandidate.anchorGridX + " anchorGridZ=" + lockedCandidate.anchorGridZ +
                        " yawStepWhenBuilt=" + lockedCandidate.yawStepWhenBuilt,
                        this
                    );
                }

                if (fullyValid)
                {
                    activeCandidate = lockedCandidate;
                    hasActiveCandidate = true;
                    TargetSocket = lockedCandidate.socket;
                    lastKnownPivotForTargetSocket = lockedCandidate.pivotCell;
                    RebuildGhostForCandidate(lockedCandidate);
                    return;
                }
            }
            else
            {
                Debug.LogWarning($"[LOCK DROP DIAG] Rotation anchor lock dropped due to HAND MOVEMENT: handMoveDistance={handMoveDistance:F4} effectiveUnlockDistance={effectiveUnlockDistance:F4}", this);
            }

            rotationAnchorLockActive = false;
        }

        SnapCandidate bestCandidate = FindInitialSnapCandidate();

        if (bestCandidate.socket == null)
        {
            ClearTemporaryStabilization();

            if (TryKeepCurrentValidCandidate())
                return;

            if (debugPlacementFailures)
                LogPlacementFailureDebugInfo("no socket found at all (FindInitialSnapCandidate returned null)");

            ClearTargetAndGhost();
            lastBuiltYaw = -999f;
            return;
        }

        // Invalid candidate:
        // Do not show red ghosts inside/halfway through blocks.
        // If the newly found candidate is invalid but the current candidate is still valid,
        // keep the current valid one instead of flickering/disappearing.
        if (!IsCandidateValidForPreview(bestCandidate))
        {
            ClearTemporaryStabilization();

            if (TryKeepCurrentValidCandidate())
                return;

            if (debugPlacementFailures)
            {
                LogCandidateInvalidReason(bestCandidate);
                LogPlacementFailureDebugInfo("candidate found but rejected as invalid (see reason above)");
            }

            if (hideInvalidGhostPreview)
            {
                ClearTargetAndGhost();
                lastBuiltYaw = -999f;
                return;
            }

            TargetSocket = bestCandidate.socket;
            hasActiveCandidate = true;
            activeCandidate = bestCandidate;
            lastKnownPivotForTargetSocket = bestCandidate.pivotCell;
            BuildGhostAtPosition(bestCandidate.worldPosition, bestCandidate.worldRotation, false);
            currentPlacementValid = false;
            lastBuiltYaw = CurrentYawOffset;
            return;
        }

        // Hysteresis:
        // If the current candidate is still usable and almost as good as the
        // newly found one, keep it. This prevents flicker between neighboring
        // sockets while still allowing clear movement to another socket.
        if (hasActiveCandidate && activeCandidate.socket != null)
        {
            // FIX: same root cause as the other call sites - without
            // allowYawChange, rotating the block broke this hysteresis check
            // (meant to prevent flicker between neighboring sockets), since it
            // saw the yaw change and rejected the current candidate, letting
            // bestCandidate silently win even when the truly correct thing was
            // to keep following the SAME stud through the rotation.
            SnapCandidate refreshedActive = RefreshCandidate(activeCandidate, allowYawChange: true);

            if (refreshedActive.socket != null &&
                IsCandidateValidForPreview(refreshedActive) &&
                refreshedActive.distance <= bestCandidate.distance + candidateSwitchMargin * GetScaleAwareFactor())
            {
                bestCandidate = refreshedActive;
            }
        }

        activeCandidate = bestCandidate;
        hasActiveCandidate = true;
        TargetSocket = bestCandidate.socket;
        lastKnownPivotForTargetSocket = bestCandidate.pivotCell;

        if (debugPlacementFailures && bestCandidate.socket != lastLoggedCandidateSocket)
        {
            lastLoggedCandidateSocket = bestCandidate.socket;
            LogAcceptedCandidateDebugInfo(bestCandidate);
        }

        RebuildGhostForCandidate(bestCandidate);
    }


    /// <summary>
    /// Keeps the current active candidate only while it is still valid.
    /// This is not the bad sticky ghost behavior: it does not keep old invalid positions.
    /// It simply prevents the ghost from disappearing for tiny movements when the currently
    /// selected socket is still placeable.
    /// </summary>
    private bool TryKeepCurrentValidCandidate()
    {
        if (!keepCurrentValidCandidate)
            return false;

        if (!hasActiveCandidate || activeCandidate.socket == null)
            return false;

        // FIX: same root cause as the other RefreshCandidate call sites above -
        // without allowYawChange, rotating the block made this reject the
        // candidate purely because its yaw changed, even though "keep the
        // current valid candidate" is explicitly what this function is meant
        // to do across rotations too.
        SnapCandidate refreshed = RefreshCandidate(activeCandidate, allowYawChange: true);

        if (refreshed.socket == null)
            return false;

        refreshed.distance = MeasureCandidateDistance(refreshed);

        float maxHoldDistance =
            snapDistanceThreshold * GetScaleAwareFactor() * Mathf.Max(1f, currentCandidateHoldDistanceMultiplier);

        if (refreshed.distance == float.MaxValue || refreshed.distance > maxHoldDistance)
            return false;

        if (!IsCandidateValidForPreview(refreshed))
            return false;

        activeCandidate = refreshed;
        hasActiveCandidate = true;
        TargetSocket = refreshed.socket;
        lastKnownPivotForTargetSocket = refreshed.pivotCell;

        RebuildGhostForCandidate(refreshed);
        return true;
    }

    // -------------------------------------------------------------------------
    // Release and Placement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles releasing the block. If the current placement is valid, the block snaps.
    /// Otherwise it is released back to normal physics.
    /// </summary>
    private void OnRelease()
    {
        HideGhost();

        // Restore normal physical collision before deciding what happens next -
        // both the snap-success and the fallback-drop paths need real colliders.
        RestoreIgnoredCollisions();

        if (hasActiveCandidate && activeCandidate.socket != null && currentPlacementValid)
            TrySnapToCandidate(activeCandidate);
        else if (TargetSocket != null && currentPlacementValid)
            TrySnapToTarget();
        else
            FallbackRelease();

        DisableAllSocketInteractors(false);
        ClearTemporaryStabilization();

        TargetSocket = null;
        currentPlacementValid = false;
        pendingRotationSteps = 0;
        hasActiveCandidate = false;
        activeCandidate = new SnapCandidate();
        rotationAnchorLockActive = false;
        lastBuiltYaw = -999f;
    }

    /// <summary>
    /// Attempts to snap the block to the exact candidate that was previewed.
    /// This preserves which footprint anchor of the held block was chosen.
    /// </summary>
    private void TrySnapToCandidate(SnapCandidate finalCandidate)
    {
        List<Vector2Int> cells = GetAbsoluteCells(finalCandidate);

        bool canPlace =
            finalCandidate.socket != null &&
            finalCandidate.parentGrid != null &&
            finalCandidate.parentGrid.IsFootprintAreaClear(cells) &&
            !CandidateWouldIntersectOtherBlocks(finalCandidate) &&
            !WouldFinalPlacementGenuinelyOverlap(finalCandidate);

        if (!canPlace)
        {
            FallbackRelease();
            return;
        }

        // TEMPORARY DIAGNOSTIC: dumps every value that determined this exact
        // placement, right as it happens. Compare yawStepWhenBuilt/pivotCell/
        // anchorGridX/Z/worldPosition against what you expected for the
        // socket you actually released onto. Now also logs the BASE grid's
        // (finalCandidate.parentGrid) own current transform, and the target
        // socket's own actual world position - to check whether the base's
        // rotation was correctly factored in, since this only seems to go
        // wrong specifically when stacking onto a ROTATED base block.
        LegoBlock baseBlockForDiag = finalCandidate.socket != null
            ? finalCandidate.socket.GetComponentInParent<LegoBlock>()
            : null;

        Debug.Log(
            "[SNAP PLACEMENT DIAG] Placing now. " +
            "yawStepWhenBuilt=" + finalCandidate.yawStepWhenBuilt +
            " GetEffectiveYawStep()=" + GetEffectiveYawStep() +
            " pivotCell=" + finalCandidate.pivotCell +
            " trueLocalPivotCell=" + finalCandidate.trueLocalPivotCell +
            " anchorGridX=" + finalCandidate.anchorGridX + " anchorGridZ=" + finalCandidate.anchorGridZ +
            " rotatedFootprint=[" + string.Join(",", finalCandidate.rotatedFootprint) + "]" +
            " socket.gridX=" + finalCandidate.socket.gridX + " socket.gridZ=" + finalCandidate.socket.gridZ +
            " socket.transform.position=" + finalCandidate.socket.transform.position.ToString("F4") +
            " worldPosition=" + finalCandidate.worldPosition.ToString("F4") +
            " worldRotation=" + finalCandidate.worldRotation.eulerAngles.ToString("F4") +
            " CurrentYawOffset=" + CurrentYawOffset +
            " heldBaseRotation=" + heldBaseRotation.eulerAngles.ToString("F4") +
            " parentGrid.transform.position=" + finalCandidate.parentGrid.transform.position.ToString("F4") +
            " parentGrid.transform.rotation=" + finalCandidate.parentGrid.transform.rotation.eulerAngles.ToString("F4") +
            " baseBlock.transform.rotation=" + (baseBlockForDiag != null ? baseBlockForDiag.transform.rotation.eulerAngles.ToString("F4") : "N/A (no base block found)"),
            this
        );

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;
        Physics.SyncTransforms();

        CurrentYawOffset = 0f;
        heldBaseRotation = transform.rotation;

        currentSocket = finalCandidate.socket;
        block.SnappedSocket = currentSocket;

        currentOccupiedSockets.Clear();
        currentOccupiedSockets.AddRange(finalCandidate.parentGrid.GetSocketsInFootprint(cells));

        MarkSocketsOccupied(currentOccupiedSockets, true);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        block.SetSnappedToSocket(true);

        currentlyNotifiedSupportBlocks.Clear();
        currentlyNotifiedSupportBlocks.AddRange(GetDirectSupportBlocksForCandidate(finalCandidate));

        for (int i = 0; i < currentlyNotifiedSupportBlocks.Count; i++)
            currentlyNotifiedSupportBlocks[i].AddAttachedBlockAbove();

        OnSnapped();
    }

    /// <summary>
    /// Attempts to snap the block to the current target socket.
    /// </summary>
    private void TrySnapToTarget()
    {
        SnapCandidate finalCandidate = ComputeCandidateForLockedSocket(TargetSocket);
        List<Vector2Int> cells = GetAbsoluteCells(finalCandidate);

        bool canPlace =
            finalCandidate.socket != null &&
            finalCandidate.parentGrid != null &&
            finalCandidate.parentGrid.IsFootprintAreaClear(cells) &&
            !CandidateWouldIntersectOtherBlocks(finalCandidate) &&
            !WouldFinalPlacementGenuinelyOverlap(finalCandidate);

        if (!canPlace)
        {
            FallbackRelease();
            return;
        }

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;
        Physics.SyncTransforms();

        CurrentYawOffset = 0f;
        heldBaseRotation = transform.rotation;

        currentSocket = finalCandidate.socket;
        block.SnappedSocket = currentSocket;

        currentOccupiedSockets.Clear();
        currentOccupiedSockets.AddRange(finalCandidate.parentGrid.GetSocketsInFootprint(cells));

        MarkSocketsOccupied(currentOccupiedSockets, true);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        block.SetSnappedToSocket(true);

        currentlyNotifiedSupportBlocks.Clear();
        currentlyNotifiedSupportBlocks.AddRange(GetDirectSupportBlocksForCandidate(finalCandidate));

        for (int i = 0; i < currentlyNotifiedSupportBlocks.Count; i++)
            currentlyNotifiedSupportBlocks[i].AddAttachedBlockAbove();

        OnSnapped();
    }

    /// <summary>
    /// Releases the block without snapping it to a socket.
    /// </summary>
    private void FallbackRelease()
    {
        HideGhost();

        block.SetSnappedToSocket(false);
        block.SnappedSocket = null;

        ResetVisualRoot();
        CurrentYawOffset = 0f;

        if (rb != null)
        {
            if (rb.isKinematic)
                rb.isKinematic = false;

            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
    }

    // -------------------------------------------------------------------------
    // Ghost Building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the ghost if a socket is currently locked.
    /// </summary>
    private void RebuildGhostIfLocked()
    {
        if (hasActiveCandidate && activeCandidate.socket != null)
            RebuildGhostForCandidate(activeCandidate);
        else if (TargetSocket != null)
            RebuildGhostForLockedSocket(TargetSocket);
        else
            lastBuiltYaw = -999f;
    }

    /// <summary>
    /// Rebuilds the ghost preview for an exact candidate.
    /// </summary>
    private void RebuildGhostForCandidate(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return;

        bool isValid = IsCandidateValidForPreview(candidate);

        if (isValid)
            StabilizeBlockUnderSocket(candidate.socket);
        else
            ClearTemporaryStabilization();

        BuildGhostAtPosition(candidate.worldPosition, candidate.worldRotation, isValid);

        currentPlacementValid = isValid;
        lastBuiltYaw = CurrentYawOffset;
    }

    /// <summary>
    /// Rebuilds the ghost preview for the current target socket and yaw rotation.
    /// </summary>
    private void RebuildGhostForLockedSocket(LegoSocket socket)
    {
        if (socket == null || socket.parentGrid == null)
            return;

        SnapCandidate candidate = ComputeCandidateForLockedSocket(socket);
        List<Vector2Int> cells = GetAbsoluteCells(candidate);

        bool coversInnerSocket = candidate.parentGrid.DoesFootprintCoverInnerSocket(cells);
        bool areaClear = candidate.parentGrid.IsFootprintAreaClear(cells);

        bool isValid = coversInnerSocket && areaClear;

        if (isValid)
            StabilizeBlockUnderSocket(socket);
        else
            ClearTemporaryStabilization();

        BuildGhostAtPosition(candidate.worldPosition, candidate.worldRotation, isValid);

        currentPlacementValid = isValid;
        lastBuiltYaw = CurrentYawOffset;
    }

    /// <summary>
    /// Creates or reuses a transparent ghost mesh at the given position and rotation.
    ///
    /// The old version destroyed and recreated the complete ghost every frame.
    /// In VR that causes visible flicker/stutter. This version creates the mesh once,
    /// then only updates transform and material state while the block is held.
    /// </summary>
    private void BuildGhostAtPosition(Vector3 position, Quaternion rotation, bool isValid)
    {
        if (meshSnapshots.Count == 0)
            return;

        if (ghostRoot == null)
            CreateGhostRoot(isValid);

        ghostRoot.transform.SetPositionAndRotation(position, rotation);

        // Important for global scaling:
        // Mesh snapshots are stored in the block's local space.
        // If the real block root is scaled, the ghost root must use the same scale.
        ghostRoot.transform.localScale = transform.localScale;

        if (ghostLastValidState != isValid)
            ApplyGhostMaterial(isValid);

        ghostLastValidState = isValid;
    }

    private void CreateGhostRoot(bool isValid)
    {
        ghostRoot = new GameObject("SnapGhost");
        ghostRenderers.Clear();

        Material ghostMaterial = GetGhostMaterial(isValid);

        foreach (MeshSnapshot snapshot in meshSnapshots)
        {
            GameObject part = new GameObject("GhostPart");

            part.transform.SetParent(ghostRoot.transform, false);
            part.transform.localPosition = snapshot.localPosition;
            part.transform.localRotation = snapshot.localRotation;
            part.transform.localScale = snapshot.localScale;

            MeshFilter meshFilter = part.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = snapshot.mesh;

            MeshRenderer meshRenderer = part.AddComponent<MeshRenderer>();

            int materialCount = 1;

            if (snapshot.originalRenderer != null &&
                snapshot.originalRenderer.sharedMaterials != null)
            {
                materialCount = Mathf.Max(1, snapshot.originalRenderer.sharedMaterials.Length);
            }

            Material[] ghostMaterials = new Material[materialCount];

            for (int i = 0; i < ghostMaterials.Length; i++)
                ghostMaterials[i] = ghostMaterial;

            meshRenderer.sharedMaterials = ghostMaterials;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            ghostRenderers.Add(meshRenderer);
        }

        ghostLastValidState = isValid;
    }

    private void ApplyGhostMaterial(bool isValid)
    {
        Material ghostMaterial = GetGhostMaterial(isValid);

        for (int r = 0; r < ghostRenderers.Count; r++)
        {
            MeshRenderer meshRenderer = ghostRenderers[r];

            if (meshRenderer == null)
                continue;

            Material[] ghostMaterials = meshRenderer.sharedMaterials;

            for (int i = 0; i < ghostMaterials.Length; i++)
                ghostMaterials[i] = ghostMaterial;

            meshRenderer.sharedMaterials = ghostMaterials;
        }
    }

    private Material GetGhostMaterial(bool isValid)
    {
        if (isValid)
        {
            if (ghostValidMaterial == null)
                ghostValidMaterial = CreateGhostMaterial(true);

            return ghostValidMaterial;
        }

        if (ghostInvalidMaterial == null)
            ghostInvalidMaterial = CreateGhostMaterial(false);

        return ghostInvalidMaterial;
    }

    /// <summary>
    /// Creates a transparent ghost material for valid or invalid placement.
    /// </summary>
    private Material CreateGhostMaterial(bool isValid)
    {
        Color color = isValid ? ghostColorValid : ghostColorInvalid;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = color;
        material.renderQueue = 3000;

        if (shader.name.Contains("Standard"))
        {
            material.SetFloat("_Mode", 3);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
        }
        else
        {
            material.SetFloat("_Surface", 1);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        return material;
    }

    // -------------------------------------------------------------------------
    // Snap Candidate Calculation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the absolute grid cells (footprint anchor + each rotated footprint cell)
    /// occupied by the given candidate.
    /// </summary>
    private List<Vector2Int> GetAbsoluteCells(SnapCandidate candidate)
    {
        return GetAbsoluteCells(candidate.anchorGridX, candidate.anchorGridZ, candidate.rotatedFootprint);
    }

    private List<Vector2Int> GetAbsoluteCells(int anchorGridX, int anchorGridZ, List<Vector2Int> rotatedFootprint)
    {
        List<Vector2Int> cells = new List<Vector2Int>(rotatedFootprint.Count);

        for (int i = 0; i < rotatedFootprint.Count; i++)
        {
            Vector2Int cell = rotatedFootprint[i];
            cells.Add(new Vector2Int(anchorGridX + cell.x, anchorGridZ + cell.y));
        }

        return cells;
    }


    /// <summary>
    /// Returns the height used for calculating the ghost/snap root position.
    ///
    /// Floor grid:
    /// Uses LegoBlock.GetWorldHeight() because that fixed floor placement while scaled.
    ///
    /// Normal block grid:
    /// Uses the held block's current visual scaled height. This is the working logic.
    ///
    /// Plate grid:
    /// Your plate prefabs use a negative socketY. The spawner adds socketY as a constant,
    /// but visually the plate is scaled. So at smaller scale the negative socketY is too
    /// negative and the ghost drifts downward.
    ///
    /// Fix:
    /// Convert the negative socketY from unscaled behavior to scaled behavior:
    /// correction = socketY * (supportScaleY - 1)
    ///
    /// Example socketY = -0.3:
    /// scale 1.0 => correction 0
    /// scale 0.5 => correction +0.15
    /// scale 0.25 => correction +0.225
    /// </summary>
    private float GetSnapHeightForGrid(LegoBlockSocketSpawner targetGrid)
    {
        if (block == null)
            return 0f;

        if (targetGrid == null)
            return block.GetWorldHeight();

        LegoBlock supportBlock = targetGrid.GetComponentInParent<LegoBlock>();

        // Floor grid / independent grid: keep the known-good floor behavior.
        if (supportBlock == null)
            return block.GetWorldHeight();

        // Working normal block-on-block logic.
        float snapHeight = block.height * Mathf.Abs(transform.localScale.y);

        // Plate-specific fix:
        // Only negative socketY grids are affected. Normal block grids with socketY = 0 are unchanged.
        if (useScaledNegativeSocketYFix && targetGrid.socketY < 0f)
        {
            float supportScaleY = Mathf.Abs(supportBlock.transform.lossyScale.y);

            // This is the important part:
            // The spawner currently adds socketY as if it were unscaled.
            // For a scaled support block, socketY should behave like socketY * scale.
            float scaledSocketYCorrection = targetGrid.socketY * (supportScaleY - 1f);

            snapHeight += scaledSocketYCorrection * scaledNegativeSocketYMultiplier;
            snapHeight += scaledNegativeSocketYFineTune;
        }

        return snapHeight;
    }

    /// <summary>
    /// Builds a candidate from a known footprint anchor grid position and the
    /// given (already rotated) footprint cells.
    /// </summary>
    private SnapCandidate BuildCandidateFromAnchor(LegoSocket socket, int anchorGridX, int anchorGridZ, List<Vector2Int> rotatedFootprint, Vector2Int pivotCell, Vector2Int trueLocalPivotCell)
    {
        Vector3 worldPosition = socket.parentGrid.GetFootprintCenterWorldPosition(
            GetAbsoluteCells(anchorGridX, anchorGridZ, rotatedFootprint),
            GetSnapHeightForGrid(socket.parentGrid)
        );

        // FIX: Use the block's OWN held rotation (heldBaseRotation + manual yaw), not the
        // candidate socket's rotation. Using socket.GetBaseRotation() meant the ghost's
        // rotation could suddenly jump whenever the search switched between a floor
        // candidate (spawner rotation) and a stacked candidate (rotated support block's
        // rotation) - completely independent of the player's rotate button. The ghost
        // must always visually match the held block exactly, and only change via the
        // explicit rotate button (which changes CurrentYawOffset).
        Quaternion worldRotation =
            heldBaseRotation *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        return new SnapCandidate
        {
            socket = socket,
            worldPosition = worldPosition,
            worldRotation = worldRotation,
            anchorGridX = anchorGridX,
            anchorGridZ = anchorGridZ,
            pivotCell = pivotCell,
            trueLocalPivotCell = trueLocalPivotCell,
            rotatedFootprint = rotatedFootprint,
            parentGrid = socket.parentGrid,
            // FIX: must match what RefreshCandidate compares this against -
            // that now uses yaw RELATIVE to the candidate's own grid (see
            // GetYawStepRelativeToGrid), not the raw world yaw. Storing the
            // world yaw here while comparing against relative yaw later would
            // make RefreshCandidate think the yaw changed on EVERY check
            // whenever this grid itself is rotated (since world yaw and
            // relative yaw only coincide when the grid's own rotation is 0).
            yawStepWhenBuilt = GetYawStepRelativeToGrid(socket.parentGrid)
        };
    }

    /// <summary>
    /// Calculates the final block position and rotation for a locked socket,
    /// assuming the block's footprint anchor (local cell 0,0) sits on this socket.
    /// </summary>
    private SnapCandidate ComputeCandidateForLockedSocket(LegoSocket socket)
    {
        int yawStep = GetYawStepRelativeToGrid(socket.parentGrid);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        // FIX: previously this always assumed pivotCell = (0,0), regardless of which
        // stud was actually used to find this socket in the live search. Since the
        // live search can anchor on ANY stud (needed for overhangs - see
        // FindCandidateFromRayHit), assuming (0,0) here produced a ghost/final
        // position offset from the real, previously-found position whenever a
        // fallback to this locked-socket path happened (e.g. right after rotating).
        // Use the last pivot we actually know is correct for this socket instead,
        // falling back to (0,0) only if we truly have no better information.
        Vector2Int pivotCell = rotatedFootprint.Contains(lastKnownPivotForTargetSocket)
            ? lastKnownPivotForTargetSocket
            : Vector2Int.zero;

        // FIX #2: the anchor must account for the pivot offset, exactly like
        // FindCandidateFromRayHit does (anchorGridX = socket.gridX - pivotCell.x).
        // Passing socket.gridX/gridZ directly as the anchor - as this used to do -
        // is only correct when pivotCell happens to be (0,0). For any other
        // remembered pivot (e.g. from a previous overhang elsewhere in the same
        // hold session), this shifted the WHOLE footprint by the pivot offset,
        // producing exactly the kind of "block sits shifted, still snaps, but
        // looks off" symptom that doesn't require rotating to trigger.
        int anchorGridX = socket.gridX - pivotCell.x;
        int anchorGridZ = socket.gridZ - pivotCell.y;

        // pivotCell here is already expressed at the CURRENT yawStep (it was only
        // accepted above if it's a member of the current rotatedFootprint), so
        // inverse-rotating it recovers the block's true, unrotated local cell.
        int inverseYawStep = (4 - yawStep) % 4;
        Vector2Int trueLocalPivotCell = RotateSingleCell(pivotCell, inverseYawStep);

        return BuildCandidateFromAnchor(socket, anchorGridX, anchorGridZ, rotatedFootprint, pivotCell, trueLocalPivotCell);
    }

    /// <summary>
    /// Returns the yaw step (0-3) that the HELD block's footprint should be
    /// rotated by for grid/pivot-cell math against a specific target grid -
    /// this is NOT the same as the held block's raw world yaw step
    /// (GetEffectiveYawStep) whenever the target grid itself is rotated.
    ///
    /// CORE FIX: GetFootprintCenterWorldPosition converts grid-cell offsets to
    /// world space via targetGrid.transform.TransformPoint(...) - which
    /// already applies the target grid's OWN current rotation once. Every
    /// footprint/pivot-cell calculation upstream of that was rotating the held
    /// block's footprint by its raw WORLD yaw step first - correct only when
    /// the target grid's own rotation is 0 (e.g. the floor, which is never
    /// rotated), but effectively double-applying rotation whenever stacking
    /// onto a target block that is itself rotated (e.g. placed at 90 degrees).
    /// That compounded rotation is exactly what produced a block landing
    /// "halfway between two studs" specifically when - and only when -
    /// stacking onto a rotated base, while floor placement (rotation always 0)
    /// was never affected.
    ///
    /// The correct value here is the RELATIVE yaw between the held block and
    /// the target grid: how the block's footprint is oriented in the grid's
    /// OWN local frame, since TransformPoint already handles converting that
    /// local frame into world space.
    /// </summary>
    private int GetYawStepRelativeToGrid(LegoBlockSocketSpawner targetGrid)
    {
        int heldWorldYawStep = GetEffectiveYawStep();

        if (targetGrid == null)
            return heldWorldYawStep;

        int gridWorldYawStep = GetYawStep(targetGrid.transform.rotation.eulerAngles.y);

        int relativeYawStep = (heldWorldYawStep - gridWorldYawStep) % 4;

        if (relativeYawStep < 0)
            relativeYawStep += 4;

        return relativeYawStep;
    }

    /// <summary>
    /// Recalculates world position, rotation, footprint, and distance for a stored
    /// candidate at the current yaw, while keeping its footprint anchor grid
    /// position fixed. This is what makes rotation happen "in place".
    /// </summary>
    private SnapCandidate RefreshCandidate(SnapCandidate candidate, bool allowYawChange = false)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        // FIX: yawStep here now specifically means "yaw relative to THIS
        // candidate's own target grid" (see GetYawStepRelativeToGrid above),
        // not the held block's raw world yaw - this is what actually fixes
        // stacking onto a rotated base block.
        int yawStep = GetYawStepRelativeToGrid(candidate.parentGrid);

        // If the yaw changed since this candidate was built, its anchorGridX/Z
        // (chosen to align a SPECIFIC stud of the OLD footprint shape with the
        // socket) is not meaningful for the NEW footprint shape - rotated footprints
        // can have a different bounding box (e.g. a 1x2 spans X at one yaw, Z at
        // another). Recomputing "footprint center" from the old anchor against the
        // new shape produced a world position that didn't line up with either
        // rotation's actual grid - exactly the "ghost sits between studs" bug.
        //
        // BUT: the rotation path (RotateActiveCandidateInPlace) DELIBERATELY calls
        // this right after changing the yaw, specifically to recompute the
        // candidate FOR the new rotation - that call passes allowYawChange=true.
        // Blocking it there (as an earlier version of this fix did) made rotation
        // silently fail to refresh the candidate at all, which is why the physical
        // block and the ghost kept disagreeing after every single rotation.
        if (!allowYawChange && yawStep != candidate.yawStepWhenBuilt)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        // CORE FIX: previously this kept anchorGridX/anchorGridZ completely
        // unchanged across rotation, and derived pivotCell from them
        // (socket.gridX - anchorGridX). That pivotCell is only guaranteed to be
        // a real cell of the footprint for the YAW IT WAS BUILT AT - after a
        // rotation, footprint cells rotate around local cell (0,0), but
        // anchorGridX represents wherever cell (0,0) happened to be for the OLD
        // shape. The recomputed pivotCell was therefore frequently not even a
        // member of the NEW rotated footprint - meaning the code kept the
        // ARBITRARY corner (0,0) fixed on the grid while the physically-anchored
        // stud silently swung away to a wrong, disconnected position. That is
        // exactly the "ghost/snap lands in the wrong spot after rotating" bug.
        //
        // The fix: re-rotate the block's OWN true local pivot cell (fixed once,
        // never itself reinterpreted) to the CURRENT yaw, and rebuild the anchor
        // from THAT - so the physical stud that is actually anchored to the
        // socket stays on the socket, and the rest of the footprint (including
        // cell 0,0) rotates around it, matching how a real Lego stud pivots.
        Vector2Int rotatedPivotCell = RotateSingleCell(candidate.trueLocalPivotCell, yawStep);

        candidate.anchorGridX = candidate.socket.gridX - rotatedPivotCell.x;
        candidate.anchorGridZ = candidate.socket.gridZ - rotatedPivotCell.y;
        candidate.pivotCell = rotatedPivotCell;
        candidate.rotatedFootprint = rotatedFootprint;

        candidate.worldPosition = candidate.parentGrid.GetFootprintCenterWorldPosition(
            GetAbsoluteCells(candidate.anchorGridX, candidate.anchorGridZ, rotatedFootprint),
            GetSnapHeightForGrid(candidate.parentGrid)
        );

        // Same fix as in BuildCandidateFromAnchor: always mirror the held block's own
        // rotation, never the candidate socket's rotation.
        candidate.worldRotation =
            heldBaseRotation *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        candidate.yawStepWhenBuilt = yawStep;
        candidate.distance = MeasureCandidateDistance(candidate);

        return candidate;
    }

    /// <summary>
    /// Returns the current 90-degree yaw step as 0, 1, 2, or 3.
    /// </summary>
    private int GetYawStep(float yawOffset)
    {
        int step = Mathf.RoundToInt(yawOffset / 90f) % 4;

        if (step < 0)
            step += 4;

        return step;
    }

    /// <summary>
    /// Returns the yaw step (0-3) representing this block's TOTAL current
    /// orientation: heldBaseRotation's own baked-in yaw PLUS the in-hand
    /// CurrentYawOffset from the rotate button.
    ///
    /// FIX: every footprint-cell calculation (block.GetRotatedFootprint) needs
    /// to know how the block's studs are ACTUALLY oriented relative to the
    /// grid right now - not just how much it turned during THIS hold session.
    /// Every call site used to compute yawStep from CurrentYawOffset alone,
    /// which only happens to be correct the very first time a block is ever
    /// picked up (heldBaseRotation starts at yaw 0 then). The moment you
    /// rotate a block and place it, heldBaseRotation permanently bakes in that
    /// rotation (TrySnapToCandidate/TrySnapToTarget: heldBaseRotation =
    /// transform.rotation). Picking the block back up resets CurrentYawOffset
    /// to 0 (BeginHold) while heldBaseRotation keeps the baked-in yaw from
    /// before - so the OLD code fed yawStep=0 into GetRotatedFootprint as if
    /// the block were still unrotated, even though it visually is not. That
    /// mismatch between "visual rotation" and "footprint-cell rotation used
    /// for grid math" is exactly the reproducible bug: rotate -> place -> pick
    /// back up -> ghost offset, even with zero further rotation involved.
    /// </summary>
    private int GetEffectiveYawStep()
    {
        float totalYaw = heldBaseRotation.eulerAngles.y + CurrentYawOffset;
        return GetYawStep(totalYaw);
    }

    /// <summary>
    /// Rotates a single LOCAL (unrotated) footprint cell by yawStep, clockwise
    /// around local cell (0,0). This mirrors LegoBlock.GetRotatedFootprint's
    /// per-cell formula exactly, but for one specific cell instead of the
    /// block's whole static footprint list - used to re-derive where the
    /// ORIGINALLY anchored stud (trueLocalPivotCell) lands after a new rotation,
    /// instead of re-deriving a (possibly nonexistent) cell from anchor math.
    /// </summary>
    private Vector2Int RotateSingleCell(Vector2Int cell, int yawStep)
    {
        int step = yawStep % 4;

        if (step < 0)
            step += 4;

        switch (step)
        {
            case 1:
                return new Vector2Int(cell.y, -cell.x);
            case 2:
                return new Vector2Int(-cell.x, -cell.y);
            case 3:
                return new Vector2Int(-cell.y, cell.x);
            default:
                return cell;
        }
    }

    /// <summary>
    /// Returns true if this candidate can currently be shown as a valid placement.
    /// </summary>
    private bool IsCandidateValidForPreview(SnapCandidate candidate)
    {
        if (!IsCandidateLogicallyValidForPreview(candidate))
            return false;

        if (CandidateWouldIntersectOtherBlocks(candidate))
            return false;

        // FIX: TrySnapToCandidate (the actual placement) ALSO requires
        // !WouldFinalPlacementGenuinelyOverlap - a separate, stricter check
        // with much tighter tolerances than the two checks above. Without it
        // here too, the ghost could show blue (valid) while this stricter
        // check would still reject the same candidate the instant you
        // release - exactly why the block sometimes just fell instead of
        // snapping despite a blue ghost. The preview must use the same
        // criteria as the real placement, or "blue" isn't a trustworthy
        // promise.
        if (WouldFinalPlacementGenuinelyOverlap(candidate))
            return false;

        return true;
    }

    /// <summary>
    /// Fast validity check used while scanning many candidates. It avoids physics
    /// OverlapBox calls, which were the main source of lag on large socket grids.
    /// The expensive collision check is done only for the selected preview candidate.
    /// </summary>
    private bool IsCandidateLogicallyValidForPreview(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return false;

        if (!IsSocketUsableForInitialSearch(candidate.socket))
            return false;

        List<Vector2Int> cells = GetAbsoluteCells(candidate);

        if (!candidate.parentGrid.DoesFootprintCoverInnerSocket(cells))
            return false;

        if (!candidate.parentGrid.IsFootprintAreaClear(cells))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if the candidate would physically overlap another placed LegoBlock.
    ///
    /// Important fix for large blocks and corner / L blocks:
    /// The old version checked one big rectangular OverlapBox around the whole
    /// footprint bounds. That falsely blocked valid placements because the empty
    /// notch of an L block was treated like solid geometry, and big rectangular
    /// blocks could hit neighbouring support blocks even when the actual studs
    /// were placeable.
    ///
    /// This version checks one small box per occupied stud cell. Empty footprint
    /// cells are not tested, so corner blocks can use their real shape. Blocks
    /// below the candidate are treated as support, not as collisions.
    /// </summary>
    /// <summary>
    /// Final, strict safety check performed ONLY at the moment of actually
    /// committing a placement (TrySnapToCandidate / TrySnapToTarget) - separate
    /// from the deliberately generous preview tolerances
    /// (verticalTouchToleranceV2, neighbourSupportVerticalTolerance,
    /// collisionCheckShrinkV2), which exist purely so the GHOST doesn't flicker
    /// red on genuinely valid touching placements. Those generous tolerances
    /// can occasionally let a genuinely overlapping placement through
    /// undetected during preview (reported symptom: plates/blocks sometimes
    /// end up placed INSIDE another block). This check uses near-zero
    /// tolerance instead, so the final commit can never lock in a real
    /// overlap, even if the live preview was a bit forgiving about it.
    /// </summary>
    private bool WouldFinalPlacementGenuinelyOverlap(SnapCandidate candidate)
    {
        if (candidate.parentGrid == null || block == null)
            return false;

        List<Vector2Int> absoluteCells = GetAbsoluteCells(candidate);

        if (absoluteCells == null || absoluteCells.Count == 0)
            return false;

        float scaleX = candidate.parentGrid.transform.lossyScale.x;
        float scaleZ = candidate.parentGrid.transform.lossyScale.z;

        float width = candidate.parentGrid.studSpacingX * Mathf.Abs(scaleX);
        float depth = candidate.parentGrid.studSpacingZ * Mathf.Abs(scaleZ);
        float height = block.height * Mathf.Abs(transform.localScale.y);

        // Near-zero shrink/tolerance: only truly touching faces are forgiven,
        // real interpenetration is always caught here, regardless of how
        // forgiving the live preview's settings are.
        //
        // FIX: horizontal shrink needs to be noticeably bigger than vertical -
        // side-by-side blocks on the SAME level have no other fallback (the
        // clearlyBelow/clearlyAbove vertical check below only helps when one
        // block is resting on top of another, not when they're just touching
        // at a shared seam on the same floor level). Without enough
        // horizontal shrink, two blocks placed directly next to each other
        // could register as "overlapping" from floating-point-level contact
        // alone, showing red at seams that aren't actually blocked.
        const float strictHorizontalShrink = 0.08f;
        const float strictVerticalShrink = 0.03f;
        const float strictVerticalTolerance = 0.03f;

        float strictScale = GetPlateSeamScaleFactor();
        float strictShrinkX = Mathf.Min(strictHorizontalShrink * strictScale, width * 0.20f);
        float strictShrinkZ = Mathf.Min(strictHorizontalShrink * strictScale, depth * 0.20f);
        float strictShrinkY = Mathf.Min(strictVerticalShrink * strictScale, height * 0.25f);
        float strictToleranceY = strictVerticalTolerance * strictScale;

        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.005f, width * 0.5f - strictShrinkX),
            Mathf.Max(0.005f, height * 0.5f - strictShrinkY),
            Mathf.Max(0.005f, depth * 0.5f - strictShrinkZ)
        );

        List<LegoBlock> allowedSupportBlocks = GetAllowedSupportChainBlocks(candidate);

        for (int cellIndex = 0; cellIndex < absoluteCells.Count; cellIndex++)
        {
            Vector3 cellCenter = GetCandidateCellWorldCenter(candidate, absoluteCells[cellIndex]);
            float candidateBottom = cellCenter.y - halfExtents.y;
            float candidateTop = cellCenter.y + halfExtents.y;

            Collider[] hits = Physics.OverlapBox(
                cellCenter,
                halfExtents,
                candidate.worldRotation,
                blockCollisionMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];

                if (hit == null || hit.isTrigger)
                    continue;

                LegoBlock otherBlock = hit.GetComponentInParent<LegoBlock>();

                if (otherBlock == null || otherBlock == block)
                    continue;

                if (allowedSupportBlocks.Contains(otherBlock))
                    continue;

                Bounds otherBounds = hit.bounds;
                float candidateHeight = Mathf.Max(0.0001f, candidateTop - candidateBottom);

                // REAL PLATE-SEAM FIX:
                // The final strict check also has to accept the neighbouring plate/block
                // as support. Otherwise a candidate that bridges across two thin plates
                // is rejected on release because the second plate is treated as an overlap.
                if (IsNeighbourSupportBelowCandidate(otherBounds, candidateBottom, candidateHeight))
                    continue;

                bool clearlyBelow = otherBounds.max.y <= candidateBottom + strictToleranceY;
                bool clearlyAbove = otherBounds.min.y >= candidateTop - strictToleranceY;

                if (clearlyBelow || clearlyAbove)
                    continue;

                return true;
            }
        }

        return false;
    }

    private bool CandidateWouldIntersectOtherBlocks(SnapCandidate candidate)
    {
        if (!preventGhostIntersectingBlocks)
            return false;

        if (candidate.parentGrid == null || block == null)
            return false;

        List<Vector2Int> absoluteCells = GetAbsoluteCells(candidate);

        if (absoluteCells == null || absoluteCells.Count == 0)
            return false;

        Vector3 halfExtents = GetCandidateCellCollisionHalfExtents(candidate);

        if (halfExtents.x <= 0f || halfExtents.y <= 0f || halfExtents.z <= 0f)
            return false;

        List<LegoBlock> allowedSupportBlocks = GetAllowedSupportChainBlocks(candidate);

        for (int cellIndex = 0; cellIndex < absoluteCells.Count; cellIndex++)
        {
            Vector3 cellCenter = GetCandidateCellWorldCenter(candidate, absoluteCells[cellIndex]);
            float candidateBottom = cellCenter.y - halfExtents.y;
            float candidateTop = cellCenter.y + halfExtents.y;

            Collider[] hits = Physics.OverlapBox(
                cellCenter,
                halfExtents,
                candidate.worldRotation,
                blockCollisionMask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];

                if (ShouldIgnoreCollisionHitForCandidateCell(hit, allowedSupportBlocks, candidateBottom, candidateTop))
                    continue;

                return true;
            }
        }

        return false;
    }

    private bool ShouldIgnoreCollisionHitForCandidateCell(
        Collider hit,
        List<LegoBlock> allowedSupportBlocks,
        float candidateBottom,
        float candidateTop
    )
    {
        if (hit == null || hit.isTrigger)
            return true;

        LegoBlock otherBlock = hit.GetComponentInParent<LegoBlock>();

        if (otherBlock == null)
            return true;

        // Ignore the held block's own colliders.
        if (otherBlock == block)
            return true;

        // The block we are snapping onto and every block below it in the
        // snapped support chain are allowed.
        if (allowedSupportBlocks.Contains(otherBlock))
            return true;

        Bounds otherBounds = hit.bounds;

        // FIX: cap the "touching, not intersecting" tolerance to a fraction of
        // the CANDIDATE's own height. verticalTouchToleranceV2/
        // neighbourSupportVerticalTolerance are fixed Inspector values (0.12 /
        // 0.30) tuned for normal full-height blocks - but a thin plate can be
        // considerably shorter than that. Without this cap, the tolerance
        // itself could exceed the plate's entire height, letting it sink
        // partway (or fully) into whatever is below it while still being
        // treated as "just touching" instead of "overlapping". Capping it
        // relative to the candidate's real height keeps the original benefit
        // (forgiving rounded stud bevels) for full-size blocks, without
        // silently permitting real overlap for anything shorter.
        float candidateHeight = Mathf.Max(0.0001f, candidateTop - candidateBottom);

        // FIX 2: 40% of the candidate's height is still way more forgiveness
        // than a rounded stud bevel actually needs (a few percent of height at
        // most) - on a normal full-height block that is nearly half a stud of
        // genuinely visible overlap that still got silently excused as "just
        // touching". Tightened to 15%, which comfortably covers bevel rounding
        // without hiding real, visible intersections like the one in the
        // screenshot (a ghost shown overlapping a neighboring block).
        float maxAllowedFraction = 0.15f;

        float activeVerticalTolerance = Mathf.Min(
            useFalseRedGhostFix ? verticalTouchToleranceV2 : verticalTouchTolerance,
            candidateHeight * maxAllowedFraction
        );

        // Bridge / edge support fix:
        // When a new block is placed across two side-by-side blocks, only the
        // socket owner under the chosen pivot was previously treated as support.
        // The neighbouring support block's studs could still overlap the
        // preview-cell OverlapBox and turn the placement invalid. If a hit is
        // basically below the candidate bottom, treat it as support too. This
        // keeps real side intersections blocked, because same-level blocks have
        // bounds that extend far above candidateBottom.
        if (IsNeighbourSupportBelowCandidate(otherBounds, candidateBottom, candidateHeight))
            return true;

        // Anything clearly below the new block is support / floor, not an
        // intersection. The tolerance is intentionally a little forgiving so
        // small collider/stud height differences do not block valid LEGO snaps.
        bool clearlyBelow = otherBounds.max.y <= candidateBottom + activeVerticalTolerance;
        bool clearlyAbove = otherBounds.min.y >= candidateTop - activeVerticalTolerance;

        if (clearlyBelow || clearlyAbove)
            return true;

        return false;
    }

    private bool IsNeighbourSupportBelowCandidate(Bounds otherBounds, float candidateBottom, float candidateHeight)
    {
        // REAL PLATE-SEAM FIX:
        // The previous cap used candidateHeight * 0.25. For plates this can become
        // only a few millimetres after the collision box shrink, so the studs/bevels
        // of the neighbouring plate are treated as a collision instead of support.
        // Use a small scale-aware absolute tolerance, but require the other collider's
        // CENTER to be below the candidate bottom. That keeps real same-level side
        // collisions blocked while allowing thin support plates at seams.
        float heightRelativeTolerance = candidateHeight * 0.75f;
        float absolutePlateTolerance = 0.16f * GetPlateSeamScaleFactor();

        float tolerance = Mathf.Min(
            Mathf.Max(0f, neighbourSupportVerticalTolerance),
            Mathf.Max(heightRelativeTolerance, absolutePlateTolerance)
        );

        bool centerIsBelowCandidate = otherBounds.center.y < candidateBottom;

        if (centerIsBelowCandidate && otherBounds.max.y <= candidateBottom + tolerance)
            return true;

        // Extra safety for plate studs / bevel colliders: the support body is below,
        // but a small part of its collider may protrude into the candidate's preview box.
        if (centerIsBelowCandidate && otherBounds.min.y < candidateBottom - tolerance * 0.25f)
            return true;

        return false;
    }

    /// <summary>
    /// Returns every block DIRECTLY touched/rested upon by this candidate's
    /// own footprint - the anchor socket's own block, plus (for bridging) any
    /// other block found underneath any other cell. Unlike
    /// GetAllowedSupportChainBlocks below, this does NOT walk each one's own
    /// further support chain - it's specifically for deciding which blocks
    /// should be notified "something is now resting on you" (and therefore
    /// become non-grabbable), not for collision-allowance purposes where a
    /// broader set is appropriate.
    /// </summary>
    private List<LegoBlock> GetDirectSupportBlocksForCandidate(SnapCandidate candidate)
    {
        List<LegoBlock> result = new List<LegoBlock>();
        List<LegoBlock> toExpand = new List<LegoBlock>();

        LegoBlock anchorBlock = candidate.socket != null
            ? candidate.socket.GetComponentInParent<LegoBlock>()
            : null;

        if (anchorBlock != null)
        {
            result.Add(anchorBlock);
            toExpand.Add(anchorBlock);
        }

        List<Vector2Int> absoluteCells = GetAbsoluteCells(candidate);

        if (absoluteCells != null && candidate.parentGrid != null)
        {
            Vector3 collisionHalfExtents = GetCandidateCellCollisionHalfExtents(candidate);

            for (int i = 0; i < absoluteCells.Count; i++)
            {
                Vector3 cellCenter = GetCandidateCellWorldCenter(candidate, absoluteCells[i]);

                // FIX: this used to build the search box from an arbitrary
                // offset below the cell's CENTER, sized to the full stud
                // footprint - that vertical range could reach high enough to
                // catch the TOP surface of a same-level, side-by-side
                // neighbor (which sits at roughly the same height as this
                // candidate's own bottom), incorrectly treating it as
                // "support" and locking it from removal too, even though it
                // was never actually underneath anything. Computing the
                // candidate's real bottom the same way the actual collision
                // check does, and searching in a thin band strictly BELOW
                // that (not up into the same height band a neighbor's top
                // would occupy), only catches blocks that are genuinely
                // underneath.
                float candidateBottom = cellCenter.y - collisionHalfExtents.y;

                // REAL PLATE-SEAM FIX:
                // A fixed tiny probe below the candidate misses thin plates because the
                // probe can sit completely below the plate collider. Use a scale-aware
                // vertical band that reaches from just below the candidate bottom down
                // into the support plate/block, so the second plate at a seam is found
                // and added as a valid support.
                float supportProbeDepth = Mathf.Max(0.12f * GetPlateSeamScaleFactor(), collisionHalfExtents.y * 3.0f);
                Vector3 boxCenter = new Vector3(cellCenter.x, candidateBottom - supportProbeDepth * 0.5f, cellCenter.z);
                Vector3 boxHalfExtents = new Vector3(collisionHalfExtents.x, supportProbeDepth * 0.5f, collisionHalfExtents.z);

                Collider[] hitsBelow = Physics.OverlapBox(boxCenter, boxHalfExtents, candidate.worldRotation, blockCollisionMask, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hitsBelow.Length; h++)
                {
                    if (hitsBelow[h] == null || hitsBelow[h].isTrigger)
                        continue;

                    LegoBlock underCellBlock = hitsBelow[h].GetComponentInParent<LegoBlock>();

                    if (underCellBlock != null && underCellBlock != block && !result.Contains(underCellBlock))
                    {
                        result.Add(underCellBlock);
                        toExpand.Add(underCellBlock);
                    }
                }
            }
        }

        // FIX: nested bridging - notify every block at ANY depth that this
        // placement ultimately rests on (bridges resting on other bridges),
        // not just the first level, so removal-locking is correctly applied
        // all the way down. Same iterative expansion as
        // GetAllowedSupportChainBlocks.
        int safetyDepth = 0;

        while (toExpand.Count > 0 && safetyDepth < 8)
        {
            safetyDepth++;
            List<LegoBlock> nextRound = new List<LegoBlock>();

            for (int b = 0; b < toExpand.Count; b++)
            {
                LegoBlock expandBlock = toExpand[b];
                Collider[] expandColliders = expandBlock.GetComponentsInChildren<Collider>();

                if (expandColliders == null || expandColliders.Length == 0)
                    continue;

                Bounds expandBounds = expandColliders[0].bounds;

                for (int c = 1; c < expandColliders.Length; c++)
                {
                    if (!expandColliders[c].isTrigger)
                        expandBounds.Encapsulate(expandColliders[c].bounds);
                }
                float recursiveProbeDepth = 0.10f * GetPlateSeamScaleFactor();
                Vector3 belowCenter = new Vector3(expandBounds.center.x, expandBounds.min.y - recursiveProbeDepth * 0.8f, expandBounds.center.z);
                Vector3 belowHalfExtents = new Vector3(expandBounds.extents.x * 0.9f, recursiveProbeDepth * 0.5f, expandBounds.extents.z * 0.9f);

                Collider[] deeperHits = Physics.OverlapBox(belowCenter, belowHalfExtents, expandBlock.transform.rotation, blockCollisionMask, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < deeperHits.Length; h++)
                {
                    if (deeperHits[h] == null || deeperHits[h].isTrigger)
                        continue;

                    LegoBlock deeperBlock = deeperHits[h].GetComponentInParent<LegoBlock>();

                    if (deeperBlock != null && deeperBlock != block && !result.Contains(deeperBlock))
                    {
                        result.Add(deeperBlock);
                        nextRound.Add(deeperBlock);
                    }
                }
            }

            toExpand = nextRound;
        }

        return result;
    }

    /// <summary>
    /// Returns the direct support block plus every block below it.
    /// Example: placing on a plate that is snapped onto a normal block returns:
    /// plate + normal block. This prevents the lower block from falsely blocking
    /// placement on the plate.
    /// </summary>
    private List<LegoBlock> GetAllowedSupportChainBlocks(SnapCandidate candidate)
    {
        List<LegoBlock> result = new List<LegoBlock>();

        LegoBlock supportBlock = candidate.socket != null
            ? candidate.socket.GetComponentInParent<LegoBlock>()
            : null;

        AddSupportChain(result, supportBlock);

        // FIX: AddSupportChain above only follows the anchor's OWN vertical
        // SnappedSocket chain - none of those blocks were ever queued for
        // further expansion, so if a bridge-on-bridge situation was actually
        // nested beneath the ANCHOR side (not just the extra bridging side
        // found below), recursion never continued there at all. Queuing
        // everything already found so far ensures every block in the
        // anchor's own chain also gets checked for further support
        // underneath it.
        List<Vector2Int> absoluteCells = GetAbsoluteCells(candidate);
        List<LegoBlock> toExpand = new List<LegoBlock>(result);

        if (absoluteCells != null && candidate.parentGrid != null)
        {
            Vector3 collisionHalfExtents = GetCandidateCellCollisionHalfExtents(candidate);

            for (int i = 0; i < absoluteCells.Count; i++)
            {
                Vector3 cellCenter = GetCandidateCellWorldCenter(candidate, absoluteCells[i]);

                // Search strictly below the candidate's real bottom surface,
                // not an arbitrary range from the cell's center that could
                // reach up into a same-level neighbor's own height band.
                float candidateBottom = cellCenter.y - collisionHalfExtents.y;

                // REAL PLATE-SEAM FIX:
                // A fixed tiny probe below the candidate misses thin plates because the
                // probe can sit completely below the plate collider. Use a scale-aware
                // vertical band that reaches from just below the candidate bottom down
                // into the support plate/block, so the second plate at a seam is found
                // and added as a valid support.
                float supportProbeDepth = Mathf.Max(0.12f * GetPlateSeamScaleFactor(), collisionHalfExtents.y * 3.0f);
                Vector3 boxCenter = new Vector3(cellCenter.x, candidateBottom - supportProbeDepth * 0.5f, cellCenter.z);
                Vector3 boxHalfExtents = new Vector3(collisionHalfExtents.x, supportProbeDepth * 0.5f, collisionHalfExtents.z);

                Collider[] hitsBelow = Physics.OverlapBox(boxCenter, boxHalfExtents, candidate.worldRotation, blockCollisionMask, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hitsBelow.Length; h++)
                {
                    if (hitsBelow[h] == null || hitsBelow[h].isTrigger)
                        continue;

                    LegoBlock underCellBlock = hitsBelow[h].GetComponentInParent<LegoBlock>();

                    if (underCellBlock != null && underCellBlock != block && !result.Contains(underCellBlock))
                    {
                        AddSupportChain(result, underCellBlock);
                        toExpand.Add(underCellBlock);
                    }
                }
            }
        }

        // FIX: nested bridging - a bridge block resting on TWO other blocks
        // only has ONE of them recorded via its own SnappedSocket (which
        // AddSupportChain follows) - the SECOND block it's also physically
        // resting on is only known through this same bridging mechanism, not
        // through SnappedSocket. So bridging a NEW block onto the seam of two
        // blocks that are THEMSELVES bridges missed whichever second support
        // each of those underlying bridges was also resting on. This
        // recursively expands: for every newly found support block, also
        // search directly beneath ITS OWN full collider bounds for further
        // support, and keeps expanding until nothing new turns up (capped for
        // safety) - this handles bridges stacked on bridges at any depth, not
        // just one extra level.
        int safetyDepth = 0;

        while (toExpand.Count > 0 && safetyDepth < 8)
        {
            safetyDepth++;
            List<LegoBlock> nextRound = new List<LegoBlock>();

            for (int b = 0; b < toExpand.Count; b++)
            {
                LegoBlock expandBlock = toExpand[b];
                Collider[] expandColliders = expandBlock.GetComponentsInChildren<Collider>();

                if (expandColliders == null || expandColliders.Length == 0)
                    continue;

                Bounds expandBounds = expandColliders[0].bounds;

                for (int c = 1; c < expandColliders.Length; c++)
                {
                    if (!expandColliders[c].isTrigger)
                        expandBounds.Encapsulate(expandColliders[c].bounds);
                }
                float recursiveProbeDepth = 0.10f * GetPlateSeamScaleFactor();
                Vector3 belowCenter = new Vector3(expandBounds.center.x, expandBounds.min.y - recursiveProbeDepth * 0.8f, expandBounds.center.z);
                Vector3 belowHalfExtents = new Vector3(expandBounds.extents.x * 0.9f, recursiveProbeDepth * 0.5f, expandBounds.extents.z * 0.9f);

                Collider[] deeperHits = Physics.OverlapBox(belowCenter, belowHalfExtents, expandBlock.transform.rotation, blockCollisionMask, QueryTriggerInteraction.Ignore);

                for (int h = 0; h < deeperHits.Length; h++)
                {
                    if (deeperHits[h] == null || deeperHits[h].isTrigger)
                        continue;

                    LegoBlock deeperBlock = deeperHits[h].GetComponentInParent<LegoBlock>();

                    if (deeperBlock != null && deeperBlock != block && !result.Contains(deeperBlock))
                    {
                        AddSupportChain(result, deeperBlock);
                        nextRound.Add(deeperBlock);
                    }
                }
            }

            toExpand = nextRound;
        }

        return result;
    }

    private void AddSupportChain(List<LegoBlock> result, LegoBlock startBlock)
    {
        LegoBlock current = startBlock;
        int safety = 0;

        while (current != null && safety < 16)
        {
            if (!result.Contains(current))
                result.Add(current);

            LegoSocket snappedSocket = current.SnappedSocket;

            if (snappedSocket == null)
                break;

            LegoBlock next = snappedSocket.GetComponentInParent<LegoBlock>();

            if (next == null || next == current)
                break;

            current = next;
            safety++;
        }
    }

    /// <summary>
    /// Builds a conservative collision box for one occupied stud cell.
    /// Using per-cell boxes avoids false blocking on L/corner blocks because
    /// empty footprint cells are no longer treated as solid.
    /// </summary>
    private Vector3 GetCandidateCellCollisionHalfExtents(SnapCandidate candidate)
    {
        float scaleX = candidate.parentGrid.transform.lossyScale.x;
        float scaleZ = candidate.parentGrid.transform.lossyScale.z;

        float width = candidate.parentGrid.studSpacingX * Mathf.Abs(scaleX);
        float depth = candidate.parentGrid.studSpacingZ * Mathf.Abs(scaleZ);
        float height = block.height * Mathf.Abs(transform.localScale.y);

        float baseShrink = Mathf.Max(
            0f,
            useFalseRedGhostFix ? collisionCheckShrinkV2 : collisionCheckShrink
        );

        // Scale-aware shrink fix:
        // A fixed 0.10 world-unit shrink was tuned at scale 1. At larger LEGO
        // scale it becomes proportionally too small, so seam/contact bevels on
        // plates can again be counted as overlap. Grow the shrink for scaled-up
        // blocks, but cap it per axis so the collision box never disappears.
        float scaledShrink = baseShrink * GetPlateSeamScaleFactor();
        float shrinkX = Mathf.Min(scaledShrink, width * 0.20f);
        float shrinkZ = Mathf.Min(scaledShrink, depth * 0.20f);
        float shrinkY = Mathf.Min(scaledShrink, height * 0.30f);

        return new Vector3(
            Mathf.Max(0.01f, width * 0.5f - shrinkX),
            Mathf.Max(0.01f, height * 0.5f - shrinkY),
            Mathf.Max(0.01f, depth * 0.5f - shrinkZ)
        );
    }

    private Vector3 GetCandidateCellWorldCenter(SnapCandidate candidate, Vector2Int absoluteCell)
    {
        List<Vector2Int> oneCell = new List<Vector2Int> { absoluteCell };

        return candidate.parentGrid.GetFootprintCenterWorldPosition(
            oneCell,
            GetSnapHeightForGrid(candidate.parentGrid)
        );
    }

    /// <summary>
    /// Measures how close the held block is to a candidate placement.
    /// The comparison is done in the local space of the target grid, so rotated
    /// blocks and high stacks behave more consistently.
    /// </summary>
    private float MeasureCandidateDistance(SnapCandidate candidate)
    {
        if (candidate.parentGrid == null || candidate.socket == null)
            return float.MaxValue;

        // Large-block accuracy:
        // Measure from the exact held stud that would land on this socket.
        Vector3 heldPivotWorld = GetHeldFootprintCellWorldPosition(
            candidate.pivotCell,
            candidate.rotatedFootprint,
            candidate.parentGrid
        );

        Vector3 diff = heldPivotWorld - candidate.socket.transform.position;
        Vector3 localDiff = candidate.parentGrid.transform.InverseTransformVector(diff);

        float scaledMaxAxisOffset = maxAxisOffset * GetScaleAwareFactor();

        if (Mathf.Abs(localDiff.x) > scaledMaxAxisOffset || Mathf.Abs(localDiff.z) > scaledMaxAxisOffset)
            return float.MaxValue;

        float horizontalDistance = new Vector2(localDiff.x, localDiff.z).magnitude;

        if (!useVerticalLevelSelection)
            return horizontalDistance;

        float verticalDistance = GetHeldHeightDifference(candidate);

        if (useHeldHeightForLevelSelection && maxHeldHeightDifference > 0f && verticalDistance > maxHeldHeightDifference)
            return float.MaxValue;

        return horizontalDistance + verticalDistance * Mathf.Max(0f, verticalLevelWeight);
    }

    private float GetHeldHeightDifference(SnapCandidate candidate)
    {
        // Compare against the final ghost/root height, not the raw socket height.
        // This makes the player's held height decide the level: low = floor/plate,
        // high = top of blocks/stacks.
        return Mathf.Abs(GetHeldReferencePosition().y - candidate.worldPosition.y);
    }

    private Vector3 GetHeldFootprintCellWorldPosition(Vector2Int cell, List<Vector2Int> rotatedFootprint, LegoBlockSocketSpawner referenceGrid)
    {
        if (rotatedFootprint == null || rotatedFootprint.Count == 0 || referenceGrid == null)
            return GetHeldReferencePosition();

        RectInt bounds = block.GetFootprintBounds(rotatedFootprint);

        float midX = bounds.xMin + (bounds.width - 1) * 0.5f;
        float midZ = bounds.yMin + (bounds.height - 1) * 0.5f;

        float worldStudX = referenceGrid.studSpacingX * Mathf.Abs(referenceGrid.transform.lossyScale.x);
        float worldStudZ = referenceGrid.studSpacingZ * Mathf.Abs(referenceGrid.transform.lossyScale.z);

        Vector3 localOffset = new Vector3(
            (cell.x - midX) * worldStudX,
            0f,
            (cell.y - midZ) * worldStudZ
        );

        // FIX: localOffset is expressed in the TARGET GRID's own local
        // X/Z axes (the same space rotatedFootprint's cells use) - it only
        // happens to line up with world X/Z when the target grid itself has
        // zero rotation (e.g. the floor). Adding it directly as if it were
        // already a world-space offset meant that moving your hand
        // left/right could feel inverted (or rotated to some other
        // direction) specifically whenever the target grid - a rotated base
        // block, most commonly at 180 degrees where X and Z world directions
        // end up flipped relative to the grid's own axes - wasn't at yaw 0.
        // Rotating localOffset by the grid's actual current rotation first
        // correctly converts it into a real world-space offset before adding
        // it to the hand's world position.
        Vector3 worldOffset = referenceGrid.transform.rotation * localOffset;

        return GetHeldReferencePosition() + worldOffset;
    }

    /// <summary>
    /// Finds the best nearby socket candidate for the currently held block.
    /// Tries every stud of the block's (rotated) footprint as the one that could
    /// land on each candidate socket, so any footprint shape - rectangular or
    /// not - can use any of its own studs as the anchor.
    /// Returns valid candidates first; falls back to the nearest invalid candidate
    /// so a red ghost can still be shown when placement is blocked.
    /// </summary>
    private SnapCandidate FindInitialSnapCandidate()
    {
        // PRIMARY PATH: if the block is being aimed with a ray interactor, use the
        // socket exactly under the ray's current hit point - deterministic, no
        // scoring, no hysteresis, no competing candidates from other levels. This
        // is what makes placement match the pointer exactly.
        if (TryGetRayHit(out RaycastHit rayHit))
        {
            SnapCandidate direct = FindCandidateFromRayHit(rayHit);

            if (direct.socket != null)
                return direct;

            // Ray is pointing somewhere with no valid grid/socket at all (e.g. empty
            // air, a wall, or the block's own body) - show nothing rather than
            // falling back to a distance-based guess from a possibly far-away hand.
            return new SnapCandidate { socket = null, distance = float.MaxValue };
        }

        // A ray interactor IS holding the block, it just has no hit THIS FRAME
        // (brief tremor, grazing a collider edge, an awkward tight angle). Do NOT
        // fall through to the legacy distance/height search below - that measures
        // from the physically held object's position instead of the ray, which can
        // be far from where the ray was pointing and causes a jarring, wrong-looking
        // jump for that one frame. Just show nothing until the ray finds a hit again.
        if (IsBeingHeldByRayInteractor())
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        // FALLBACK PATH: no ray interactor is currently selecting this block (e.g.
        // direct hand grab). Use the original nearby-socket distance/height search.
        LegoSocket[] allSockets = GetCandidateSocketsFromTargetSurface();

        if (allSockets == null || allSockets.Length == 0)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        SnapCandidate bestValid = new SnapCandidate { socket = null, distance = float.MaxValue };
        SnapCandidate bestInvalid = new SnapCandidate { socket = null, distance = float.MaxValue };

        foreach (LegoSocket socket in allSockets)
        {
            if (!IsSocketUsableForInvalidSearch(socket))
                continue;

            // FIX: yawStep/rotatedFootprint used to be computed ONCE, outside
            // this loop, using the held block's raw world yaw - but allSockets
            // can contain sockets from MULTIPLE different grids at once (the
            // floor plus one or more nearby placed blocks), each potentially
            // rotated differently. Computed per-socket here, relative to THAT
            // socket's own grid, exactly like the RefreshCandidate fix above -
            // otherwise this reintroduces the same "lands wrong on a rotated
            // base" bug for the very first candidate found, before any
            // rotation-in-place refresh ever runs.
            int yawStep = GetYawStepRelativeToGrid(socket.parentGrid);
            List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

            if (!IsSocketNearHeldBlock(socket, rotatedFootprint))
                continue;

            // FIX: try EVERY stud of the held footprint as a possible anchor,
            // not just a single fixed index - this is what lets the block hang
            // over ANY edge of the target (overhang), from any side, regardless
            // of current rotation. The previous "always use index 0" fix solved
            // a real problem (sockets jumping between neighbours depending on
            // rotation), but that problem was caused by picking "whichever stud
            // happens to validate first" - not by trying multiple studs itself.
            // Since candidates here are compared purely by real geometric
            // distance (IsBetterSnapCandidate/MeasureCandidateDistance), trying
            // every stud is safe and stable: the closest actual physical
            // alignment always wins, deterministically, exactly like the ray
            // (laser pointer) path already does below in FindCandidateFromRayHit.
            for (int i = 0; i < rotatedFootprint.Count; i++)
            {
                Vector2Int pivotCell = rotatedFootprint[i];
                Vector2Int trueLocalPivotCell = i < block.GetStudFootprint().Count
                    ? block.GetStudFootprint()[i]
                    : pivotCell;

                int anchorGridX = socket.gridX - pivotCell.x;
                int anchorGridZ = socket.gridZ - pivotCell.y;

                SnapCandidate candidate = BuildCandidateFromAnchor(socket, anchorGridX, anchorGridZ, rotatedFootprint, pivotCell, trueLocalPivotCell);
                candidate.distance = MeasureCandidateDistance(candidate);

                if (candidate.distance == float.MaxValue)
                    continue;

                if (candidate.distance > snapDistanceThreshold * GetScaleAwareFactor())
                    continue;

                // STICKINESS FIX: trying every stud as a possible anchor (above)
                // means many near-tied candidates now compete for the same hand
                // position every frame - without any memory of what was shown
                // last frame. That let the search jump straight past legitimate,
                // reachable in-between overhang positions to whichever OTHER
                // stud/socket combo scored a hair better this exact frame - the
                // "skips certain valid spots" you're seeing in the screenshots.
                //
                // Giving the candidate that matches the CURRENTLY active
                // placement a small distance bonus makes it keep winning close
                // calls against competing alternatives, so slowly sweeping the
                // hand actually rests on every valid in-between position instead
                // of hopping over some of them. This only affects near-ties
                // (bounded by candidateSwitchMargin) - a clearly closer socket
                // still wins normally.
                if (hasActiveCandidate &&
                    activeCandidate.socket == socket &&
                    activeCandidate.anchorGridX == anchorGridX &&
                    activeCandidate.anchorGridZ == anchorGridZ)
                {
                    candidate.distance -= candidateSwitchMargin * GetScaleAwareFactor();
                }

                // Use the fast logical check while scanning. Calling Physics.OverlapBox
                // for every socket candidate makes large grids laggy.
                if (IsCandidateLogicallyValidForPreview(candidate))
                {
                    if (IsBetterSnapCandidate(candidate, bestValid))
                        bestValid = candidate;
                }
                else
                {
                    if (IsBetterSnapCandidate(candidate, bestInvalid))
                        bestInvalid = candidate;
                }
            }
        }

        // Gültiger Kandidat hat immer Vorrang vor ungültigem
        if (bestValid.socket != null)
            return bestValid;

        return bestInvalid;
    }

    /// <summary>
    /// Returns true and outputs the live raycast hit if this block is currently
    /// being selected/aimed by an XRRayInteractor.
    /// </summary>
    private bool TryGetRayHit(out RaycastHit hit)
    {
        hit = default;

        if (grabInteractable == null)
            return false;

        var interactorsSelecting = grabInteractable.interactorsSelecting;

        for (int i = 0; i < interactorsSelecting.Count; i++)
        {
            XRRayInteractor rayInteractor = interactorsSelecting[i] as XRRayInteractor;

            if (rayInteractor == null)
                continue;

            if (rayInteractor.TryGetCurrent3DRaycastHit(out hit))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True if the block is currently being selected/aimed by an XRRayInteractor at
    /// all - regardless of whether that ray currently has a hit this frame. Used to
    /// tell apart "ray interactor present, just no hit this exact frame" (should show
    /// nothing / keep last state) from "no ray interactor involved at all" (true
    /// direct hand-grab, where the old distance/height search fallback is correct).
    /// Without this distinction, a single frame of the ray missing (a small tremor,
    /// grazing the edge of a collider, an awkward overhang angle) would silently fall
    /// through to the legacy system, which measures from the physically held object's
    /// position instead - producing a jarring, wrong-looking jump for that one frame.
    /// </summary>
    private bool IsBeingHeldByRayInteractor()
    {
        if (grabInteractable == null)
            return false;

        var interactorsSelecting = grabInteractable.interactorsSelecting;

        for (int i = 0; i < interactorsSelecting.Count; i++)
        {
            if (interactorsSelecting[i] as XRRayInteractor != null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a candidate directly from the live ray hit: finds whichever grid the
    /// ray is actually touching, rounds the hit point to the nearest cell on THAT
    /// grid, and tries each footprint stud as the one landing on that exact cell.
    /// No neighbourhood search, no scoring across multiple sockets/levels - the
    /// ray tells us precisely which surface and which spot, so we just use it.
    /// </summary>
    private SnapCandidate FindCandidateFromRayHit(RaycastHit hit)
    {
        SnapCandidate none = new SnapCandidate { socket = null, distance = float.MaxValue };

        if (hit.collider == null)
            return none;

        LegoBlock hitBlock = hit.collider.GetComponentInParent<LegoBlock>();

        // Ignore hits on the block currently being held.
        if (hitBlock == block)
            return none;

        LegoBlockSocketSpawner grid = hit.collider.GetComponentInParent<LegoBlockSocketSpawner>();

        if (grid == null && hitBlock != null)
            grid = hitBlock.GetComponentInChildren<LegoBlockSocketSpawner>(true);

        if (grid == null)
            return none;

        float safeSpacingX = Mathf.Max(0.0001f, grid.studSpacingX);
        float safeSpacingZ = Mathf.Max(0.0001f, grid.studSpacingZ);

        Vector3 localHit = grid.transform.InverseTransformPoint(hit.point);

        float hitGridXf = (localHit.x - grid.offsetX) / safeSpacingX;
        float hitGridZf = (localHit.z - grid.offsetZ) / safeSpacingZ;

        int yawStep = GetYawStepRelativeToGrid(grid);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        if (rotatedFootprint == null || rotatedFootprint.Count == 0)
            return none;

        RectInt bounds = block.GetFootprintBounds(rotatedFootprint);

        // Search sockets on THIS SAME grid (the one the ray is actually touching)
        // within roughly the block's own footprint size of the ray-hit point.
        // Restricting to a single grid avoids ever mixing up floor vs. stacked-block
        // levels (the "floating in the air" bug from earlier).
        float radiusX = bounds.width * 0.5f + 1f;
        float radiusZ = bounds.height * 0.5f + 1f;

        List<LegoSocket> nearby = grid.GetSocketsNearIndex(hitGridXf, hitGridZf, radiusX, radiusZ);

        if (nearby == null || nearby.Count == 0)
            return none;

        SnapCandidate bestValid = none;
        SnapCandidate bestInvalid = none;

        foreach (LegoSocket socket in nearby)
        {
            if (!IsSocketUsableForInvalidSearch(socket))
                continue;

            // Try every stud of the held footprint as the one that could land on
            // this socket - this is what lets a 1x3 block hang either its "1x2"
            // side or its lone "1x1" end over an edge, depending on where exactly
            // you point. Which alignment wins is decided purely by real geometric
            // distance below (MeasureCandidateDistance), not by list order - so
            // rotating the block only changes the result when the actual geometry
            // changes, never as an arbitrary side effect of Inspector cell order.
            for (int i = 0; i < rotatedFootprint.Count; i++)
            {
                Vector2Int pivotCell = rotatedFootprint[i];
                Vector2Int trueLocalPivotCell = i < block.GetStudFootprint().Count
                    ? block.GetStudFootprint()[i]
                    : pivotCell;

                int anchorGridX = socket.gridX - pivotCell.x;
                int anchorGridZ = socket.gridZ - pivotCell.y;

                SnapCandidate candidate = BuildCandidateFromAnchor(socket, anchorGridX, anchorGridZ, rotatedFootprint, pivotCell, trueLocalPivotCell);
                candidate.distance = MeasureCandidateDistance(candidate);

                if (candidate.distance == float.MaxValue)
                    continue;

                if (IsCandidateLogicallyValidForPreview(candidate))
                {
                    if (IsBetterSnapCandidate(candidate, bestValid))
                        bestValid = candidate;
                }
                else
                {
                    if (IsBetterSnapCandidate(candidate, bestInvalid))
                        bestInvalid = candidate;
                }
            }
        }

        return bestValid.socket != null
            ? bestValid
            : LogAndReturnRayCandidateFallback(bestInvalid, nearby, hitGridXf, hitGridZf, rotatedFootprint);
    }

    /// <summary>
    /// TEMPORARY DIAGNOSTIC: when FindCandidateFromRayHit would return "nothing at
    /// all" (no valid AND no invalid candidate - meaning the outer code reports
    /// "no socket found at all"), this logs exactly how many nearby sockets were
    /// found and how many were rejected by IsSocketUsableForInvalidSearch vs by
    /// MeasureCandidateDistance's axis-offset clamp, so we can see which stage
    /// is actually eliminating every option.
    /// </summary>
    private SnapCandidate LogAndReturnRayCandidateFallback(
        SnapCandidate bestInvalid,
        List<LegoSocket> nearby,
        float hitGridXf,
        float hitGridZf,
        List<Vector2Int> rotatedFootprint
    )
    {
        if (debugPlacementFailures && bestInvalid.socket == null)
        {
            int usableCount = 0;
            int clippedByDistance = 0;

            foreach (LegoSocket socket in nearby)
            {
                if (!IsSocketUsableForInvalidSearch(socket))
                    continue;

                usableCount++;

                for (int i = 0; i < rotatedFootprint.Count; i++)
                {
                    Vector2Int pivotCell = rotatedFootprint[i];
                    int anchorGridX = socket.gridX - pivotCell.x;
                    int anchorGridZ = socket.gridZ - pivotCell.y;

                    SnapCandidate candidate = BuildCandidateFromAnchor(socket, anchorGridX, anchorGridZ, rotatedFootprint, pivotCell, pivotCell);
                    float d = MeasureCandidateDistance(candidate);

                    if (d == float.MaxValue)
                        clippedByDistance++;
                }
            }

            Debug.Log(
                "[RAY CANDIDATE EMPTY] hitGridX=" + hitGridXf.ToString("F2") +
                " hitGridZ=" + hitGridZf.ToString("F2") +
                " nearbyCount=" + nearby.Count +
                " usableSockets=" + usableCount +
                " pivotAttemptsClippedByAxisOffset=" + clippedByDistance +
                " (maxAxisOffset clamp in MeasureCandidateDistance likely the culprit if this is high)"
            );
        }

        return bestInvalid;
    }

    private bool IsSocketNearHeldBlock(LegoSocket socket, List<Vector2Int> rotatedFootprint)
    {
        if (!useNearbySocketPruning)
            return true;

        if (socket == null || socket.parentGrid == null || rotatedFootprint == null || rotatedFootprint.Count == 0)
            return true;

        LegoBlockSocketSpawner grid = socket.parentGrid;
        RectInt bounds = block.GetFootprintBounds(rotatedFootprint);

        float safeSpacingX = Mathf.Max(0.0001f, grid.studSpacingX);
        float safeSpacingZ = Mathf.Max(0.0001f, grid.studSpacingZ);

        Vector3 localHeld = grid.transform.InverseTransformPoint(GetHeldReferencePosition());

        float heldGridX = (localHeld.x - grid.offsetX) / safeSpacingX;
        float heldGridZ = (localHeld.z - grid.offsetZ) / safeSpacingZ;

        float radiusX = bounds.width * 0.5f + Mathf.Max(0, nearbySocketExtraRadiusInStuds);
        float radiusZ = bounds.height * 0.5f + Mathf.Max(0, nearbySocketExtraRadiusInStuds);

        if (Mathf.Abs(socket.gridX - heldGridX) > radiusX)
            return false;

        if (Mathf.Abs(socket.gridZ - heldGridZ) > radiusZ)
            return false;

        return true;
    }

    /// <summary>
    /// Chooses the better snap candidate.
    ///
    /// Important: the old version always preferred the higher candidate first.
    /// That made top sockets on a nearby block steal the preview even when the
    /// hand was trying to place a block on the floor socket directly beside it.
    ///
    /// New rule:
    /// 1) The horizontally closest candidate wins.
    /// 2) Only when two candidates are almost equally close, prefer the higher one.
    ///
    /// This keeps stacking working, but stops the ghost from skipping free floor
    /// cells beside an existing block.
    /// </summary>
    private bool IsBetterSnapCandidate(SnapCandidate candidate, SnapCandidate currentBest)
    {
        if (candidate.socket == null)
            return false;

        if (currentBest.socket == null)
            return true;

        const float almostSameDistance = 0.10f;
        float distanceDelta = candidate.distance - currentBest.distance;

        if (distanceDelta < -almostSameDistance)
            return true;

        if (distanceDelta > almostSameDistance)
            return false;

        if (useHeldHeightForLevelSelection)
        {
            float candidateHeightDelta = GetHeldHeightDifference(candidate);
            float currentHeightDelta = GetHeldHeightDifference(currentBest);

            if (candidateHeightDelta < currentHeightDelta - 0.03f)
                return true;

            if (candidateHeightDelta > currentHeightDelta + 0.03f)
                return false;
        }

        // FIX: within tie-breaking range, strongly prefer the candidate whose
        // TRUE pivot is the block's own local origin (0,0) over any other
        // stud. Previously, which exact stud got picked as the anchor for a
        // roughly-centered aim could vary based on tiny differences in hand
        // position - not wrong on its own (this is exactly what makes
        // overhang possible), but it meant a simple "aim at a point and
        // rotate" could unpredictably lock onto a non-origin stud, giving
        // visually inconsistent rotation behavior for no obvious reason. This
        // only affects genuine near-ties (within almostSameDistance) - a
        // clearly closer non-origin stud (e.g. deliberately overhanging an
        // edge) still correctly wins on raw distance above.
        bool candidateIsOrigin = candidate.trueLocalPivotCell == Vector2Int.zero;
        bool currentBestIsOrigin = currentBest.trueLocalPivotCell == Vector2Int.zero;

        if (candidateIsOrigin && !currentBestIsOrigin)
            return true;

        if (!candidateIsOrigin && currentBestIsOrigin)
            return false;

        return candidate.distance < currentBest.distance;
    }

    /// <summary>
    /// Returns true if the socket can be considered during initial snap search.
    /// Only free (unoccupied) sockets pass this check.
    /// </summary>
    private bool IsSocketUsableForInitialSearch(LegoSocket socket)
    {
        if (socket == null) return false;
        if (!socket.isInnerSocket) return false;
        if (socket.isOccupied) return false;
        if (socket.transform.IsChildOf(transform)) return false;
        if (socket.parentGrid == null) return false;

        // Important:
        // Floor/grid sockets can still exist under already placed blocks.
        // If we allow those sockets, the ghost can appear inside/under a block
        // and the held block can be placed on the invisible floor grid instead
        // of on the visible block top sockets.
        if (IsFloorSocketBlockedByPlacedBlockAbove(socket))
            return false;

        // The block should approach the socket from above, but this must be
        // forgiving. On high stacks and small 1x1 blocks the hand/controller
        // can easily be a bit below the target socket even though the intended
        // placement is clear.
        if (GetHeldReferencePosition().y < socket.transform.position.y - allowedBelowSocket)
            return false;

        return true;
    }

    /// <summary>
    /// Like IsSocketUsableForInitialSearch, but also allows occupied sockets
    /// so that a red ghost can be shown when placement is blocked.
    /// </summary>
    private bool IsSocketUsableForInvalidSearch(LegoSocket socket)
    {
        if (socket == null) return false;
        if (!socket.isInnerSocket) return false;
        if (socket.transform.IsChildOf(transform)) return false;
        if (socket.parentGrid == null) return false;

        // Important:
        // Do not consider occupied sockets even for a red/invalid ghost.
        // When a plate sits on another block, the lower block's top sockets
        // are occupied by that plate. If those occupied sockets are allowed,
        // the ghost can appear at the lower block level and look like it is
        // ignoring the plate.
        if (socket.isOccupied)
            return false;

        // Even for invalid/red preview we do not want to preview hidden floor
        // sockets that are physically covered by an already placed block.
        if (IsFloorSocketBlockedByPlacedBlockAbove(socket))
            return false;

        if (GetHeldReferencePosition().y < socket.transform.position.y - allowedBelowSocket)
            return false;

        return true;
    }

    /// <summary>
    /// Returns true for independent floor/grid sockets that should be ignored
    /// because they are under an already placed block.
    ///
    /// Important fix:
    /// The previous version used a long upward raycast of 3 units. That can
    /// accidentally hit slightly oversized block colliders beside the socket,
    /// so free floor cells next to a placed block become unsnappable.
    ///
    /// Now the normal occupation state is the main blocker. This is the clean
    /// logical LEGO-grid answer: when a block is placed on the floor grid, its
    /// footprint sockets are marked occupied. Neighbor sockets stay free.
    ///
    /// The physical raycast is optional and short. Only enable it if you still
    /// have hidden floor ghosts under blocks whose floor sockets are not being
    /// marked occupied for some reason.
    /// </summary>
    private bool IsFloorSocketBlockedByPlacedBlockAbove(LegoSocket socket)
    {
        if (socket == null)
            return false;

        // If the socket belongs to a LEGO block, it is a top socket and should
        // still be usable for stacking. This blocker is only for independent
        // floor/grid sockets under blocks.
        LegoBlock socketOwnerBlock = socket.GetComponentInParent<LegoBlock>();

        if (socketOwnerBlock != null)
            return false;

        // Main rule: floor sockets that are already occupied by the logical
        // grid cannot be used. This does not block free neighbor sockets.
        if (socket.isOccupied)
            return true;

        // Usually keep this disabled. It is only a fallback for scenes where
        // the floor grid's occupied state is not updated correctly.
        if (!usePhysicalFloorSocketBlocker)
            return false;

        Vector3 origin = socket.transform.position + Vector3.up * floorSocketBlockerStartLift;
        float checkDistance = Mathf.Max(0.01f, floorSocketBlockerRayDistance);

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.up,
            checkDistance,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;

            if (hitCollider == null)
                continue;

            LegoBlock hitBlock = hitCollider.GetComponentInParent<LegoBlock>();

            if (hitBlock == null)
                continue;

            // Ignore the block currently being held.
            if (hitBlock == block)
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the currently locked socket is still close enough to keep.
    /// </summary>
    private bool IsSocketReachable(LegoSocket socket)
    {
        if (!IsSocketUsableForInitialSearch(socket))
            return false;

        SnapCandidate candidate = ComputeCandidateForLockedSocket(socket);
        float horizontalDistance = MeasureCandidateDistance(candidate);

        float releaseDistance = snapDistanceThreshold * GetScaleAwareFactor() * releaseDistanceMultiplier;
        return horizontalDistance <= releaseDistance;
    }

    // -------------------------------------------------------------------------
    // Surface Targeting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns sockets from the single surface currently under the held block.
    /// This is the main fix for plates/stacks:
    /// first choose the top surface, then snap only to that surface's sockets.
    /// </summary>
    private LegoSocket[] GetCandidateSocketsFromTargetSurface()
    {
        // PERFORMANCE FIX: This used to call GetAllSocketsCached(), which scans EVERY
        // LegoSocket in the entire scene via FindObjectsOfType (up to ~40x/sec while a
        // block is held). That cost scales with the TOTAL number of sockets on the floor
        // grid, which can now be in the tens of thousands (worst-case-sized grids built
        // once for the smallest possible block scale). GetNearbySocketsFast() replaces
        // that full scan with cheap, index-based lookups near the held block instead -
        // its cost stays roughly constant regardless of total grid size.
        if (useHeldHeightForLevelSelection)
            return GetNearbySocketsFast();

        if (!useSurfaceRaycastTargeting)
            return GetNearbySocketsFast();

        LegoBlockSocketSpawner targetGrid = FindTargetSurfaceGrid();

        if (targetGrid == null)
        {
            if (debugSurfaceTargeting)
                Debug.Log("[LEGO SURFACE TARGET] " + gameObject.name + " no target grid found, falling back to nearby sockets.");

            return GetNearbySocketsFast();
        }

        if (debugSurfaceTargeting)
        {
            LegoBlock ownerBlock = targetGrid.GetComponentInParent<LegoBlock>();
            Debug.Log(
                "[LEGO SURFACE TARGET] " + gameObject.name +
                " targetGrid=" + targetGrid.name +
                " owner=" + (ownerBlock != null ? ownerBlock.name : "Floor/NoOwner")
            );
        }

        // A single specific target grid was found (e.g. the top of one specific block).
        // Floor grids use the lazy indexed lookup (huge potential socket count). Block-
        // mounted grids typically have their sockets baked directly into the block
        // prefab (small, fixed count, e.g. 4 for a 2x2 block) - GetComponentsInChildren
        // is correct and cheap for those, since there's nothing to lazily create.
        LegoBlock targetOwnerBlock = targetGrid.GetComponentInParent<LegoBlock>();

        if (targetOwnerBlock == null)
            return GetFloorSocketsNearHeldBlock(targetGrid).ToArray();

        return targetGrid.GetComponentsInChildren<LegoSocket>(true);
    }

    // Reused each call to avoid per-frame allocations from Physics.OverlapSphere.
    // Made generous (1500) so the actual LegoBlock is always found even with many
    // nearby socket colliders, WITHOUT needing any special Physics Layer setup at all.
    private static readonly Collider[] nearbyBlockColliderBuffer = new Collider[1500];

    [Header("Nearby Block Search (stacking)")]
    [Tooltip("TEMPORARY DEBUG AID: logs how many nearby blocks/spawners/sockets were found each search while holding a block. Turn on to diagnose stacking issues, turn off afterward (it spams the console).")]
    [SerializeField] private bool debugStackingSearch = false;

    /// <summary>
    /// Fast replacement for "get every socket in the scene, then filter by distance".
    /// Gathers candidates from two cheap sources instead of one expensive full scan:
    /// 
    /// 1. Floor grid(s): direct index-based lookup (LegoBlockSocketSpawner.GetSocketsNearIndex),
    ///    cost independent of how many thousands of sockets the floor actually has.
    /// 2. Stacked sockets on nearby placed blocks: found via a small-radius physics
    ///    overlap (cheap, Unity's broad-phase handles this efficiently) instead of
    ///    scanning every LegoSocket component in the scene.
    /// </summary>
    private LegoSocket[] GetNearbySocketsFast()
    {
        List<LegoSocket> result = new List<LegoSocket>();

        int yawStep = GetEffectiveYawStep();
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);
        RectInt bounds = block.GetFootprintBounds(rotatedFootprint);

        float extraRadius = Mathf.Max(0, nearbySocketExtraRadiusInStuds);

        // --- Floor grid(s): fast index lookup ---
        LegoBlockSocketSpawner[] allSpawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        float maxStudSpacing = 0.5f;

        foreach (LegoBlockSocketSpawner spawner in allSpawners)
        {
            if (spawner == null || spawner.GetComponentInParent<LegoBlock>() != null)
                continue; // only floor-level grids here; block-mounted grids handled below

            result.AddRange(GetFloorSocketsNearHeldBlock(spawner, bounds, extraRadius));

            maxStudSpacing = Mathf.Max(maxStudSpacing, spawner.studSpacingX, spawner.studSpacingZ);
        }

        // --- Stacked sockets on nearby placed blocks ---
        float worldSearchRadius = Mathf.Max(1.0f, (bounds.width + bounds.height) * 0.5f * maxStudSpacing + extraRadius * maxStudSpacing);

        // FIX: this used to be QueryTriggerInteraction.Collide, which ALSO
        // matches trigger colliders - and LEGO socket objects (LegoSocketInteractor,
        // used for XR socket detection) almost certainly use trigger colliders.
        // Once Auto Fit To Floor correctly populates a large floor with its
        // real, dense grid of individual socket objects (thousands of small
        // trigger colliders), this search's FIXED-SIZE buffer
        // (nearbyBlockColliderBuffer, only 1500 slots) could get filled up
        // with floor-socket trigger hits before ever reaching the ONE actual
        // block collider we're looking for - silently dropping the real
        // target block from the results. That's exactly why placing directly
        // on the floor always worked (a completely different, index-based
        // lookup - no physics query at all) while stacking onto another
        // block became unreliable specifically once the floor grid was
        // densely populated, and why removing the floor grid entirely "fixed"
        // it (far fewer competing trigger colliders in range).
        //
        // Blocks' own physical colliders are solid (non-trigger) - see
        // CacheHeldOwnColliders/the collision-ignore system elsewhere in this
        // file - so Ignore here still finds every real placed block, it just
        // stops LEGO socket trigger zones from ever competing for the buffer.
        int hitCount = Physics.OverlapSphereNonAlloc(
            GetHeldReferencePosition(),
            worldSearchRadius,
            nearbyBlockColliderBuffer,
            blockCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        if (debugStackingSearch)
            Debug.Log($"[STACK DEBUG] {gameObject.name}: searchRadius={worldSearchRadius:F2}, hitCount={hitCount}");

        int blocksFound = 0;
        int spawnersFound = 0;
        int socketsFoundTotal = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = nearbyBlockColliderBuffer[i];

            if (hitCollider == null)
                continue;

            LegoBlock nearbyBlock = hitCollider.GetComponentInParent<LegoBlock>();

            if (nearbyBlock == null || nearbyBlock.gameObject == gameObject)
                continue;

            blocksFound++;

            LegoBlockSocketSpawner stackSpawner = nearbyBlock.GetComponentInChildren<LegoBlockSocketSpawner>(true);

            if (stackSpawner == null)
                continue;

            spawnersFound++;

            // Blocks have their sockets baked directly into the prefab (small, fixed
            // count) - GetComponentsInChildren is correct and cheap here, no lazy
            // creation needed (that's only relevant for the huge floor grid).
            LegoSocket[] stackSockets = stackSpawner.GetComponentsInChildren<LegoSocket>(true);
            socketsFoundTotal += stackSockets.Length;

            for (int s = 0; s < stackSockets.Length; s++)
            {
                if (stackSockets[s] != null && !result.Contains(stackSockets[s]))
                    result.Add(stackSockets[s]);
            }
        }

        if (debugStackingSearch)
            Debug.Log($"[STACK DEBUG] {gameObject.name}: blocksFound={blocksFound}, spawnersFound={spawnersFound}, socketsFoundTotal={socketsFoundTotal}, resultCount={result.Count}");

        return result.ToArray();
    }

    private List<LegoSocket> GetFloorSocketsNearHeldBlock(LegoBlockSocketSpawner spawner)
    {
        int yawStep = GetEffectiveYawStep();
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);
        RectInt bounds = block.GetFootprintBounds(rotatedFootprint);
        float extraRadius = Mathf.Max(0, nearbySocketExtraRadiusInStuds);

        return GetFloorSocketsNearHeldBlock(spawner, bounds, extraRadius);
    }

    private List<LegoSocket> GetFloorSocketsNearHeldBlock(LegoBlockSocketSpawner spawner, RectInt bounds, float extraRadius)
    {
        if (spawner == null)
            return new List<LegoSocket>();

        float safeSpacingX = Mathf.Max(0.0001f, spawner.studSpacingX);
        float safeSpacingZ = Mathf.Max(0.0001f, spawner.studSpacingZ);

        Vector3 localHeld = spawner.transform.InverseTransformPoint(GetHeldReferencePosition());

        float heldGridX = (localHeld.x - spawner.offsetX) / safeSpacingX;
        float heldGridZ = (localHeld.z - spawner.offsetZ) / safeSpacingZ;

        float radiusX = bounds.width * 0.5f + extraRadius;
        float radiusZ = bounds.height * 0.5f + extraRadius;

        return spawner.GetSocketsNearIndex(heldGridX, heldGridZ, radiusX, radiusZ);
    }

    private LegoSocket[] GetAllSocketsCached()
    {
        if (cachedAllSockets == null || cachedAllSockets.Length == 0 || Time.time >= nextSocketCacheRefreshTime)
        {
            cachedAllSockets = FindObjectsOfType<LegoSocket>();
            nextSocketCacheRefreshTime = Time.time + Mathf.Max(0.05f, socketCacheRefreshInterval);
        }

        return cachedAllSockets;
    }

    /// <summary>
    /// Finds the top snap surface under the held block using several downward rays.
    /// Multiple rays are used because large blocks may have their root/pivot away
    /// from the stud currently over the target plate.
    /// </summary>
    private LegoBlockSocketSpawner FindTargetSurfaceGrid()
    {
        List<Vector3> rayOrigins = GetSurfaceRayOrigins();

        RaycastHit? bestHit = null;

        for (int i = 0; i < rayOrigins.Count; i++)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigins[i],
                Vector3.down,
                Mathf.Max(0.1f, surfaceRayDistance),
                surfaceRayMask,
                QueryTriggerInteraction.Ignore
            );

            for (int h = 0; h < hits.Length; h++)
            {
                RaycastHit hit = hits[h];

                if (hit.collider == null)
                    continue;

                LegoBlock hitBlock = hit.collider.GetComponentInParent<LegoBlock>();

                // Ignore the block currently being held.
                if (hitBlock == block)
                    continue;

                LegoBlockSocketSpawner grid = hit.collider.GetComponentInParent<LegoBlockSocketSpawner>();

                if (grid == null && hitBlock != null)
                    grid = hitBlock.GetComponentInChildren<LegoBlockSocketSpawner>(true);

                if (grid == null)
                    continue;

                // Pick the highest surface hit, not the closest ray origin hit.
                // This makes a plate sitting on a block beat the lower block/floor.
                if (!bestHit.HasValue || hit.point.y > bestHit.Value.point.y)
                    bestHit = hit;
            }
        }

        if (!bestHit.HasValue)
            return null;

        Collider bestCollider = bestHit.Value.collider;
        LegoBlock bestBlock = bestCollider.GetComponentInParent<LegoBlock>();
        LegoBlockSocketSpawner bestGrid = bestCollider.GetComponentInParent<LegoBlockSocketSpawner>();

        if (bestGrid == null && bestBlock != null)
            bestGrid = bestBlock.GetComponentInChildren<LegoBlockSocketSpawner>(true);

        return bestGrid;
    }

    /// <summary>
    /// Builds ray origins over the held block root and over the approximate
    /// footprint corners. This makes surface selection stable for large blocks.
    /// </summary>
    private List<Vector3> GetSurfaceRayOrigins()
    {
        List<Vector3> origins = new List<Vector3>();

        float lift = Mathf.Max(0.05f, surfaceRayStartHeight);
        Vector3 up = Vector3.up * lift;
        Vector3 referencePosition = GetHeldReferencePosition();

        origins.Add(referencePosition + up);

        int yawStep = GetEffectiveYawStep();
        List<Vector2Int> footprint = block.GetRotatedFootprint(yawStep);

        if (footprint == null || footprint.Count == 0)
            return origins;

        RectInt bounds = block.GetFootprintBounds(footprint);

        // Use current block dimensions as an approximate search footprint.
        // The exact snap still uses the socket grid afterwards.
        float studX = 0.5f;
        float studZ = 0.5f;

        LegoBlockSocketSpawner anyGrid = FindObjectOfType<LegoBlockSocketSpawner>();
        if (anyGrid != null)
        {
            studX = anyGrid.studSpacingX;
            studZ = anyGrid.studSpacingZ;
        }

        float midX = bounds.xMin + (bounds.width - 1) * 0.5f;
        float midZ = bounds.yMin + (bounds.height - 1) * 0.5f;

        for (int x = bounds.xMin; x <= bounds.xMax - 1; x++)
        {
            for (int z = bounds.yMin; z <= bounds.yMax - 1; z++)
            {
                bool isCorner =
                    (x == bounds.xMin || x == bounds.xMax - 1) &&
                    (z == bounds.yMin || z == bounds.yMax - 1);

                if (!isCorner)
                    continue;

                Vector3 local = new Vector3(
                    (x - midX) * studX,
                    0f,
                    (z - midZ) * studZ
                );

                origins.Add(referencePosition + transform.rotation * local + up);
            }
        }

        return origins;
    }

    // -------------------------------------------------------------------------
    // Rotation and Visual Reset
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an upright rotation when the block is picked up.
    /// Keeps the current horizontal facing direction but removes X/Z tipping.
    /// </summary>
    private Quaternion GetUprightRotationFromCurrentYaw()
    {
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float snappedYaw = Mathf.Round(yaw / 90f) * 90f;

        return Quaternion.Euler(0f, snappedYaw, 0f);
    }

    /// <summary>
    /// Applies the current yaw offset to the real Rigidbody root while held.
    ///
    /// IMPORTANT FIX: this used to go through rb.MoveRotation(), which is a
    /// DEFERRED move - Unity only actually applies it at the next FixedUpdate,
    /// not immediately. Meanwhile, the ghost/candidate math (BuildCandidateFromAnchor,
    /// RefreshCandidate) computes its preview rotation INSTANTLY from
    /// CurrentYawOffset, in the very same frame the rotate button was pressed.
    /// That mismatch - ghost already showing the new rotation, real held block
    /// still one physics-tick behind - is exactly the one-time visible "shift"
    /// that showed up right when rotating, and only then (confirmed by testing
    /// with rotation completely disabled: with no yaw changes at all, snapping
    /// and positioning were perfect).
    ///
    /// Since Track Rotation is OFF on the XR Grab Interactable (nothing else ever
    /// touches this object's rotation while held), there is nothing left to
    /// "fight" by writing transform.rotation directly here. Physics.SyncTransforms()
    /// immediately afterward pushes that new pose into the physics engine's
    /// internal representation too, so collider queries reflect it in the very
    /// same frame - no more one-frame lag, no more visible jump on rotate.
    /// </summary>
    private void ApplyHeldRootRotation()
    {
        Quaternion targetRotation =
            heldBaseRotation *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        if (rb != null)
            rb.angularVelocity = Vector3.zero;

        transform.rotation = targetRotation;
        Physics.SyncTransforms();

        ResetVisualRoot();

        // TEMPORARY DIAGNOSTIC: directly compares visualRoot's actual local
        // transform right now against the values ResetVisualRoot() just tried
        // to restore it to. If these ever differ, something is either
        // overriding visualRoot AFTER this point in the same frame, or
        // ResetVisualRoot() itself isn't actually managing to apply the
        // values (e.g. a parent-constraint component fighting it).
        if (visualRoot != null)
        {
            bool posMatches = Vector3.Distance(visualRoot.localPosition, visualRootOriginalLocalPosition) < 0.0001f;
            bool rotMatches = Quaternion.Angle(visualRoot.localRotation, visualRootOriginalLocalRotation) < 0.01f;

            if (!posMatches || !rotMatches)
            {
                Debug.LogWarning(
                    "[VISUALROOT DIAG] MISMATCH after reset! " +
                    "currentLocalPos=" + visualRoot.localPosition.ToString("F4") +
                    " expectedLocalPos=" + visualRootOriginalLocalPosition.ToString("F4") +
                    " currentLocalRot=" + visualRoot.localRotation.eulerAngles.ToString("F4") +
                    " expectedLocalRot=" + visualRootOriginalLocalRotation.eulerAngles.ToString("F4") +
                    " CurrentYawOffset=" + CurrentYawOffset,
                    this
                );
            }
            else
            {
                Debug.Log(
                    "[VISUALROOT DIAG] OK, matches. CurrentYawOffset=" + CurrentYawOffset +
                    " rootWorldRot=" + transform.rotation.eulerAngles.ToString("F4"),
                    this
                );
            }
        }
    }

    /// <summary>
    /// Keeps the visual root attached to this block and restores its original local transform.
    /// </summary>
    private void ResetVisualRoot()
    {
        if (visualRoot == null)
            return;

        // Safety: visualRoot must be a child of this spawned block instance.
        // If the field accidentally points to a Prefab Asset or another object,
        // Unity will throw "Transform resides in a Prefab asset..." when SetParent is called.
        // In that case we do nothing and print a warning instead of breaking snapping.
        if (!visualRoot.IsChildOf(transform))
        {
            Debug.LogWarning(
                $"{gameObject.name}: VisualRoot is not a child of this block instance. " +
                "Clear the Visual Root field or assign the VisualRoot child from inside this prefab instance."
            );
            return;
        }

        visualRoot.localPosition = visualRootOriginalLocalPosition;
        visualRoot.localRotation = visualRootOriginalLocalRotation;

        // TEMPORARY DEBUG - remove once the rotation-drift bug is found.
        Debug.Log(
            $"[VISUALROOT DEBUG] Reset '{gameObject.name}' visualRoot to " +
            $"storedPos={visualRootOriginalLocalPosition:F4} storedRot(euler)={visualRootOriginalLocalRotation.eulerAngles:F4} " +
            $"| NOW ACTUALLY reads pos={visualRoot.localPosition:F4} rot(euler)={visualRoot.localRotation.eulerAngles:F4} " +
            $"| yawOffset={CurrentYawOffset:F1}"
        );
    }

    // -------------------------------------------------------------------------
    // Socket Occupation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Releases all sockets currently occupied by this block and notifies the parent block.
    /// </summary>
    private void ReleaseCurrentOccupiedSocketsAndNotifyParent()
    {
        ReleaseCurrentOccupiedSockets();

        // FIX: this used to guess a single "parentBlock" from currentSocket/
        // currentOccupiedSockets and only notify that one - now we simply
        // release exactly the same set of blocks that were notified when
        // this block was placed (currentlyNotifiedSupportBlocks, populated in
        // TrySnapToCandidate/TrySnapToTarget), covering the anchor AND any
        // bridging supports symmetrically.
        for (int i = 0; i < currentlyNotifiedSupportBlocks.Count; i++)
        {
            if (currentlyNotifiedSupportBlocks[i] != null)
                currentlyNotifiedSupportBlocks[i].RemoveAttachedBlockAbove();
        }

        currentlyNotifiedSupportBlocks.Clear();
    }

    /// <summary>
    /// Frees all sockets currently occupied by this block.
    /// </summary>
    private void ReleaseCurrentOccupiedSockets()
    {
        if (currentOccupiedSockets.Count > 0)
        {
            MarkSocketsOccupied(currentOccupiedSockets, false);
            currentOccupiedSockets.Clear();
        }
        else if (currentSocket != null)
        {
            currentSocket.isOccupied = false;

            Collider socketCollider = currentSocket.GetComponent<Collider>();

            if (socketCollider != null)
                socketCollider.enabled = true;
        }

        currentSocket = null;
        block.SnappedSocket = null;
    }

    /// <summary>
    /// Marks a list of sockets as occupied or free.
    /// Occupied sockets have their colliders disabled.
    /// </summary>
    private void MarkSocketsOccupied(List<LegoSocket> sockets, bool occupied)
    {
        foreach (LegoSocket socket in sockets)
        {
            if (socket == null)
                continue;

            socket.isOccupied = occupied;

            Collider socketCollider = socket.GetComponent<Collider>();

            if (socketCollider != null)
                socketCollider.enabled = !occupied;
        }
    }

    // -------------------------------------------------------------------------
    // Temporary Stabilization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Temporarily stabilizes the block under the target socket while previewing placement.
    /// </summary>
    private void StabilizeBlockUnderSocket(LegoSocket socket)
    {
        LegoBlock blockUnderSocket =
            socket != null ? socket.GetComponentInParent<LegoBlock>() : null;

        if (blockUnderSocket == null)
        {
            ClearTemporaryStabilization();
            return;
        }

        if (blockUnderSocket == block)
            return;

        if (temporarilyStabilizedBlock == blockUnderSocket)
            return;

        ClearTemporaryStabilization();

        temporarilyStabilizedBlock = blockUnderSocket;
        temporarilyStabilizedBlock.BeginTemporaryStabilization();
    }

    /// <summary>
    /// Clears any temporary stabilization applied to another block.
    /// </summary>
    private void ClearTemporaryStabilization()
    {
        if (temporarilyStabilizedBlock == null)
            return;

        temporarilyStabilizedBlock.EndTemporaryStabilization();
        temporarilyStabilizedBlock = null;
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears the current target socket and removes the ghost.
    /// </summary>
    private void ClearTargetAndGhost()
    {
        TargetSocket = null;
        currentPlacementValid = false;
        hasActiveCandidate = false;
        activeCandidate = new SnapCandidate();
        rotationAnchorLockActive = false;
        HideGhost();
    }

    /// <summary>
    /// Enables or disables all LEGO socket interactors while a block is manually held.
    /// </summary>
    private void DisableAllSocketInteractors(bool disable)
    {
        foreach (LegoSocketInteractor socketInteractor in allSocketInteractors)
        {
            if (socketInteractor != null)
                socketInteractor.enabled = !disable;
        }
    }

    /// <summary>
    /// Refreshes the cached list of all socket interactors in the scene.
    /// </summary>
    private void RefreshSocketInteractors()
    {
        allSocketInteractors.Clear();
        allSocketInteractors.AddRange(FindObjectsOfType<LegoSocketInteractor>());
    }

    /// <summary>
    /// Tries to find the socket this block is currently occupying.
    /// </summary>
    private void FindCurrentSocket()
    {
        Collider[] colliders = Physics.OverlapBox(transform.position, Vector3.one * 0.1f);

        foreach (Collider col in colliders)
        {
            LegoSocket socket = col.GetComponent<LegoSocket>();

            if (socket != null && socket.isOccupied)
            {
                currentSocket = socket;
                return;
            }
        }
    }

    /// <summary>
    /// Finds the visual root if it was not assigned manually.
    /// </summary>
    private void FindVisualRoot()
    {
        if (visualRoot != null)
            return;

        Transform foundVisualRoot = transform.Find("VisualRoot");

        if (foundVisualRoot != null)
            visualRoot = foundVisualRoot;
    }

    /// <summary>
    /// Stores the original local position and rotation of the visual root.
    /// </summary>
    private void StoreVisualRootDefaults()
    {
        if (visualRoot == null)
        {
            Debug.LogWarning($"{gameObject.name}: No VisualRoot assigned.");
            return;
        }

        visualRootOriginalLocalPosition = visualRoot.localPosition;
        visualRootOriginalLocalRotation = visualRoot.localRotation;

        // TEMPORARY DEBUG - remove once the rotation-drift bug is found.
        Debug.Log(
            $"[VISUALROOT DEBUG] Stored '{gameObject.name}' visualRoot defaults: " +
            $"pos={visualRootOriginalLocalPosition:F4} rot(euler)={visualRootOriginalLocalRotation.eulerAngles:F4}"
        );
    }

    /// <summary>
    /// Stores mesh data so the ghost can be rebuilt without cloning the whole object.
    /// </summary>
    private void CacheMeshSnapshots()
    {
        meshSnapshots.Clear();

        Transform meshRoot = visualRoot != null ? visualRoot : transform;

        // true = also include inactive child objects. This is useful for prefabs
        // where some visual parts may be disabled/enabled by setup scripts.
        MeshFilter[] meshFilters = meshRoot.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();

            if (renderer == null)
                continue;

            meshSnapshots.Add(new MeshSnapshot
            {
                mesh = meshFilter.sharedMesh,
                localPosition = transform.InverseTransformPoint(meshFilter.transform.position),
                localRotation = Quaternion.Inverse(transform.rotation) * meshFilter.transform.rotation,
                localScale = meshFilter.transform.localScale,
                originalRenderer = renderer
            });
        }

        Debug.Log($"{gameObject.name}: Cached ghost meshes = {meshSnapshots.Count}");
    }

    /// <summary>
    /// Cleans up sockets and state when the block is deleted externally.
    /// </summary>
    public void ForceRelease()
    {
        ReleaseCurrentOccupiedSocketsAndNotifyParent();
        ClearTemporaryStabilization();
        DisableAllSocketInteractors(false);
    }
    // -------------------------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        if (TargetSocket != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(TargetSocket.transform.position, 0.08f);

            if (ghostRoot != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(ghostRoot.transform.position, 0.12f);
                Gizmos.DrawLine(TargetSocket.transform.position, ghostRoot.transform.position);
            }
        }

        // FIX: this used to be in OnDrawGizmos() (runs for EVERY block with
        // this component, all the time, whether selected or not) - meaning
        // every single placed block permanently showed a small green wire
        // sphere at its own transform.position (which, for many block
        // prefabs, sits at the block's visual CENTER rather than a corner
        // stud - see the earlier pivot discussion). That's exactly the
        // "every block has a weird third point in the middle" you were
        // seeing: it was never a real GameObject (hence nothing extra in the
        // Hierarchy), just an always-on debug gizmo. Moving this to
        // OnDrawGizmosSelected keeps it available for debugging one specific
        // block when you actually click on it, without cluttering every
        // block in the scene all the time.
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.06f);
    }
}
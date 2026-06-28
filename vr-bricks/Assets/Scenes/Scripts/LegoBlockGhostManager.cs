using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;

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

    [Header("Floor Socket Blocking")]
    [Tooltip("If enabled, floor sockets can also be blocked by a short physical raycast. Leave this OFF if free cells next to blocks sometimes become unsnappable.")]
    [SerializeField] private bool usePhysicalFloorSocketBlocker = false;

    [Tooltip("Only used when Use Physical Floor Socket Blocker is enabled. Keep this small, otherwise neighboring floor sockets may be blocked by oversized colliders.")]
    [SerializeField] private float floorSocketBlockerRayDistance = 0.35f;

    [Tooltip("Small lift from the floor socket before checking upward for a directly covering block.")]
    [SerializeField] private float floorSocketBlockerStartLift = 0.03f;

    [Tooltip("After rotating, keep the current socket/anchor locked until the hand moves this far. Higher values make rotation independent from tiny hand-position differences.")]
    [SerializeField] private float rotationAnchorUnlockDistance = 0.35f;

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
    /// "Anchor" refers to the block's local footprint origin (cell 0,0 in
    /// LegoBlock.StudFootprint, BEFORE rotation). anchorGridX/anchorGridZ store
    /// where that origin cell currently sits in the target grid. The actual
    /// occupied cells are anchor + each cell of rotatedFootprint.
    /// 
    /// Keeping the anchor fixed while only rotatedFootprint changes is what
    /// allows the block to "rotate in place" around a stable grid position,
    /// for any footprint shape (rectangular or not).
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
        public List<Vector2Int> rotatedFootprint;
        public LegoBlockSocketSpawner parentGrid;
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        block = GetComponent<LegoBlock>();

        FindVisualRoot();
        StoreVisualRootDefaults();
        RefreshSocketInteractors();
        FindCurrentSocket();
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
    /// Rotates the held block clockwise by 90 degrees.
    /// </summary>
    public void RotateClockwise()
    {
        QueueRotationStep(1);
    }

    /// <summary>
    /// Rotates the held block counter-clockwise by 90 degrees.
    /// </summary>
    public void RotateCounterClockwise()
    {
        QueueRotationStep(-1);
    }

    /// <summary>
    /// Queues exactly one 90-degree rotation. The actual ghost rebuild happens
    /// later in HandleHeldState, so the ghost is never rebuilt once with the
    /// old candidate and once with the new candidate in the same visual frame.
    /// </summary>
    private void QueueRotationStep(int step)
    {
        // Guard against accidental double calls in the same frame.
        if (lastRotationRequestFrame == Time.frameCount)
            return;

        lastRotationRequestFrame = Time.frameCount;
        pendingRotationSteps += step;

        if (pendingRotationSteps > 1)
            pendingRotationSteps = 1;
        else if (pendingRotationSteps < -1)
            pendingRotationSteps = -1;
    }

    /// <summary>
    /// Rotates the preview around the same fixed footprint anchor grid cell and
    /// the same target socket area. The anchor (block's local origin stud) stays
    /// pinned to the same grid position; only the rotated footprint around it
    /// changes. This works for any footprint shape, not just rectangles.
    /// </summary>
    private void RotateActiveCandidateInPlace()
    {
        ApplyHeldRootRotation();

        if (!hasActiveCandidate || activeCandidate.socket == null)
        {
            RebuildGhostIfLocked();
            return;
        }

        SnapCandidate rotatedCandidate = RefreshCandidate(activeCandidate);

        if (rotatedCandidate.socket == null)
        {
            RebuildGhostIfLocked();
            return;
        }

        activeCandidate = rotatedCandidate;
        hasActiveCandidate = true;
        TargetSocket = rotatedCandidate.socket;

        rotationAnchorLockActive = true;
        rotationAnchorLockHandPosition = transform.position;

        // RebuildGhostForCandidate shows a valid (blue) or invalid (red) ghost
        // depending on whether the rotated footprint still fits, instead of
        // silently discarding the preview.
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
    /// Called after this block has been successfully snapped.
    /// </summary>
    public void OnSnapped()
    {
        CurrentYawOffset = 0f;
        ResetVisualRoot();

        ClearTargetAndGhost();
        lastBuiltYaw = -999f;
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

        if (pendingRotationSteps != 0)
        {
            ApplyQueuedRotationStep();
            wasHeld = true;
            return;
        }

        ApplyHeldRootRotation();

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

        SnapCandidate refreshed = RefreshCandidate(activeCandidate);

        if (refreshed.socket == null)
            return;

        float maxHoldDistance = snapDistanceThreshold * Mathf.Max(1f, currentCandidateHoldDistanceMultiplier);

        // This check is cheap: no full socket search and no overlap tests.
        // It only prevents an old ghost from sticking when the hand moved far away.
        if (refreshed.distance > maxHoldDistance || refreshed.distance == float.MaxValue)
        {
            ClearTargetAndGhost();
            return;
        }

        activeCandidate = refreshed;
        TargetSocket = refreshed.socket;

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
        int step = pendingRotationSteps;
        pendingRotationSteps = 0;

        CurrentYawOffset = (CurrentYawOffset + step * 90f) % 360f;

        if (CurrentYawOffset < 0f)
            CurrentYawOffset += 360f;

        RotateActiveCandidateInPlace();
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
        transform.rotation = heldBaseRotation;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(heldBaseRotation);
        }

        block.SetSnappedToSocket(false);

        ReleaseCurrentOccupiedSocketsAndNotifyParent();
        DisableAllSocketInteractors(true);
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
            float handMoveDistance = Vector3.Distance(transform.position, rotationAnchorLockHandPosition);

            if (handMoveDistance <= rotationAnchorUnlockDistance)
            {
                SnapCandidate lockedCandidate = RefreshCandidate(activeCandidate);

                if (lockedCandidate.socket != null && IsCandidateValidForPreview(lockedCandidate))
                {
                    activeCandidate = lockedCandidate;
                    hasActiveCandidate = true;
                    TargetSocket = lockedCandidate.socket;
                    RebuildGhostForCandidate(lockedCandidate);
                    return;
                }
            }

            rotationAnchorLockActive = false;
        }

        SnapCandidate bestCandidate = FindInitialSnapCandidate();

        if (bestCandidate.socket == null)
        {
            ClearTemporaryStabilization();

            if (TryKeepCurrentValidCandidate())
                return;

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

            if (hideInvalidGhostPreview)
            {
                ClearTargetAndGhost();
                lastBuiltYaw = -999f;
                return;
            }

            TargetSocket = bestCandidate.socket;
            hasActiveCandidate = true;
            activeCandidate = bestCandidate;
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
            SnapCandidate refreshedActive = RefreshCandidate(activeCandidate);

            if (refreshedActive.socket != null &&
                IsCandidateValidForPreview(refreshedActive) &&
                refreshedActive.distance <= bestCandidate.distance + candidateSwitchMargin)
            {
                bestCandidate = refreshedActive;
            }
        }

        activeCandidate = bestCandidate;
        hasActiveCandidate = true;
        TargetSocket = bestCandidate.socket;

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

        SnapCandidate refreshed = RefreshCandidate(activeCandidate);

        if (refreshed.socket == null)
            return false;

        refreshed.distance = MeasureCandidateDistance(refreshed);

        float maxHoldDistance =
            snapDistanceThreshold * Mathf.Max(1f, currentCandidateHoldDistanceMultiplier);

        if (refreshed.distance == float.MaxValue || refreshed.distance > maxHoldDistance)
            return false;

        if (!IsCandidateValidForPreview(refreshed))
            return false;

        activeCandidate = refreshed;
        hasActiveCandidate = true;
        TargetSocket = refreshed.socket;

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
            !CandidateWouldIntersectOtherBlocks(finalCandidate);

        if (!canPlace)
        {
            FallbackRelease();
            return;
        }

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;

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

        LegoBlock parentBlock = finalCandidate.socket.GetComponentInParent<LegoBlock>();

        if (parentBlock != null)
            parentBlock.AddAttachedBlockAbove();

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
            !CandidateWouldIntersectOtherBlocks(finalCandidate);

        if (!canPlace)
        {
            FallbackRelease();
            return;
        }

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;

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

        LegoBlock parentBlock = finalCandidate.socket.GetComponentInParent<LegoBlock>();

        if (parentBlock != null)
            parentBlock.AddAttachedBlockAbove();

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
    private SnapCandidate BuildCandidateFromAnchor(LegoSocket socket, int anchorGridX, int anchorGridZ, List<Vector2Int> rotatedFootprint, Vector2Int pivotCell)
    {
        Vector3 worldPosition = socket.parentGrid.GetFootprintCenterWorldPosition(
            GetAbsoluteCells(anchorGridX, anchorGridZ, rotatedFootprint),
            GetSnapHeightForGrid(socket.parentGrid)
        );

        Quaternion worldRotation =
            socket.GetBaseRotation() *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        return new SnapCandidate
        {
            socket = socket,
            worldPosition = worldPosition,
            worldRotation = worldRotation,
            anchorGridX = anchorGridX,
            anchorGridZ = anchorGridZ,
            pivotCell = pivotCell,
            rotatedFootprint = rotatedFootprint,
            parentGrid = socket.parentGrid
        };
    }

    /// <summary>
    /// Calculates the final block position and rotation for a locked socket,
    /// assuming the block's footprint anchor (local cell 0,0) sits on this socket.
    /// </summary>
    private SnapCandidate ComputeCandidateForLockedSocket(LegoSocket socket)
    {
        int yawStep = GetYawStep(CurrentYawOffset);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        return BuildCandidateFromAnchor(socket, socket.gridX, socket.gridZ, rotatedFootprint, Vector2Int.zero);
    }

    /// <summary>
    /// Recalculates world position, rotation, footprint, and distance for a stored
    /// candidate at the current yaw, while keeping its footprint anchor grid
    /// position fixed. This is what makes rotation happen "in place".
    /// </summary>
    private SnapCandidate RefreshCandidate(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        int yawStep = GetYawStep(CurrentYawOffset);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        candidate.rotatedFootprint = rotatedFootprint;
        candidate.pivotCell = new Vector2Int(
            candidate.socket.gridX - candidate.anchorGridX,
            candidate.socket.gridZ - candidate.anchorGridZ
        );

        candidate.worldPosition = candidate.parentGrid.GetFootprintCenterWorldPosition(
            GetAbsoluteCells(candidate.anchorGridX, candidate.anchorGridZ, rotatedFootprint),
            GetSnapHeightForGrid(candidate.parentGrid)
        );

        candidate.worldRotation =
            candidate.socket.GetBaseRotation() *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

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
    /// Returns true if this candidate can currently be shown as a valid placement.
    /// </summary>
    private bool IsCandidateValidForPreview(SnapCandidate candidate)
    {
        if (!IsCandidateLogicallyValidForPreview(candidate))
            return false;

        if (CandidateWouldIntersectOtherBlocks(candidate))
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

        float activeVerticalTolerance = useFalseRedGhostFix
            ? verticalTouchToleranceV2
            : verticalTouchTolerance;

        // Bridge / edge support fix:
        // When a new block is placed across two side-by-side blocks, only the
        // socket owner under the chosen pivot was previously treated as support.
        // The neighbouring support block's studs could still overlap the
        // preview-cell OverlapBox and turn the placement invalid. If a hit is
        // basically below the candidate bottom, treat it as support too. This
        // keeps real side intersections blocked, because same-level blocks have
        // bounds that extend far above candidateBottom.
        if (IsNeighbourSupportBelowCandidate(otherBounds, candidateBottom))
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

    private bool IsNeighbourSupportBelowCandidate(Bounds otherBounds, float candidateBottom)
    {
        float tolerance = Mathf.Max(0f, neighbourSupportVerticalTolerance);

        // Normal support case: the top of the other block/studs is just below
        // or slightly above the candidate bottom because of rounded stud
        // colliders. This is exactly the case for setting one block across the
        // seam between two support blocks.
        if (otherBounds.max.y <= candidateBottom + tolerance)
            return true;

        // Safety: if the block's center is still below the candidate bottom and
        // only studs/collider bevels protrude upward, it is support, not a side
        // blocker. A block at the same placement level will not pass this test.
        if (otherBounds.center.y < candidateBottom && otherBounds.min.y < candidateBottom - tolerance * 0.5f)
            return true;

        return false;
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

        float shrink = Mathf.Max(
            0f,
            useFalseRedGhostFix ? collisionCheckShrinkV2 : collisionCheckShrink
        );

        return new Vector3(
            Mathf.Max(0.01f, width * 0.5f - shrink),
            Mathf.Max(0.01f, height * 0.5f - shrink),
            Mathf.Max(0.01f, depth * 0.5f - shrink)
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

        if (Mathf.Abs(localDiff.x) > maxAxisOffset || Mathf.Abs(localDiff.z) > maxAxisOffset)
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
        return Mathf.Abs(transform.position.y - candidate.worldPosition.y);
    }

    private Vector3 GetHeldFootprintCellWorldPosition(Vector2Int cell, List<Vector2Int> rotatedFootprint, LegoBlockSocketSpawner referenceGrid)
    {
        if (rotatedFootprint == null || rotatedFootprint.Count == 0 || referenceGrid == null)
            return transform.position;

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

        return transform.position + transform.rotation * localOffset;
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
        int yawStep = GetYawStep(CurrentYawOffset);
        List<Vector2Int> rotatedFootprint = block.GetRotatedFootprint(yawStep);

        LegoSocket[] allSockets = GetCandidateSocketsFromTargetSurface();

        if (allSockets == null || allSockets.Length == 0)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        SnapCandidate bestValid = new SnapCandidate { socket = null, distance = float.MaxValue };
        SnapCandidate bestInvalid = new SnapCandidate { socket = null, distance = float.MaxValue };

        foreach (LegoSocket socket in allSockets)
        {
            if (!IsSocketUsableForInvalidSearch(socket))
                continue;

            if (!IsSocketNearHeldBlock(socket, rotatedFootprint))
                continue;

            // Try every physical stud of the held block's footprint as the one
            // that lands on this socket. This is the important part for 1x1
            // blocks and middle/side studs of irregular shapes: an L-block can
            // land any of its three studs on the same target socket.
            for (int i = 0; i < rotatedFootprint.Count; i++)
            {
                Vector2Int pivotCell = rotatedFootprint[i];

                int anchorGridX = socket.gridX - pivotCell.x;
                int anchorGridZ = socket.gridZ - pivotCell.y;

                SnapCandidate candidate = BuildCandidateFromAnchor(socket, anchorGridX, anchorGridZ, rotatedFootprint, pivotCell);
                candidate.distance = MeasureCandidateDistance(candidate);

                if (candidate.distance == float.MaxValue)
                    continue;

                if (candidate.distance > snapDistanceThreshold)
                    continue;

                // Use the fast logical check while scanning. Calling Physics.OverlapBox
                // for every socket/pivot candidate makes large grids laggy.
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

        Vector3 localHeld = grid.transform.InverseTransformPoint(transform.position);

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
        if (transform.position.y < socket.transform.position.y - allowedBelowSocket)
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

        if (transform.position.y < socket.transform.position.y - allowedBelowSocket)
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

        float releaseDistance = snapDistanceThreshold * releaseDistanceMultiplier;
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
        // Height based placement fix:
        // The old surface targeting picked exactly one raycast surface, usually the highest hit.
        // That made forward/floor sockets unreachable when any nearby block top was hit by a ray.
        // Now we scan all sockets and let MeasureCandidateDistance choose the correct level from
        // the held block height. Nearby socket pruning still keeps this fast on large grids.
        if (useHeldHeightForLevelSelection)
            return GetAllSocketsCached();

        if (!useSurfaceRaycastTargeting)
            return GetAllSocketsCached();

        LegoBlockSocketSpawner targetGrid = FindTargetSurfaceGrid();

        if (targetGrid == null)
        {
            if (debugSurfaceTargeting)
                Debug.Log("[LEGO SURFACE TARGET] " + gameObject.name + " no target grid found, falling back to all sockets.");

            return GetAllSocketsCached();
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

        return targetGrid.GetComponentsInChildren<LegoSocket>(true);
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

        origins.Add(transform.position + up);

        int yawStep = GetYawStep(CurrentYawOffset);
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

                origins.Add(transform.position + transform.rotation * local + up);
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
    /// </summary>
    private void ApplyHeldRootRotation()
    {
        Quaternion targetRotation =
            heldBaseRotation *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        if (rb != null)
        {
            rb.angularVelocity = Vector3.zero;
            rb.MoveRotation(targetRotation);
        }

        transform.rotation = targetRotation;

        ResetVisualRoot();
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
    }

    // -------------------------------------------------------------------------
    // Socket Occupation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Releases all sockets currently occupied by this block and notifies the parent block.
    /// </summary>
    private void ReleaseCurrentOccupiedSocketsAndNotifyParent()
    {
        LegoBlock parentBlock = null;

        if (currentSocket != null)
        {
            parentBlock = currentSocket.GetComponentInParent<LegoBlock>();
        }
        else if (currentOccupiedSockets.Count > 0 && currentOccupiedSockets[0] != null)
        {
            parentBlock = currentOccupiedSockets[0].GetComponentInParent<LegoBlock>();
        }

        ReleaseCurrentOccupiedSockets();

        if (parentBlock != null)
            parentBlock.RemoveAttachedBlockAbove();
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

    private void OnDrawGizmos()
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

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.06f);
    }
}
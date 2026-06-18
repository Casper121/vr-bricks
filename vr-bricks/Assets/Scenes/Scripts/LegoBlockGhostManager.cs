using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using System.Collections.Generic;

/// <summary>
/// Handles LEGO block snapping, ghost preview, placement validation,
/// held-block rotation, and socket occupation.
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
    [SerializeField] private float snapDistanceThreshold = 0.6f;

    [Tooltip("Maximum allowed X/Z axis offset from the target socket.")]
    [SerializeField] private float maxAxisOffset = 0.8f;

    [Tooltip("Multiplier used to keep an already locked socket reachable slightly longer.")]
    [SerializeField] private float releaseDistanceMultiplier = 1.1f;

    [Tooltip("How much better a new candidate must be before the ghost switches to it. Higher = less flicker, lower = more responsive.")]
    [SerializeField] private float candidateSwitchMargin = 0.08f;

    [Tooltip("How far below a socket the held block center may be while the socket is still considered reachable.")]
    [SerializeField] private float allowedBelowSocket = 0.18f;

    [Tooltip("After rotating, keep the current socket/orbit locked until the hand moves this far. Higher values make rotation independent from tiny hand-position differences.")]
    [SerializeField] private float rotationAnchorUnlockDistance = 0.35f;

    [Tooltip("Ghost color for valid placement.")]
    [SerializeField] private Color ghostColorValid = new Color(0.3f, 0.6f, 1f, 0.4f);

    [Tooltip("Ghost color for invalid placement.")]
    [SerializeField] private Color ghostColorInvalid = new Color(1f, 0.2f, 0.2f, 0.4f);

    // -------------------------------------------------------------------------
    // Inspector: Grid Placement
    // -------------------------------------------------------------------------

    [Header("Grid Placement")]
    [Tooltip("If enabled, blocks cannot be freely dropped. They must snap to a valid LEGO socket/grid position.")]
    [SerializeField] private bool requireGridPlacement = true;

    [Tooltip("If enabled, a block returns to its position before grabbing when released without a valid grid snap.")]
    [SerializeField] private bool returnToLastPositionWhenInvalid = true;

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
    private bool rotationLockGraceFrame;
    private float lastBuiltYaw = -999f;

    // Used when grid placement is required: invalid releases return here
    // instead of falling freely onto the floor/table.
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;
    private bool hasLastValidPose;

    private bool hasActiveCandidate;
    private SnapCandidate activeCandidate;

    // The physical underside stud that is currently attached to TargetSocket.
    // This is stored in the block's original, unrotated local grid coordinates.
    // When O is pressed, this same stud stays attached to the same socket while
    // the block rotates around it. This prevents the every-frame search from
    // choosing a different anchor just because the hand did not move.
    private bool hasLockedPhysicalAnchor;
    private int lockedAnchorLocalX;
    private int lockedAnchorLocalZ;
    private bool rotationAnchorLockActive;
    private Vector3 rotationAnchorLockHandPosition;

    // Rotation is queued and applied from HandleHeldState only.
    // This prevents a short wrong/opposite ghost flash caused by rebuilding
    // once from the input callback and again from the normal every-frame search.
    private int pendingRotationSteps;
    private int lastRotationRequestFrame = -999;

    // Visible orbit step is independent from CurrentYawOffset.
    // This fixes the "pos 1 -> pos 2 -> pos 1 -> pos 2 -> pos 3 -> pos 4" bug.
    // CurrentYawOffset controls the mesh yaw. visibleOrbitStep controls which side
    // of the target socket the block is placed on. Both advance by one on O,
    // but visibleOrbitStep starts from the candidate that is already shown.
    private bool hasVisibleOrbitStep;
    private int visibleOrbitStep;

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
    }

    private struct SnapCandidate
    {
        public LegoSocket socket;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public float distance;
        public int startX;
        public int startZ;
        public int effectiveWidth;
        public int effectiveLength;
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
    /// Rotates the preview around the same physical underside stud and the same
    /// target socket. This is the key difference from the normal snap search:
    /// pressing O must not choose a new nearest anchor. It must only advance the
    /// yaw by exactly 90 degrees and make that visible at the current socket.
    /// </summary>
    private void RotateAroundLockedPhysicalAnchor()
    {
        ApplyHeldRootRotation();

        LegoSocket socketToKeep = TargetSocket;

        if (socketToKeep == null && hasActiveCandidate && activeCandidate.socket != null)
            socketToKeep = activeCandidate.socket;

        if (socketToKeep == null)
        {
            RebuildGhostIfLocked();
            return;
        }

        // Important:
        // Do NOT keep the same physical underside stud here.
        // Keeping the same middle stud on a 1x3 (or similar block) can only show
        // two visible positions because 0/180 and 90/270 overlap visually.
        // Instead, O cycles the block around the same socket in four visible
        // orbit positions: +Z, +X, -Z, -X in the socket grid.
        if (!hasVisibleOrbitStep && hasActiveCandidate && activeCandidate.socket != null)
            LockVisibleOrbitStepFromCandidate(activeCandidate);

        SnapCandidate rotatedCandidate = FindFourWayOrbitCandidateForSocket(socketToKeep, visibleOrbitStep);

        if (rotatedCandidate.socket == null || !IsCandidateValidForPreview(rotatedCandidate))
        {
            RebuildGhostIfLocked();
            return;
        }

        activeCandidate = rotatedCandidate;
        hasActiveCandidate = true;
        TargetSocket = rotatedCandidate.socket;

        rotationAnchorLockActive = true;
        rotationAnchorLockHandPosition = transform.position;

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
        rotationLockGraceFrame = false;
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

        // Important fix:
        // Do not stay hard-locked to one socket. Every frame we evaluate all
        // possible sockets and all possible underside anchor positions of the
        // held block. This makes 1x1 top sockets and middle studs of long
        // blocks much easier to hit.
        UpdateBestSnapCandidate();

        wasHeld = true;
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

        // Initialize the visible orbit from the position that is currently shown.
        // Without this, the orbit direction is tied to absolute yaw and can jump
        // back to an earlier visible position at the beginning of rotation.
        if (!hasVisibleOrbitStep && hasActiveCandidate && activeCandidate.socket != null)
            LockVisibleOrbitStepFromCandidate(activeCandidate);

        if (!hasVisibleOrbitStep)
            visibleOrbitStep = GetYawStep(CurrentYawOffset);

        visibleOrbitStep = WrapStep(visibleOrbitStep + step);
        hasVisibleOrbitStep = true;

        CurrentYawOffset = (CurrentYawOffset + step * 90f) % 360f;

        if (CurrentYawOffset < 0f)
            CurrentYawOffset += 360f;

        rotationLockGraceFrame = true;

        RotateAroundLockedPhysicalAnchor();
    }

    /// <summary>
    /// Initializes physics and socket state when the block starts being held.
    /// </summary>
    private void BeginHold()
    {
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;
        hasLastValidPose = true;

        pendingRotationSteps = 0;
        hasVisibleOrbitStep = false;
        visibleOrbitStep = 0;
        CurrentYawOffset = 0f;

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
        // After pressing O, keep the same socket/orbit candidate active until
        // the user actually moves the hand away. Without this, the every-frame
        // search may choose a different anchor based on hand position, making
        // rotation work only from certain holding positions.
        if (rotationAnchorLockActive && TargetSocket != null && hasActiveCandidate && activeCandidate.socket != null)
        {
            float handMoveDistance = Vector3.Distance(transform.position, rotationAnchorLockHandPosition);

            if (handMoveDistance <= rotationAnchorUnlockDistance)
            {
                // Keep the exact orbit candidate chosen by the O-press.
                // Do not re-search by hand distance while the hand has not moved,
                // otherwise the system falls back into the old two-position behavior.
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
            ClearTargetAndGhost();
            lastBuiltYaw = -999f;
            return;
        }

        // Ungültiger Kandidat: roten Ghost zeigen aber nicht einrasten
        if (!IsCandidateValidForPreview(bestCandidate))
        {
            ClearTemporaryStabilization();
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
        LockPhysicalAnchorFromCandidate(bestCandidate);
        LockVisibleOrbitStepFromCandidate(bestCandidate);

        RebuildGhostForCandidate(bestCandidate);
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
        {
            TrySnapToCandidate(activeCandidate);
        }
        else if (TargetSocket != null && currentPlacementValid)
        {
            TrySnapToTarget();
        }
        else
        {
            if (requireGridPlacement)
                RejectInvalidGridPlacement();
            else
                FallbackRelease();
        }

        DisableAllSocketInteractors(false);
        ClearTemporaryStabilization();

        TargetSocket = null;
        currentPlacementValid = false;
        pendingRotationSteps = 0;
        hasActiveCandidate = false;
        activeCandidate = new SnapCandidate();
        hasLockedPhysicalAnchor = false;
        hasVisibleOrbitStep = false;
        visibleOrbitStep = 0;
        rotationAnchorLockActive = false;
        lastBuiltYaw = -999f;
        rotationLockGraceFrame = false;
    }

    /// <summary>
    /// Attempts to snap the block to the exact candidate that was previewed.
    /// This preserves which underside stud of the held block was chosen.
    /// </summary>
    private void TrySnapToCandidate(SnapCandidate finalCandidate)
    {
        bool canPlace =
            finalCandidate.socket != null &&
            finalCandidate.parentGrid != null &&
            finalCandidate.parentGrid.IsAreaClear(
                finalCandidate.startX,
                finalCandidate.startZ,
                finalCandidate.effectiveWidth,
                finalCandidate.effectiveLength
            );

        if (!canPlace)
        {
            if (requireGridPlacement)
                RejectInvalidGridPlacement();
            else
                FallbackRelease();

            return;
        }

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;

        CurrentYawOffset = 0f;
        heldBaseRotation = transform.rotation;

        currentSocket = finalCandidate.socket;

        currentOccupiedSockets.Clear();
        currentOccupiedSockets.AddRange(
            finalCandidate.parentGrid.GetSocketsInArea(
                finalCandidate.startX,
                finalCandidate.startZ,
                finalCandidate.effectiveWidth,
                finalCandidate.effectiveLength
            )
        );

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

        bool canPlace =
            finalCandidate.socket != null &&
            finalCandidate.parentGrid != null &&
            finalCandidate.parentGrid.IsAreaClear(
                finalCandidate.startX,
                finalCandidate.startZ,
                finalCandidate.effectiveWidth,
                finalCandidate.effectiveLength
            );

        if (!canPlace)
        {
            if (requireGridPlacement)
                RejectInvalidGridPlacement();
            else
                FallbackRelease();

            return;
        }

        ResetVisualRoot();

        transform.position = finalCandidate.worldPosition;
        transform.rotation = finalCandidate.worldRotation;

        CurrentYawOffset = 0f;
        heldBaseRotation = transform.rotation;

        currentSocket = finalCandidate.socket;

        currentOccupiedSockets.Clear();
        currentOccupiedSockets.AddRange(
            finalCandidate.parentGrid.GetSocketsInArea(
                finalCandidate.startX,
                finalCandidate.startZ,
                finalCandidate.effectiveWidth,
                finalCandidate.effectiveLength
            )
        );

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
    /// Rejects a release that is not on a valid LEGO grid position.
    /// The block does not fall freely onto the floor/table.
    /// </summary>
    private void RejectInvalidGridPlacement()
    {
        HideGhost();
        ClearTemporaryStabilization();

        ResetVisualRoot();
        CurrentYawOffset = 0f;

        if (returnToLastPositionWhenInvalid && hasLastValidPose)
        {
            transform.position = lastValidPosition;
            transform.rotation = lastValidRotation;
        }

        heldBaseRotation = transform.rotation;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        // Mark the block stable again, because it has been returned instead of dropped.
        block.SetSnappedToSocket(hasLastValidPose);
    }

    /// <summary>
    /// Releases the block without snapping it to a socket.
    /// </summary>
    private void FallbackRelease()
    {
        HideGhost();

        block.SetSnappedToSocket(false);

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

        bool coversInnerSocket = socket.parentGrid.DoesRotatedBlockCoverInnerSocket(
            candidate.startX,
            candidate.startZ,
            block,
            CurrentYawOffset
        );

        bool areaClear = candidate.parentGrid.IsAreaClear(
            candidate.startX,
            candidate.startZ,
            candidate.effectiveWidth,
            candidate.effectiveLength
        );

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
    /// Creates a transparent ghost mesh at the given position and rotation.
    /// </summary>
    private void BuildGhostAtPosition(Vector3 position, Quaternion rotation, bool isValid)
    {
        HideGhost();

        if (meshSnapshots.Count == 0)
            return;

        Material ghostMaterial = CreateGhostMaterial(isValid);

        ghostRoot = new GameObject("SnapGhost");
        ghostRoot.transform.SetPositionAndRotation(position, rotation);

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
            meshRenderer.material = ghostMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }
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
    /// Calculates the final block position and rotation for a locked socket.
    /// </summary>
    private SnapCandidate ComputeCandidateForLockedSocket(LegoSocket socket)
    {
        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, CurrentYawOffset);

        int step = Mathf.RoundToInt(CurrentYawOffset / 90f) % 4;

        if (step < 0)
            step += 4;

        int startX;
        int startZ;

        switch (step)
        {
            case 1:
                startX = socket.gridX;
                startZ = socket.gridZ - (effectiveLength - 1);
                break;

            case 2:
                startX = socket.gridX - (effectiveWidth - 1);
                startZ = socket.gridZ - (effectiveLength - 1);
                break;

            case 3:
                startX = socket.gridX - (effectiveWidth - 1);
                startZ = socket.gridZ;
                break;

            default:
                startX = socket.gridX;
                startZ = socket.gridZ;
                break;
        }

        Vector3 worldPosition = socket.parentGrid.GetBlockCenterWorldPositionRotated(
            startX,
            startZ,
            block,
            CurrentYawOffset
        );

        Quaternion worldRotation =
            socket.GetBaseRotation() *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        return new SnapCandidate
        {
            socket = socket,
            worldPosition = worldPosition,
            worldRotation = worldRotation,
            startX = startX,
            startZ = startZ,
            effectiveWidth = effectiveWidth,
            effectiveLength = effectiveLength,
            parentGrid = socket.parentGrid
        };
    }

    /// <summary>
    /// Recalculates world position, rotation, dimensions, and distance for a stored candidate.
    /// </summary>
    private SnapCandidate RefreshCandidate(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, CurrentYawOffset);

        candidate.effectiveWidth = effectiveWidth;
        candidate.effectiveLength = effectiveLength;
        candidate.worldPosition = candidate.parentGrid.GetBlockCenterWorldPositionRotated(
            candidate.startX,
            candidate.startZ,
            block,
            CurrentYawOffset
        );
        candidate.worldRotation =
            candidate.socket.GetBaseRotation() *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);
        candidate.distance = MeasureCandidateDistance(candidate);

        return candidate;
    }

    /// <summary>
    /// Chooses one of the possible anchor positions on the same socket so that
    /// repeated O presses produce four visible positions around that socket.
    ///
    /// This intentionally does not choose the candidate closest to the hand.
    /// It chooses the candidate whose block center is furthest in the desired
    /// orbit direction for the current yaw step:
    /// 0 = +Z, 1 = +X, 2 = -Z, 3 = -X in the socket grid.
    /// </summary>
    private SnapCandidate FindFourWayOrbitCandidateForSocket(LegoSocket socket, int orbitStep)
    {
        if (socket == null || socket.parentGrid == null)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, CurrentYawOffset);

        Vector2 desiredDirection = GetOrbitDirectionForStep(orbitStep);

        SnapCandidate best = new SnapCandidate
        {
            socket = null,
            distance = float.MaxValue
        };

        float bestScore = float.NegativeInfinity;

        for (int dx = 0; dx < effectiveWidth; dx++)
        {
            for (int dz = 0; dz < effectiveLength; dz++)
            {
                int startX = socket.gridX - dx;
                int startZ = socket.gridZ - dz;

                SnapCandidate candidate = new SnapCandidate
                {
                    socket = socket,
                    startX = startX,
                    startZ = startZ,
                    effectiveWidth = effectiveWidth,
                    effectiveLength = effectiveLength,
                    parentGrid = socket.parentGrid,
                    worldPosition = socket.parentGrid.GetBlockCenterWorldPositionRotated(
                        startX,
                        startZ,
                        block,
                        CurrentYawOffset
                    ),
                    worldRotation =
                        socket.GetBaseRotation() *
                        Quaternion.Euler(0f, CurrentYawOffset, 0f)
                };

                // Important:
                // During O-rotation we must NOT judge the orbit candidate by the
                // current hand position. The user may be holding the block above
                // the socket from any side. If we call MeasureCandidateDistance()
                // here, some of the four orbit positions get rejected or scored
                // badly depending on hand position, which recreates the old
                // "only works from one holding position" behavior.
                candidate.distance = 0f;

                if (!IsCandidateValidForPreview(candidate))
                    continue;

                Vector3 worldOffset = candidate.worldPosition - socket.transform.position;
                Vector3 localOffset = socket.parentGrid.transform.InverseTransformVector(worldOffset);
                Vector2 localOffset2D = new Vector2(localOffset.x, localOffset.z);

                // Prefer the candidate that lies furthest in the current orbit direction.
                // No hand-distance tie-breaker here: O should cycle visible positions,
                // not choose the position nearest to the hand.
                float score = Vector2.Dot(localOffset2D, desiredDirection);

                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the desired visible orbit direction for a visible orbit step.
    /// This is intentionally independent from CurrentYawOffset.
    /// CurrentYawOffset controls mesh rotation; visibleOrbitStep controls which
    /// side of the socket the block occupies.
    /// </summary>
    private Vector2 GetOrbitDirectionForStep(int step)
    {
        step = WrapStep(step);

        switch (step)
        {
            case 1:
                return Vector2.right;
            case 2:
                return Vector2.down;
            case 3:
                return Vector2.left;
            default:
                return Vector2.up;
        }
    }

    /// <summary>
    /// Stores which visible side of the target socket the current candidate uses.
    /// On the next O press, rotation advances from this exact visible position,
    /// instead of being guessed from absolute yaw.
    /// </summary>
    private void LockVisibleOrbitStepFromCandidate(SnapCandidate candidate)
    {
        if (candidate.socket == null || candidate.parentGrid == null)
            return;

        Vector3 worldOffset = candidate.worldPosition - candidate.socket.transform.position;
        Vector3 localOffset = candidate.parentGrid.transform.InverseTransformVector(worldOffset);
        Vector2 offset = new Vector2(localOffset.x, localOffset.z);

        Vector2[] directions = new Vector2[]
        {
            Vector2.up,
            Vector2.right,
            Vector2.down,
            Vector2.left
        };

        int bestStep = 0;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < directions.Length; i++)
        {
            float score = Vector2.Dot(offset, directions[i]);

            if (score > bestScore)
            {
                bestScore = score;
                bestStep = i;
            }
        }

        visibleOrbitStep = bestStep;
        hasVisibleOrbitStep = true;
    }

    private int WrapStep(int step)
    {
        step %= 4;

        if (step < 0)
            step += 4;

        return step;
    }

    /// <summary>
    /// Stores which physical underside stud of the held block is currently being
    /// placed on the target socket. The stored coordinates are in the block's
    /// original width/length grid, not the currently rotated effective grid.
    /// </summary>
    private void LockPhysicalAnchorFromCandidate(SnapCandidate candidate)
    {
        if (candidate.socket == null)
            return;

        int anchorEffectiveX = candidate.socket.gridX - candidate.startX;
        int anchorEffectiveZ = candidate.socket.gridZ - candidate.startZ;

        Vector2Int originalAnchor = EffectiveAnchorToOriginalAnchor(
            anchorEffectiveX,
            anchorEffectiveZ,
            CurrentYawOffset
        );

        lockedAnchorLocalX = Mathf.Clamp(originalAnchor.x, 0, block.width - 1);
        lockedAnchorLocalZ = Mathf.Clamp(originalAnchor.y, 0, block.length - 1);
        hasLockedPhysicalAnchor = true;
    }

    /// <summary>
    /// Builds a candidate where the stored physical underside stud remains on the
    /// given socket after the current yaw rotation.
    /// </summary>
    private SnapCandidate BuildCandidateFromLockedPhysicalAnchor(LegoSocket socket)
    {
        if (socket == null || socket.parentGrid == null || !hasLockedPhysicalAnchor)
            return new SnapCandidate { socket = null, distance = float.MaxValue };

        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, CurrentYawOffset);

        Vector2Int effectiveAnchor = OriginalAnchorToEffectiveAnchor(
            lockedAnchorLocalX,
            lockedAnchorLocalZ,
            CurrentYawOffset
        );

        effectiveAnchor.x = Mathf.Clamp(effectiveAnchor.x, 0, effectiveWidth - 1);
        effectiveAnchor.y = Mathf.Clamp(effectiveAnchor.y, 0, effectiveLength - 1);

        int startX = socket.gridX - effectiveAnchor.x;
        int startZ = socket.gridZ - effectiveAnchor.y;

        Vector3 worldPosition = socket.parentGrid.GetBlockCenterWorldPositionRotated(
            startX,
            startZ,
            block,
            CurrentYawOffset
        );

        Quaternion worldRotation =
            socket.GetBaseRotation() *
            Quaternion.Euler(0f, CurrentYawOffset, 0f);

        SnapCandidate candidate = new SnapCandidate
        {
            socket = socket,
            worldPosition = worldPosition,
            worldRotation = worldRotation,
            startX = startX,
            startZ = startZ,
            effectiveWidth = effectiveWidth,
            effectiveLength = effectiveLength,
            parentGrid = socket.parentGrid
        };

        candidate.distance = MeasureCandidateDistance(candidate);
        return candidate;
    }

    /// <summary>
    /// Converts a stored original block stud coordinate into the currently
    /// rotated effective grid coordinate.
    /// </summary>
    private Vector2Int OriginalAnchorToEffectiveAnchor(int originalX, int originalZ, float yawOffset)
    {
        int step = GetYawStep(yawOffset);
        int width = block.width;
        int length = block.length;

        switch (step)
        {
            case 1:
                return new Vector2Int(originalZ, width - 1 - originalX);

            case 2:
                return new Vector2Int(width - 1 - originalX, length - 1 - originalZ);

            case 3:
                return new Vector2Int(length - 1 - originalZ, originalX);

            default:
                return new Vector2Int(originalX, originalZ);
        }
    }

    /// <summary>
    /// Converts a rotated effective grid coordinate back to the block's original
    /// local stud coordinate.
    /// </summary>
    private Vector2Int EffectiveAnchorToOriginalAnchor(int effectiveX, int effectiveZ, float yawOffset)
    {
        int step = GetYawStep(yawOffset);
        int width = block.width;
        int length = block.length;

        switch (step)
        {
            case 1:
                return new Vector2Int(width - 1 - effectiveZ, effectiveX);

            case 2:
                return new Vector2Int(width - 1 - effectiveX, length - 1 - effectiveZ);

            case 3:
                return new Vector2Int(effectiveZ, length - 1 - effectiveX);

            default:
                return new Vector2Int(effectiveX, effectiveZ);
        }
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
        if (candidate.socket == null || candidate.parentGrid == null)
            return false;

        if (!IsSocketUsableForInitialSearch(candidate.socket))
            return false;

        bool coversInnerSocket = candidate.parentGrid.DoesRotatedBlockCoverInnerSocket(
            candidate.startX,
            candidate.startZ,
            block,
            CurrentYawOffset
        );

        if (!coversInnerSocket)
            return false;

        return candidate.parentGrid.IsAreaClear(
            candidate.startX,
            candidate.startZ,
            candidate.effectiveWidth,
            candidate.effectiveLength
        );
    }

    /// <summary>
    /// Measures how close the held block is to a candidate placement.
    /// The comparison is done in the local space of the target grid, so rotated
    /// blocks and high stacks behave more consistently.
    /// </summary>
    private float MeasureCandidateDistance(SnapCandidate candidate)
    {
        if (candidate.parentGrid == null)
            return float.MaxValue;

        Vector3 diff = transform.position - candidate.worldPosition;
        Vector3 localDiff = candidate.parentGrid.transform.InverseTransformVector(diff);

        if (Mathf.Abs(localDiff.x) > maxAxisOffset || Mathf.Abs(localDiff.z) > maxAxisOffset)
            return float.MaxValue;

        return new Vector2(localDiff.x, localDiff.z).magnitude;
    }

    /// <summary>
    /// Finds the best nearby socket candidate for the currently held block.
    /// Returns valid candidates first; falls back to the nearest invalid candidate
    /// so a red ghost can still be shown when placement is blocked.
    /// </summary>
    private SnapCandidate FindInitialSnapCandidate()
    {
        (int effectiveWidth, int effectiveLength) = GetRotatedDimensions(block, CurrentYawOffset);

        LegoSocket[] allSockets = FindObjectsOfType<LegoSocket>();

        SnapCandidate bestValid = new SnapCandidate { socket = null, distance = float.MaxValue };
        SnapCandidate bestInvalid = new SnapCandidate { socket = null, distance = float.MaxValue };

        foreach (LegoSocket socket in allSockets)
        {
            if (!IsSocketUsableForInvalidSearch(socket))
                continue;

            // Try every possible underside stud/anchor of the held block.
            // This is the important part for 1x1 blocks and middle studs:
            // A 1x4 can now place its left, middle, or right underside stud
            // onto the same target socket.
            for (int dx = 0; dx < effectiveWidth; dx++)
            {
                for (int dz = 0; dz < effectiveLength; dz++)
                {
                    int startX = socket.gridX - dx;
                    int startZ = socket.gridZ - dz;

                    SnapCandidate candidate = new SnapCandidate
                    {
                        socket = socket,
                        startX = startX,
                        startZ = startZ,
                        effectiveWidth = effectiveWidth,
                        effectiveLength = effectiveLength,
                        parentGrid = socket.parentGrid,
                        worldPosition = socket.parentGrid.GetBlockCenterWorldPositionRotated(
                            startX,
                            startZ,
                            block,
                            CurrentYawOffset
                        ),
                        worldRotation =
                            socket.GetBaseRotation() *
                            Quaternion.Euler(0f, CurrentYawOffset, 0f)
                    };

                    candidate.distance = MeasureCandidateDistance(candidate);

                    if (candidate.distance == float.MaxValue)
                        continue;

                    if (candidate.distance > snapDistanceThreshold)
                        continue;

                    if (IsCandidateValidForPreview(candidate))
                    {
                        if (candidate.distance < bestValid.distance)
                            bestValid = candidate;
                    }
                    else
                    {
                        if (candidate.distance < bestInvalid.distance)
                            bestInvalid = candidate;
                    }
                }
            }
        }

        // Gültiger Kandidat hat immer Vorrang vor ungültigem
        if (bestValid.socket != null)
            return bestValid;

        return bestInvalid;
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

        if (transform.position.y < socket.transform.position.y - allowedBelowSocket)
            return false;

        return true;
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

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
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

        if (visualRoot.parent != transform)
            visualRoot.SetParent(transform, false);

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
        hasLockedPhysicalAnchor = false;
        hasVisibleOrbitStep = false;
        visibleOrbitStep = 0;
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
        MeshFilter[] meshFilters = meshRoot.GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
                continue;

            meshSnapshots.Add(new MeshSnapshot
            {
                mesh = meshFilter.sharedMesh,
                localPosition = transform.InverseTransformPoint(meshFilter.transform.position),
                localRotation = Quaternion.Inverse(transform.rotation) * meshFilter.transform.rotation,
                localScale = meshFilter.transform.lossyScale
            });
        }
    }

    /// <summary>
    /// Returns effective width and length after applying a 90-degree yaw rotation.
    /// </summary>
    private (int width, int length) GetRotatedDimensions(LegoBlock legoBlock, float yawOffset)
    {
        bool rotatedByQuarterTurn =
            Mathf.Approximately(Mathf.Abs(yawOffset) % 180f, 90f);

        if (rotatedByQuarterTurn)
            return (legoBlock.length, legoBlock.width);

        return (legoBlock.width, legoBlock.length);
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
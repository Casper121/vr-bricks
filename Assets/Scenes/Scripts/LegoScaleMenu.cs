using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global LEGO scale menu:
/// - No menu lag from Auto Update
/// - Optional live scaling while dragging
/// - Floor grid height stays fixed
/// - Connected stacked blocks keep their relative distance when scaling
///
/// PERFORMANCE UPDATE:
/// Floor socket spawners are now only fully (re)built ONCE, the first time they are
/// encountered, sized generously enough (using minScale) so the grid never needs to
/// shrink again. Every subsequent scale change only repositions the EXISTING socket
/// transforms (spawner.RepositionSockets()) instead of destroying and recreating
/// hundreds of GameObjects every frame while dragging the slider. This also fixes
/// snapped blocks losing their socket reference / jumping around after a second scale
/// change, since sockets now keep a stable identity for the lifetime of the scene.
///
/// IMPORTANT:
/// This version expects LegoBlock.GetWorldHeight() to return the UNSCALED logical height:
///
/// public float GetWorldHeight()
/// {
///     return height;
/// }
///
/// Put this script on an always-active object, for example MenuController.
/// Do NOT put it on SettingsMenu.
/// </summary>
public class LegoScaleMenu : MonoBehaviour
{
    public static float CurrentScale { get; private set; } = 1f;

    [Header("UI")]
    [SerializeField] private Slider scaleSlider;
    [SerializeField] private TMP_Text scaleLabel;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;

    [Header("Scale Values")]
    [SerializeField] private float minScale = 0.2f;
    [SerializeField] private float maxScale = 2.0f;
    [SerializeField] private float defaultScale = 1.0f;

    [Header("Floor Height")]
    [SerializeField] private float lockedFloorGridY = -0.4f;

    [Tooltip("Keep true. Prevents floor grid from moving vertically.")]
    [SerializeField] private bool forceFloorGridY = true;

    [Tooltip("Keep true. Prevents floor transform scaling from changing snap height.")]
    [SerializeField] private bool forceFloorGridScaleOne = true;

    [Tooltip("Optional. Keeps the floor grid world X/Z origin fixed while scaling spacing. Usually true is safest for preventing snap-point drift.")]
    [SerializeField] private bool lockFloorGridXZOrigin = true;

    [Header("Connected Blocks")]
    [Tooltip("Keeps stacked/connected blocks together vertically when global scale changes.")]
    [SerializeField] private bool keepConnectedBlocksTogether = true;

    [Header("Multiple Builds")]
    [Tooltip("Optional. Assign your XR Origin / camera / player root here. When multiple separate builds exist in the same room, scaling anchors to whichever build is CLOSEST to this transform, so the build you're currently working on stays visually stable instead of drifting toward a point between unrelated builds. Leave empty to fall back to averaging all snapped blocks together.")]
    [SerializeField] private Transform playerReference;

    [Header("Live Preview")]
    [Tooltip("If true, scaling happens immediately while moving the slider.")]
    [SerializeField] private bool liveScaleWhileDragging = true;

    [Tooltip("Forces live preview even if an old serialized inspector value disabled Live Scale While Dragging.")]
    [SerializeField] private bool forceLivePreview = true;

    [Tooltip("Small throttle for live scaling so it does not run every tiny slider change.")]
    [SerializeField] private float liveApplyInterval = 0.02f;

    private float lastLiveApplyTime = -999f;
    private float lastAppliedScale = 1f;

    private readonly Dictionary<LegoBlockSocketSpawner, SpawnerBaseData> baseSpawnerData =
        new Dictionary<LegoBlockSocketSpawner, SpawnerBaseData>();

    // Tracks, per floor spawner, the SMALLEST scale it has ever been structurally built
    // for (i.e. the highest socket density it currently supports). A full rebuild only
    // happens when scaling below this value - never preemptively for the whole range.

    private struct SpawnerBaseData
    {
        public float socketY;
        public float offsetX;
        public float offsetZ;
        public float studSpacingX;
        public float studSpacingZ;
        public Vector3 worldPosition;

        // Fixed local-space center point of the grid (at the original scale=1 setup).
        // Used to keep the grid centered in place while spacing changes, instead of
        // growing/shrinking from the index (0,0) corner.
        public float centerLocalX;
        public float centerLocalZ;
    }

    private struct SnappedBlockSnapshot
    {
        public LegoBlock block;
        public LegoBlockSocketSpawner grid;
        public LegoBlock supportBlock;
        public int anchorX;
        public int anchorZ;
        public int yawStep;
        public float oldY;
        public float oldDeltaYFromSupport;

        // Full local offset relative to the support block.
        // This prevents X/Z drift for stacked blocks when global scale changes.
        public bool hasSupportLocalPose;
        public Vector3 localPositionInSupport;
        public Quaternion localRotationInSupport;
    }

    private void Awake()
    {
        EnforceScaleLimits();

        CurrentScale = Mathf.Clamp(defaultScale, minScale, maxScale);
        lastAppliedScale = CurrentScale;

        if (scaleSlider != null)
        {
            scaleSlider.minValue = minScale;
            scaleSlider.maxValue = maxScale;
            scaleSlider.value = CurrentScale;
            scaleSlider.onValueChanged.AddListener(OnSliderChanged);
        }

        if (applyButton != null)
            applyButton.onClick.AddListener(ApplyScaleFromUI);

        if (resetButton != null)
            resetButton.onClick.AddListener(ResetScale);

        UpdateLabel(CurrentScale);
    }

    private IEnumerator Start()
    {
        // IMPORTANT: Wait until every floor spawner reports IsGridReady == true. Spawners
        // now build their worst-case grid once, spread over several frames (to avoid a
        // startup freeze with thousands of sockets) - caching base data before that
        // finishes would lock in incomplete/incorrect blockWidth values.
        yield return null;

        while (!AllFloorSpawnersReady())
            yield return null;

        CacheFloorSpawnerBaseData();
        LockFloorGridHeightOnly();
    }

    private bool AllFloorSpawnersReady()
    {
        LegoBlockSocketSpawner[] spawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlockSocketSpawner spawner in spawners)
        {
            if (!IsFloorSpawner(spawner))
                continue;

            if (!spawner.IsGridReady)
                return false;
        }

        return true;
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        EnforceScaleLimits();
        UpdateSliderRangeOnly();
        UpdateLabel(Mathf.Clamp(CurrentScale, minScale, maxScale));
    }
#endif

    private void EnforceScaleLimits()
    {
        // NOTE: minScale/maxScale used to be hard-overridden to 0.2/2.0 here, ignoring
        // whatever was set in the Inspector. That silently allowed scaling further than
        // any LegoBlockSocketSpawner's "Worst Case Reference Scale" was built for,
        // causing uncovered grid edges at extreme scales. Now the Inspector values are
        // respected - just make sure every floor spawner's Worst Case Reference Scale is
        // set to (or below) this minScale.
        minScale = Mathf.Max(0.01f, minScale);
        maxScale = Mathf.Max(minScale, maxScale);
        defaultScale = Mathf.Clamp(defaultScale, minScale, maxScale);
    }

    private void UpdateSliderRangeOnly()
    {
        if (scaleSlider == null)
            return;

        scaleSlider.minValue = minScale;
        scaleSlider.maxValue = maxScale;
        scaleSlider.value = Mathf.Clamp(scaleSlider.value, minScale, maxScale);
    }

    private void OnDestroy()
    {
        if (scaleSlider != null)
            scaleSlider.onValueChanged.RemoveListener(OnSliderChanged);

        if (applyButton != null)
            applyButton.onClick.RemoveListener(ApplyScaleFromUI);

        if (resetButton != null)
            resetButton.onClick.RemoveListener(ResetScale);
    }

    private void OnSliderChanged(float value)
    {
        CurrentScale = Mathf.Clamp(value, minScale, maxScale);
        UpdateLabel(CurrentScale);

        if (!liveScaleWhileDragging && !forceLivePreview)
            return;

        if (Time.unscaledTime - lastLiveApplyTime < liveApplyInterval)
            return;

        lastLiveApplyTime = Time.unscaledTime;
        ApplyScale(CurrentScale);
    }

    private void ApplyScaleFromUI()
    {
        if (scaleSlider != null)
            CurrentScale = Mathf.Clamp(scaleSlider.value, minScale, maxScale);

        ApplyScale(CurrentScale);
    }

    private void ResetScale()
    {
        CurrentScale = Mathf.Clamp(defaultScale, minScale, maxScale);

        if (scaleSlider != null)
            scaleSlider.value = CurrentScale;

        ApplyScale(CurrentScale);
        UpdateLabel(CurrentScale);
    }

    public void ApplyScale(float scale)
    {
        EnforceScaleLimits();
        UpdateSliderRangeOnly();

        scale = Mathf.Clamp(scale, minScale, maxScale);

        float oldScale = Mathf.Max(0.0001f, lastAppliedScale);
        float scaleRatio = scale / oldScale;

        CurrentScale = scale;
        lastAppliedScale = scale;

        CacheFloorSpawnerBaseData();

        List<SnappedBlockSnapshot> snapshots = SnapshotSnappedBlocks();
        Dictionary<LegoBlockSocketSpawner, Vector2> buildAnchorPerSpawner = ComputeBuildAnchors(snapshots);

        ScaleFloorGridXZOnly(scale, buildAnchorPerSpawner);
        ScaleAllBlocks(scale);

        RestoreSnappedSocketReferences(snapshots);
        RebuildSocketOccupationFromSnapshots(snapshots);
        RepositionSnappedBlocksFromSnapshots(snapshots, scaleRatio);

        ClearAllGhosts();
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Computes, per floor spawner, the scaling anchor cell (anchorX, anchorZ) to use.
    /// 
    /// If a playerReference is assigned and multiple snapped floor-level blocks exist,
    /// this picks the anchor of whichever SINGLE block is closest to the player - so
    /// whichever build you are currently standing near/working on stays visually stable,
    /// instead of every separate build in the room being dragged toward a shared average
    /// point that might sit in empty space between them.
    /// 
    /// Falls back to averaging all snapped floor-level blocks together if no
    /// playerReference is assigned.
    /// 
    /// Blocks stacked on top of other blocks are excluded, since their anchor
    /// coordinates live in a different, block-local grid.
    /// </summary>
    private Dictionary<LegoBlockSocketSpawner, Vector2> ComputeBuildAnchors(List<SnappedBlockSnapshot> snapshots)
    {
        Dictionary<LegoBlockSocketSpawner, Vector2> result = new Dictionary<LegoBlockSocketSpawner, Vector2>();

        if (playerReference != null)
        {
            Dictionary<LegoBlockSocketSpawner, SnappedBlockSnapshot> nearest =
                new Dictionary<LegoBlockSocketSpawner, SnappedBlockSnapshot>();

            Dictionary<LegoBlockSocketSpawner, float> nearestDistanceSqr =
                new Dictionary<LegoBlockSocketSpawner, float>();

            foreach (SnappedBlockSnapshot snapshot in snapshots)
            {
                if (snapshot.grid == null || !IsFloorSpawner(snapshot.grid) || snapshot.block == null)
                    continue;

                float distanceSqr = (snapshot.block.transform.position - playerReference.position).sqrMagnitude;

                if (!nearestDistanceSqr.TryGetValue(snapshot.grid, out float bestSoFar) || distanceSqr < bestSoFar)
                {
                    nearestDistanceSqr[snapshot.grid] = distanceSqr;
                    nearest[snapshot.grid] = snapshot;
                }
            }

            foreach (KeyValuePair<LegoBlockSocketSpawner, SnappedBlockSnapshot> entry in nearest)
                result[entry.Key] = new Vector2(entry.Value.anchorX, entry.Value.anchorZ);

            return result;
        }

        // Fallback: no player reference assigned, average all snapped floor blocks together.
        Dictionary<LegoBlockSocketSpawner, Vector2> sums = new Dictionary<LegoBlockSocketSpawner, Vector2>();
        Dictionary<LegoBlockSocketSpawner, int> counts = new Dictionary<LegoBlockSocketSpawner, int>();

        foreach (SnappedBlockSnapshot snapshot in snapshots)
        {
            if (snapshot.grid == null || !IsFloorSpawner(snapshot.grid))
                continue;

            Vector2 cell = new Vector2(snapshot.anchorX, snapshot.anchorZ);

            if (!sums.ContainsKey(snapshot.grid))
            {
                sums[snapshot.grid] = Vector2.zero;
                counts[snapshot.grid] = 0;
            }

            sums[snapshot.grid] += cell;
            counts[snapshot.grid] = counts[snapshot.grid] + 1;
        }

        foreach (KeyValuePair<LegoBlockSocketSpawner, Vector2> entry in sums)
            result[entry.Key] = entry.Value / counts[entry.Key];

        return result;
    }

    private void CacheFloorSpawnerBaseData()
    {
        LegoBlockSocketSpawner[] spawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlockSocketSpawner spawner in spawners)
        {
            if (!IsFloorSpawner(spawner))
                continue;

            if (baseSpawnerData.ContainsKey(spawner))
                continue;

            // The spawner's own Start() already built the initial grid (using whatever
            // spacing/AutoFit settings are configured in the Inspector). We treat that
            // as the "scale = 1" reference point - no extra structural build needed here.
            float centerIndexX = (spawner.blockWidth - 1) * 0.5f;
            float centerIndexZ = (spawner.blockLength - 1) * 0.5f;

            baseSpawnerData[spawner] = new SpawnerBaseData
            {
                socketY = spawner.socketY,
                offsetX = spawner.offsetX,
                offsetZ = spawner.offsetZ,
                studSpacingX = spawner.studSpacingX,
                studSpacingZ = spawner.studSpacingZ,
                worldPosition = spawner.transform.position,
                centerLocalX = centerIndexX * spawner.studSpacingX + spawner.offsetX,
                centerLocalZ = centerIndexZ * spawner.studSpacingZ + spawner.offsetZ
            };
        }
    }

    private bool IsFloorSpawner(LegoBlockSocketSpawner spawner)
    {
        if (spawner == null)
            return false;

        // PERFORMANCE: skip spawners belonging to a currently inactive room (e.g. rooms
        // hidden by LegoRoomSwitcher). Without this, every scale change would redo work
        // for every room's sockets simultaneously, multiplying the cost by room count.
        if (!spawner.gameObject.activeInHierarchy)
            return false;

        LegoBlock ownerBlock = spawner.GetComponentInParent<LegoBlock>();
        return ownerBlock == null;
    }

    private void LockFloorGridHeightOnly()
    {
        LegoBlockSocketSpawner[] spawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlockSocketSpawner spawner in spawners)
        {
            if (!IsFloorSpawner(spawner))
                continue;

            if (forceFloorGridScaleOne)
                spawner.transform.localScale = Vector3.one;

            if (forceFloorGridY)
            {
                Vector3 position = spawner.transform.position;
                position.y = lockedFloorGridY;
                spawner.transform.position = position;
            }
        }
    }

    private void ScaleFloorGridXZOnly(float scale, Dictionary<LegoBlockSocketSpawner, Vector2> buildAnchorPerSpawner)
    {
        LegoBlockSocketSpawner[] spawners = FindObjectsByType<LegoBlockSocketSpawner>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlockSocketSpawner spawner in spawners)
        {
            if (!IsFloorSpawner(spawner))
                continue;

            if (!baseSpawnerData.TryGetValue(spawner, out SpawnerBaseData baseData))
                continue;

            if (forceFloorGridScaleOne)
                spawner.transform.localScale = Vector3.one;

            if (forceFloorGridY || lockFloorGridXZOrigin)
            {
                Vector3 position = spawner.transform.position;

                if (lockFloorGridXZOrigin)
                {
                    position.x = baseData.worldPosition.x;
                    position.z = baseData.worldPosition.z;
                }

                if (forceFloorGridY)
                    position.y = lockedFloorGridY;

                spawner.transform.position = position;
            }

            // If there is an actual build on this floor, remember its current local
            // position (using the SPACING/OFFSET AS THEY ARE RIGHT NOW, before we change
            // anything below) so we can keep that exact point fixed after rescaling -
            // this makes the build scale around ITSELF instead of the room's center.
            bool hasBuildAnchor = buildAnchorPerSpawner.TryGetValue(spawner, out Vector2 anchorCell);

            float anchorLocalX = 0f;
            float anchorLocalZ = 0f;

            if (hasBuildAnchor)
            {
                anchorLocalX = anchorCell.x * spawner.studSpacingX + spawner.offsetX;
                anchorLocalZ = anchorCell.y * spawner.studSpacingZ + spawner.offsetZ;
            }

            // Keep height unscaled.
            spawner.socketY = baseData.socketY;

            // Only horizontal grid spacing scales.
            spawner.studSpacingX = baseData.studSpacingX * scale;
            spawner.studSpacingZ = baseData.studSpacingZ * scale;

            // NOTE: No RespawnSockets() call here anymore. The spawner already built a
            // generously-sized grid ONCE at startup (worstCaseReferenceScale), so it never
            // needs to grow again - only cheap repositioning happens here, regardless of
            // how far the player scales blocks up or down.

            if (hasBuildAnchor)
            {
                // Scale around the build's own center: keep the same local point fixed.
                spawner.offsetX = anchorLocalX - anchorCell.x * spawner.studSpacingX;
                spawner.offsetZ = anchorLocalZ - anchorCell.y * spawner.studSpacingZ;
            }
            else
            {
                // No blocks placed yet on this floor - fall back to centering the whole
                // grid on the room's own fixed center point.
                float centerIndexX = (spawner.blockWidth - 1) * 0.5f;
                float centerIndexZ = (spawner.blockLength - 1) * 0.5f;

                spawner.offsetX = baseData.centerLocalX - centerIndexX * spawner.studSpacingX;
                spawner.offsetZ = baseData.centerLocalZ - centerIndexZ * spawner.studSpacingZ;
            }

            // PERFORMANCE: only move the already-built sockets, never destroy/recreate them
            // (unless the RespawnSockets() above just ran, in which case this simply
            // confirms the final centered positions).
            spawner.RepositionSockets();

            if (forceFloorGridScaleOne)
                spawner.transform.localScale = Vector3.one;

            if (forceFloorGridY || lockFloorGridXZOrigin)
            {
                Vector3 position = spawner.transform.position;

                if (lockFloorGridXZOrigin)
                {
                    position.x = baseData.worldPosition.x;
                    position.z = baseData.worldPosition.z;
                }

                if (forceFloorGridY)
                    position.y = lockedFloorGridY;

                spawner.transform.position = position;
            }
        }
    }

    private void ScaleAllBlocks(float scale)
    {
        LegoBlock[] blocks = FindObjectsByType<LegoBlock>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlock block in blocks)
        {
            if (block == null)
                continue;

            block.transform.localScale = Vector3.one * scale;

            Rigidbody rb = block.GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private List<SnappedBlockSnapshot> SnapshotSnappedBlocks()
    {
        List<SnappedBlockSnapshot> result = new List<SnappedBlockSnapshot>();

        LegoBlock[] blocks = FindObjectsByType<LegoBlock>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlock block in blocks)
        {
            if (block == null || block.SnappedSocket == null)
                continue;

            LegoSocket socket = block.SnappedSocket;
            LegoBlockSocketSpawner grid = socket.parentGrid;

            if (grid == null)
                grid = socket.GetComponentInParent<LegoBlockSocketSpawner>();

            if (grid == null)
                continue;

            LegoBlock supportBlock = null;

            if (!IsFloorSpawner(grid))
                supportBlock = grid.GetComponentInParent<LegoBlock>();

            float oldDeltaYFromSupport = 0f;
            bool hasSupportLocalPose = false;
            Vector3 localPositionInSupport = Vector3.zero;
            Quaternion localRotationInSupport = Quaternion.identity;

            if (supportBlock != null && supportBlock != block)
            {
                oldDeltaYFromSupport = block.transform.position.y - supportBlock.transform.position.y;

                // Store full local pose, not only Y.
                // X/Z socket positions inside scaled support blocks otherwise drift slightly.
                hasSupportLocalPose = true;
                localPositionInSupport = supportBlock.transform.InverseTransformPoint(block.transform.position);
                localRotationInSupport = Quaternion.Inverse(supportBlock.transform.rotation) * block.transform.rotation;
            }

            result.Add(new SnappedBlockSnapshot
            {
                block = block,
                grid = grid,
                supportBlock = supportBlock,
                anchorX = socket.gridX,
                anchorZ = socket.gridZ,
                yawStep = GetYawStepRelativeToSocket(block, socket),
                oldY = block.transform.position.y,
                oldDeltaYFromSupport = oldDeltaYFromSupport,
                hasSupportLocalPose = hasSupportLocalPose,
                localPositionInSupport = localPositionInSupport,
                localRotationInSupport = localRotationInSupport
            });
        }

        return result;
    }

    private void RestoreSnappedSocketReferences(List<SnappedBlockSnapshot> snapshots)
    {
        foreach (SnappedBlockSnapshot snapshot in snapshots)
        {
            if (snapshot.block == null || snapshot.grid == null)
                continue;

            LegoSocket socket = snapshot.grid.GetSocketAt(snapshot.anchorX, snapshot.anchorZ);

            if (socket == null)
                continue;

            snapshot.block.SnappedSocket = socket;
            snapshot.block.SetSnappedToSocket(true);
        }
    }

    private void RebuildSocketOccupationFromSnapshots(List<SnappedBlockSnapshot> snapshots)
    {
        foreach (SnappedBlockSnapshot snapshot in snapshots)
        {
            if (snapshot.block == null || snapshot.grid == null)
                continue;

            List<Vector2Int> rotatedFootprint = snapshot.block.GetRotatedFootprint(snapshot.yawStep);
            List<Vector2Int> absoluteCells = GetAbsoluteCells(snapshot.anchorX, snapshot.anchorZ, rotatedFootprint);

            snapshot.grid.SetSocketsOccupiedInFootprint(absoluteCells, true);
        }
    }

    private void RepositionSnappedBlocksFromSnapshots(List<SnappedBlockSnapshot> snapshots, float scaleRatio)
    {
        // Bottom/floor blocks first, upper blocks later.
        snapshots.Sort((a, b) =>
        {
            if (a.block == null && b.block == null) return 0;
            if (a.block == null) return -1;
            if (b.block == null) return 1;

            return a.oldY.CompareTo(b.oldY);
        });

        foreach (SnappedBlockSnapshot snapshot in snapshots)
        {
            if (snapshot.block == null || snapshot.grid == null)
                continue;

            LegoSocket socket = snapshot.grid.GetSocketAt(snapshot.anchorX, snapshot.anchorZ);

            if (socket == null)
                continue;

            List<Vector2Int> rotatedFootprint = snapshot.block.GetRotatedFootprint(snapshot.yawStep);
            List<Vector2Int> absoluteCells = GetAbsoluteCells(snapshot.anchorX, snapshot.anchorZ, rotatedFootprint);

            // Keep the original working snap formula for floor blocks.
            Vector3 correctedPosition = snapshot.grid.GetFootprintCenterWorldPosition(
                absoluteCells,
                snapshot.block.GetWorldHeight()
            );

            // Fix connected stacked blocks:
            // If this block is sitting on another block, restore its complete local pose
            // relative to the support block. This keeps X/Z snap positions stable while scaling.
            if (keepConnectedBlocksTogether &&
                snapshot.supportBlock != null &&
                snapshot.supportBlock != snapshot.block &&
                snapshot.hasSupportLocalPose)
            {
                correctedPosition =
                    snapshot.supportBlock.transform.TransformPoint(snapshot.localPositionInSupport);

                snapshot.block.transform.position = correctedPosition;
                snapshot.block.transform.rotation =
                    snapshot.supportBlock.transform.rotation * snapshot.localRotationInSupport;
            }
            else
            {
                snapshot.block.transform.position = correctedPosition;
                snapshot.block.transform.rotation =
                    socket.GetBaseRotation() * Quaternion.Euler(0f, snapshot.yawStep * 90f, 0f);
            }

            Rigidbody rb = snapshot.block.GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            snapshot.block.SnappedSocket = socket;
            snapshot.block.SetSnappedToSocket(true);
        }
    }

    private void ClearAllGhosts()
    {
        LegoBlockGhostManager[] ghostManagers = FindObjectsByType<LegoBlockGhostManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (LegoBlockGhostManager ghostManager in ghostManagers)
        {
            if (ghostManager != null)
                ghostManager.HideGhost();
        }
    }

    private int GetYawStepRelativeToSocket(LegoBlock block, LegoSocket socket)
    {
        if (block == null || socket == null)
            return 0;

        Quaternion relativeRotation =
            Quaternion.Inverse(socket.GetBaseRotation()) * block.transform.rotation;

        float yaw = relativeRotation.eulerAngles.y;
        int step = Mathf.RoundToInt(yaw / 90f) % 4;

        if (step < 0)
            step += 4;

        return step;
    }

    private List<Vector2Int> GetAbsoluteCells(int anchorX, int anchorZ, List<Vector2Int> rotatedFootprint)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (rotatedFootprint == null || rotatedFootprint.Count == 0)
        {
            result.Add(new Vector2Int(anchorX, anchorZ));
            return result;
        }

        foreach (Vector2Int cell in rotatedFootprint)
            result.Add(new Vector2Int(anchorX + cell.x, anchorZ + cell.y));

        return result;
    }

    private void UpdateLabel(float value)
    {
        if (scaleLabel != null)
            scaleLabel.text = $"{value:F1}x";
    }
}
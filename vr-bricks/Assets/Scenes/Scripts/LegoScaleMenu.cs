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

    private struct SpawnerBaseData
    {
        public float socketY;
        public float offsetX;
        public float offsetZ;
        public float studSpacingX;
        public float studSpacingZ;
        public Vector3 worldPosition;
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

        CacheFloorSpawnerBaseData();
        LockFloorGridHeightOnly();

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
        minScale = 0.2f;
        maxScale = 2.0f;
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

        ScaleFloorGridXZOnly(scale);
        ScaleAllBlocks(scale);

        RestoreSnappedSocketReferences(snapshots);
        RebuildSocketOccupationFromSnapshots(snapshots);
        RepositionSnappedBlocksFromSnapshots(snapshots, scaleRatio);

        ClearAllGhosts();
        Physics.SyncTransforms();
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

            baseSpawnerData[spawner] = new SpawnerBaseData
            {
                socketY = spawner.socketY,
                offsetX = spawner.offsetX,
                offsetZ = spawner.offsetZ,
                studSpacingX = spawner.studSpacingX,
                studSpacingZ = spawner.studSpacingZ,
                worldPosition = spawner.transform.position
            };
        }
    }

    private bool IsFloorSpawner(LegoBlockSocketSpawner spawner)
    {
        if (spawner == null)
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

    private void ScaleFloorGridXZOnly(float scale)
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

            // Keep height unscaled.
            spawner.socketY = baseData.socketY;

            // Only horizontal grid spacing scales.
            spawner.offsetX = baseData.offsetX * scale;
            spawner.offsetZ = baseData.offsetZ * scale;
            spawner.studSpacingX = baseData.studSpacingX * scale;
            spawner.studSpacingZ = baseData.studSpacingZ * scale;

            spawner.RespawnSockets();

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

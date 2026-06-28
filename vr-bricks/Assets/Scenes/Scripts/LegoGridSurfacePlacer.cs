using UnityEngine;

/// <summary>
/// Safe floor grid placer.
/// 
/// This version prevents duplicate grid spawning.
/// It should be placed on an ALWAYS ACTIVE world object, NOT under MenuCanvas / BlockMenu.
/// </summary>
public class LegoGridSurfacePlacer : MonoBehaviour
{
    [Header("Grid Prefab")]
    [SerializeField] private GameObject floorGridPrefab;

    [Header("Manual Target Surface")]
    [SerializeField] private Transform targetSurface;

    [Header("Placement")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;
    [SerializeField] private float yawOffset = 0f;

    [Header("Behaviour")]
    [SerializeField] private bool placeOnStart = true;

    [Tooltip("Keep false unless you really want to delete and respawn the grid.")]
    [SerializeField] private bool replaceExistingGrid = false;

    [Header("Floor Height Lock")]
    [SerializeField] private bool lockY = true;
    [SerializeField] private float lockedY = -0.4f;

    private GameObject spawnedGrid;

    private void Start()
    {
        if (placeOnStart)
            PlaceGrid();
    }

    [ContextMenu("Place Grid")]
    public void PlaceGrid()
    {
        if (spawnedGrid != null && !replaceExistingGrid)
        {
            ApplyLockedHeight(spawnedGrid);
            return;
        }

        if (floorGridPrefab == null)
        {
            Debug.LogWarning("LegoGridSurfacePlacer: No floorGridPrefab assigned.", this);
            return;
        }

        if (targetSurface == null)
        {
            Debug.LogWarning("LegoGridSurfacePlacer: No targetSurface assigned.", this);
            return;
        }

        if (spawnedGrid != null && replaceExistingGrid)
            Destroy(spawnedGrid);

        spawnedGrid = Instantiate(floorGridPrefab, transform);

        spawnedGrid.transform.position = targetSurface.TransformPoint(localOffset);
        spawnedGrid.transform.rotation =
            targetSurface.rotation * Quaternion.Euler(0f, yawOffset, 0f);

        ApplyLockedHeight(spawnedGrid);

        Debug.Log("LegoGridSurfacePlacer: Grid placed on " + targetSurface.name, this);
    }

    public void SetTargetSurface(Transform newTargetSurface)
    {
        targetSurface = newTargetSurface;
        PlaceGrid();
    }

    public GameObject GetSpawnedGrid()
    {
        return spawnedGrid;
    }

    private void ApplyLockedHeight(GameObject grid)
    {
        if (!lockY || grid == null)
            return;

        Vector3 position = grid.transform.position;
        position.y = lockedY;
        grid.transform.position = position;

        Vector3 scale = grid.transform.localScale;
        scale.y = 1f;
        grid.transform.localScale = scale;
    }
}

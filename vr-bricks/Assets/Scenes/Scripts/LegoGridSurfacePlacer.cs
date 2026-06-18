using UnityEngine;

public class LegoGridSurfacePlacer : MonoBehaviour
{
    [Header("Grid Prefab")]
    [SerializeField] private GameObject floorGridPrefab;

    [Header("Manual Target Surface")]
    [Tooltip("Zum Testen: Zieh hier später eine echte MRUK-Fläche oder einen Fake-Tisch rein.")]
    [SerializeField] private Transform targetSurface;

    [Header("Placement")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    [Tooltip("Zusätzliche Drehung des Grids um die lokale Y-Achse der Fläche.")]
    [SerializeField] private float yawOffset = 0f;

    [Header("Behaviour")]
    [SerializeField] private bool placeOnStart = true;
    [SerializeField] private bool destroyOldGridBeforePlacing = true;

    private GameObject spawnedGrid;

    private void Start()
    {
        if (placeOnStart)
            PlaceGrid();
    }

    [ContextMenu("Place Grid")]
    public void PlaceGrid()
    {
        if (floorGridPrefab == null)
        {
            Debug.LogWarning("LegoGridSurfacePlacer: No floorGridPrefab assigned.");
            return;
        }

        if (targetSurface == null)
        {
            Debug.LogWarning("LegoGridSurfacePlacer: No targetSurface assigned.");
            return;
        }

        if (spawnedGrid != null && destroyOldGridBeforePlacing)
            Destroy(spawnedGrid);

        spawnedGrid = Instantiate(floorGridPrefab, transform);

        spawnedGrid.transform.position = targetSurface.TransformPoint(localOffset);
        spawnedGrid.transform.rotation =
            targetSurface.rotation * Quaternion.Euler(0f, yawOffset, 0f);

        Debug.Log("LegoGridSurfacePlacer: Grid placed on " + targetSurface.name);
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
}
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LegoBlock : MonoBehaviour
{
    [Header("Block Dimensions")]
    [Tooltip("Number of studs wide (X axis).")]
    public int width = 2;

    [Tooltip("Number of studs long (Z axis).")]
    public int length = 2;

    [Tooltip("Height units of the block (Y axis). 1 = Standard block height.")]
    public float height = 1;

    [Header("Grid Settings")]
    [Tooltip("The Unity Layer where the Lego Sockets are assigned.")]
    [SerializeField] private LayerMask socketLayer;

    private readonly Vector3 pivotSpacing = new Vector3(0.5f, 0.2f, 0.5f);

    public void takeBottomSpace(Vector3 centerSocketPosition)
    {
        Vector3 boxHalfExtents = new Vector3(
            (width * pivotSpacing.x) / 2f,
            pivotSpacing.y / 2f,
            (length * pivotSpacing.z) / 2f
        );

        Vector3 boxCenter = centerSocketPosition;
        boxCenter.y += 0.05f; 

        Collider[] overlappedColliders = Physics.OverlapBox(boxCenter, boxHalfExtents, transform.rotation, socketLayer);

        foreach (Collider col in overlappedColliders)
        {
            LegoSocket socket = col.GetComponent<LegoSocket>();
            if (socket != null && socket.gameObject.transform.position != centerSocketPosition)
            {
                socket.onBlockLeave(); 
            }
        }
    }

    public void releaseBottomSpace(Vector3 centerSocketPosition)
    {
        Vector3 boxHalfExtents = new Vector3(
            (width * pivotSpacing.x) / 2f,
            pivotSpacing.y / 2f,
            (length * pivotSpacing.z) / 2f
        );

        Vector3 boxCenter = centerSocketPosition;
        boxCenter.y += 0.05f;

        Collider[] overlappedColliders = Physics.OverlapBox(boxCenter, boxHalfExtents, transform.rotation, socketLayer);

        foreach (Collider col in overlappedColliders)
        {
            LegoSocket socket = col.GetComponent<LegoSocket>();
            if (socket != null)
            {
                socket.onBlockEnters(); 
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 boxSize = new Vector3(width * pivotSpacing.x, height * 1.0f, length * pivotSpacing.z);
        Gizmos.DrawWireCube(transform.position, boxSize);
    }
}

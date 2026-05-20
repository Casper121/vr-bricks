using UnityEngine;

public class LegoBlock : MonoBehaviour
{
    [Tooltip("Width of the Block (X Axis) ")]
    public int width = 1;  // Tamaño en X

    [Tooltip("Lenght of the Block (Z Axis) ")]
    public int length = 1; // Tamaño en Z

    public LayerMask socketLayer; 

    public void takeBottomSpace(Vector3 centerOfSocket)
    {
        Vector3 boxSize = new Vector3(width - 0.1f, 0.5f, length - 0.1f);

        Collider[] closedSocket = Physics.OverlapBox(centerOfSocket, boxSize / 2, transform.rotation, socketLayer);

        foreach (Collider col in closedSocket)
        {
            var socket = col.GetComponent<LegoSocket>();
            if (socket != null && col.transform.position != centerOfSocket) 
            {
                socket.onBlockLeave();
            }
        }
    }

    public void releaseBottomSpace(Vector3 centerOfSocket)
    {
        Vector3 boxSize = new Vector3(width - 0.1f, 0.5f, length - 0.1f);
        Collider[] socketsLiberados = Physics.OverlapBox(centerOfSocket, boxSize / 2, transform.rotation, socketLayer);

        foreach (Collider col in socketsLiberados)
        {
            var socket = col.GetComponent<LegoSocket>();
            if (socket != null)
            {
                socket.onBlockEnters();
            }
        }
    }
}

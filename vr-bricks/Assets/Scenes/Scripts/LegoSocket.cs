using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class LegoSocket : MonoBehaviour
{
  private XRSocketInteractor socketInteractor;
    private Transform activeBottomAttach;
    private Transform dynamicAttachPoint; 

    private void Awake()
    {
        socketInteractor = GetComponent<XRSocketInteractor>();
        socketInteractor.selectEntered.AddListener(OnBlockSnapped);
        socketInteractor.selectExited.AddListener(OnBlockRemoved);

        GameObject attachGO = new GameObject("DynamicAttachPoint");
        dynamicAttachPoint = attachGO.transform;
        dynamicAttachPoint.SetParent(transform);
        dynamicAttachPoint.localPosition = Vector3.zero;
        dynamicAttachPoint.localRotation = Quaternion.identity;

        socketInteractor.attachTransform = dynamicAttachPoint;
    }

    private void OnDestroy()
    {
        if (socketInteractor != null)
        {
            socketInteractor.selectEntered.RemoveListener(OnBlockSnapped);
            socketInteractor.selectExited.RemoveListener(OnBlockRemoved);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("BottomAttach"))
        {
            activeBottomAttach = other.transform;

            LegoBlock block = other.GetComponentInParent<LegoBlock>();
            if (block != null)
            {
                Vector3 localOffset = -activeBottomAttach.localPosition;
                dynamicAttachPoint.localPosition = localOffset;
            }
        }
    }

    private void OnBlockSnapped(SelectEnterEventArgs args)
    {
        LegoBlock block = args.interactableObject.transform.gameObject.GetComponentInParent<LegoBlock>();

        if (block != null)
        {
            Rigidbody rb = block.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            if (activeBottomAttach == null)
            {
                float halfHeight = (block.height * 1.0f) / 2f;
                dynamicAttachPoint.localPosition = new Vector3(0, halfHeight, 0);
            }

            socketInteractor.showInteractableHoverMeshes = false;
            block.takeBottomSpace(transform.position);
        }
    }

    private void OnBlockRemoved(SelectExitEventArgs args)
    {
        LegoBlock block = args.interactableObject.transform.gameObject.GetComponentInParent<LegoBlock>();

        if (block != null)
        {
            Rigidbody rb = block.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            block.releaseBottomSpace(transform.position);
        }

        dynamicAttachPoint.localPosition = Vector3.zero;
        activeBottomAttach = null; 
    }

    public void onBlockLeave() => socketInteractor.socketActive = false;
    public void onBlockEnters() => socketInteractor.socketActive = true;
}
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class LegoSocket : MonoBehaviour
{
    private XRSocketInteractor socketInteractor;
    private Transform dynamicAttachPoint; 
    private const float BLOCK_HEIGHT_UNIT = 1.0f;
    

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

    private void OnBlockSnapped(SelectEnterEventArgs args)
    {
        LegoBlock block = args.interactableObject.transform.gameObject.GetComponentInParent<LegoBlock>();

        if (block != null)
        {
            Rigidbody rb = block.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            float perfectY = block.height * BLOCK_HEIGHT_UNIT;

            dynamicAttachPoint.localPosition = new Vector3(0f, perfectY, 0f);
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
            socketInteractor.showInteractableHoverMeshes = true;
        }
        
        socketInteractor.showInteractableHoverMeshes = true;
        dynamicAttachPoint.localPosition = Vector3.zero;
    }

    public void onBlockLeave() => socketInteractor.socketActive = false;
    public void onBlockEnters() => socketInteractor.socketActive = true;
}
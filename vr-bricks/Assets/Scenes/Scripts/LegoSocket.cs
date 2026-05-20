using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class LegoSocket : MonoBehaviour
{
    private XRSocketInteractor socketInteractor;

    private void Awake()
    {
        socketInteractor = GetComponent<XRSocketInteractor>();
        socketInteractor.selectEntered.AddListener(OnBlockSnapped);
        socketInteractor.selectExited.AddListener(OnBlockRemoved);
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
            block.takeBottomSpace(transform.position);
        }
    }

    private void OnBlockRemoved(SelectExitEventArgs args)
    {
        LegoBlock block = args.interactableObject.transform.gameObject.GetComponentInParent<LegoBlock>();

        if (block != null)
            block.releaseBottomSpace(transform.position);
    }

    public void onBlockLeave() => socketInteractor.socketActive = false;
    public void onBlockEnters() => socketInteractor.socketActive = true;
}
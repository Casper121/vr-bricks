using UnityEngine;

public class VRWristMenuTrigger : MonoBehaviour
{
    public GameObject canvas; 
    public Transform camaraVR;              

    [Range(10, 90)]
    public float activationAngle = 35f; 
    public float margin = 15f;
    public Vector3 ejePalma = Vector3.up; 

    private bool menuActivo = false;

    void Start()
    {
        if (camaraVR == null && Camera.main != null)
        {
            camaraVR = Camera.main.transform;
        }
        
        if (canvas != null) 
            canvas.SetActive(false);
    }

    void Update()
    {
        if (camaraVR == null || canvas == null) return;

        Vector3 palmDir = transform.TransformDirection(ejePalma);
        Vector3 eyeDir = (camaraVR.position - transform.position).normalized;
        float actualAngle = Vector3.Angle(palmDir, eyeDir);

        if (!menuActivo && actualAngle < activationAngle)
        {
            menuActivo = true;
            canvas.SetActive(true);
        }
        else if (menuActivo && actualAngle > (activationAngle + margin))
        {
            menuActivo = false;
            canvas.SetActive(false);
        }
    }
}

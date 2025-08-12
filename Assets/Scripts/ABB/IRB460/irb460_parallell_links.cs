using UnityEngine;

public class ParallelLinkDriver : MonoBehaviour
{
    [Header("Source Joint")]
    public Transform sourceJoint;

    [Header("Rotation Settings")]
    public Vector3 rotationAxis = Vector3.up; // Typically Y for IRB 460 arm
    public bool invert = true;                   // Mirror motion (true for IRB 460)
    public float angleOffset = 0f;               // Offset in degrees, if needed

    void Update()
    {
        if (sourceJoint == null) return;

        float sourceAngle = Vector3.Dot(sourceJoint.localEulerAngles, rotationAxis);
        if (invert) sourceAngle *= -1;

        transform.localRotation = Quaternion.AngleAxis(sourceAngle + angleOffset, rotationAxis);
    }
}

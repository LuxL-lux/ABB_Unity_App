using UnityEngine;

public class IRB460TestController : MonoBehaviour
{
    [Header("Joint Transforms")]
    public Transform axis2; // Main arm
    public Transform axis3; // Parallel-linked arm

    [Header("Rotation Settings")]
    public float speed = 30f; // Degrees per second
    public float maxAngle = 90f;
    public float minAngle = -90f;

    private float joint2Angle = 0f;
    private float joint3Angle = 0f;

    void Update()
    {
        // Input: A/D to rotate Axis2
        float inputaxis2 = Input.GetKey(KeyCode.D) ? 1f : Input.GetKey(KeyCode.A) ? -1f : 0f;

        // Input: W/S to rotate Axis3
        float inputaxis3 = Input.GetKey(KeyCode.W) ? 1f : Input.GetKey(KeyCode.S) ? -1f : 0f;

        // Update joint angle
        joint2Angle += inputaxis2 * speed * Time.deltaTime;
        joint2Angle = Mathf.Clamp(joint2Angle, minAngle, maxAngle);

        // Calc Offset for Joint3

        // Update joint angle
        joint3Angle += (inputaxis3-inputaxis2) * speed * Time.deltaTime;
        joint3Angle = Mathf.Clamp(joint3Angle, minAngle, maxAngle);

        // Rotate Axis2 (around Y)
        axis2.localRotation = Quaternion.AngleAxis(joint2Angle, Vector3.up);

        // Rotate Axis3 (around Y)
        axis3.localRotation = Quaternion.AngleAxis(joint3Angle, Vector3.up);
    }
}

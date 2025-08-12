using UnityEngine;
using System.Collections;

public class CollisionCube : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Collided with cube");
        foreach (ContactPoint contact in collision.contacts)
        {
            Debug.Log(contact.point + contact.normal);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        Debug.Log("Collided with cube");
        foreach (ContactPoint contact in collision.contacts)
        {
            Debug.Log(contact.point + contact.normal);
        }
    }
}

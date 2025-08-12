// System
using System;
// Unity 
using UnityEngine;
using static abb_data_processing;
using Debug = UnityEngine.Debug;

public class irb460_link2 : MonoBehaviour
{
    void FixedUpdate()
    {
        try
        {
            transform.localEulerAngles = new Vector3(0f, 0f, (float)((-1) * ABB_Stream_Data.J_Orientation[0]));
        }
        catch (Exception e)
        {
            Debug.Log("Exception:" + e);
        }
    }
    void OnApplicationQuit()
    {
        Destroy(this);
    }
}

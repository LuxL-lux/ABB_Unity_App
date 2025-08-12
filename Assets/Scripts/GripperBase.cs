using UnityEngine;
using Preliy.Flange;
using Preliy.Flange.Common;

public class SchunkGripperController : MonoBehaviour
{
    [Header("Gripper Configuration")]
    [SerializeField] private Transform finger1;
    [SerializeField] private Transform finger2;
    [SerializeField] private Transform tcpPoint;
    
    [Header("Movement Settings")]
    [SerializeField] private float maxOpenDistance = 0.1f;
    [SerializeField] private float closeSpeed = 2.0f;
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Manual Control")]
    [SerializeField, Range(0f, 1f)] private float manualGripperPosition = 0f; // Manual slider control
    
    [Header("Current State")]
    [SerializeField] private float currentOpenAmount = 0f; // 0 = closed, 1 = fully open
    [SerializeField] private bool isMoving = false;
    [SerializeField] private bool isUnderABBControl = false;
    
    private Vector3 finger1StartPos;
    private Vector3 finger2StartPos;
    private Tool toolComponent;
    private Gripper gripperComponent;
    private ABBToolController abbToolController;
    
    private void Start()
    {
        if (finger1 != null) finger1StartPos = finger1.localPosition;
        if (finger2 != null) finger2StartPos = finger2.localPosition;
        
        toolComponent = GetComponent<Tool>();
        if (toolComponent == null)
        {
            toolComponent = gameObject.AddComponent<Tool>();
            toolComponent.Point = tcpPoint;
        }
        
        gripperComponent = GetComponent<Gripper>();
        if (gripperComponent == null)
        {
            gripperComponent = gameObject.AddComponent<Gripper>();
        }
        
        // Find ABB Tool Controller (usually on robot root)
        abbToolController = FindFirstObjectByType<ABBToolController>();
        if (abbToolController != null)
        {
            // Subscribe to gripper state changes from ABB controller
            abbToolController.OnGripperStateChanged += OnABBGripperStateChanged;
        }
    }
    
    private void Update()
    {
        // Handle manual control via inspector slider (only when not under ABB control)
        if (!isUnderABBControl && !isMoving)
        {
            if (Mathf.Abs(manualGripperPosition - currentOpenAmount) > 0.01f)
            {
                SetGripperPosition(manualGripperPosition);
            }
        }
    }
    
    public void SetGripperPosition(float openAmount)
    {
        openAmount = Mathf.Clamp01(openAmount);
        currentOpenAmount = openAmount;
        
        if (finger1 != null && finger2 != null)
        {
            float distance = maxOpenDistance * movementCurve.Evaluate(openAmount);
            
            finger1.localPosition = finger1StartPos + Vector3.right * distance * 0.5f;
            finger2.localPosition = finger2StartPos - Vector3.right * distance * 0.5f;
        }
        
        if (openAmount < 0.1f && gripperComponent != null)
        {
            gripperComponent.Grip(true);
        }
        else if (openAmount > 0.8f && gripperComponent != null)
        {
            gripperComponent.Grip(false);
        }
    }
    
    [ContextMenu("Manual: Open Gripper")]
    public void ManualOpenGripper()
    {
        isUnderABBControl = false;
        if (!isMoving) StartCoroutine(MoveGripper(1f));
    }
    
    [ContextMenu("Manual: Close Gripper")]
    public void ManualCloseGripper()
    {
        isUnderABBControl = false;
        if (!isMoving) StartCoroutine(MoveGripper(0f));
    }
    
    [ContextMenu("Manual: Half Open")]
    public void ManualHalfOpen()
    {
        isUnderABBControl = false;
        SetGripperPosition(0.5f);
    }
    
    private System.Collections.IEnumerator MoveGripper(float targetAmount)
    {
        isMoving = true;
        float startAmount = currentOpenAmount;
        float elapsed = 0f;
        float duration = Mathf.Abs(targetAmount - startAmount) / closeSpeed;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float newAmount = Mathf.Lerp(startAmount, targetAmount, t);
            SetGripperPosition(newAmount);
            yield return null;
        }
        
        SetGripperPosition(targetAmount);
        manualGripperPosition = targetAmount; // Sync manual slider
        isMoving = false;
    }
    
    public bool IsOpen => currentOpenAmount > 0.5f;
    public bool IsClosed => currentOpenAmount < 0.1f;
    public bool IsMoving => isMoving;
    public float OpenAmount => currentOpenAmount;
    
    // ABB Tool Controller integration
    private void OnABBGripperStateChanged(bool isOpen)
    {
        Debug.Log($"[Schunk Gripper] ABB API command: {(isOpen ? "Open" : "Close")} gripper");
        
        // Switch to ABB control mode and animate gripper
        isUnderABBControl = true;
        
        if (isOpen)
        {
            if (!isMoving) StartCoroutine(MoveGripper(1f)); // Open
        }
        else
        {
            if (!isMoving) StartCoroutine(MoveGripper(0f)); // Close
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (abbToolController != null)
        {
            abbToolController.OnGripperStateChanged -= OnABBGripperStateChanged;
        }
    }
}
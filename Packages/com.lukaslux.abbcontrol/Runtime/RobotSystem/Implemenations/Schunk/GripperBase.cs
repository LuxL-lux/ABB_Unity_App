using UnityEngine;
using Preliy.Flange;
using Preliy.Flange.Common;
using RobotSystem.Core;

/// <summary>
/// Controls the Schunk gripper visualization based on the RobotState.
/// Automatically subscribes to RobotManager state updates and responds to GripperOpen changes.
/// Also supports manual control via inspector slider when not receiving robot state updates.
/// </summary>
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
    
    private Vector3 finger1StartPos;
    private Vector3 finger2StartPos;
    private Tool toolComponent;
    private Gripper gripperComponent;
    private RobotManager robotManager;
    private bool lastGripperState = false;
    
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
        
        // Find Robot Manager to access robot state
        robotManager = FindFirstObjectByType<RobotManager>();
        if (robotManager != null)
        {
            // Subscribe to robot state changes using the public event
            robotManager.OnStateUpdated += OnRobotStateUpdated;
            
            // Initialize gripper to current robot state
            var currentState = robotManager.GetCurrentState();
            if (currentState != null)
            {
                lastGripperState = currentState.GripperOpen;
                // Set initial position without animation
                SetGripperPosition(lastGripperState ? 1f : 0f);
                manualGripperPosition = lastGripperState ? 1f : 0f;
                Debug.Log($"[Schunk Gripper] Initialized to robot state: {(lastGripperState ? "OPEN" : "CLOSED")}");
            }
        }
        else
        {
            Debug.LogWarning("[Schunk Gripper] RobotManager not found. Gripper will not respond to robot state changes.");
        }
    }
    
    private void Update()
    {
        // Handle manual control via inspector slider (only when not under RWS control)
        if (!isMoving)
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
        
        // Only grip when gripper is actually closed and fingers are in contact position
        // Only release when gripper is sufficiently open
        if (gripperComponent != null)
        {
            if (openAmount < 0.05f && !gripperComponent.Gripped) // Very closed and not already gripped
            {
                gripperComponent.Grip(true);
            }
            else if (openAmount > 0.3f && gripperComponent.Gripped) // Sufficiently open and currently gripped
            {
                gripperComponent.Grip(false);
            }
        }
    }
    
    [ContextMenu("Manual: Open Gripper")]
    public void ManualOpenGripper()
    {
        if (!isMoving) StartCoroutine(MoveGripper(1f));
    }
    
    [ContextMenu("Manual: Close Gripper")]
    public void ManualCloseGripper()
    {
        if (!isMoving) StartCoroutine(MoveGripper(0f));
    }
    
    [ContextMenu("Manual: Half Open")]
    public void ManualHalfOpen()
    {
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
    
    // Robot State integration
    private void OnRobotStateUpdated(RobotState state)
    {
        if (state == null || isMoving) return;
        
        // Check if gripper state has changed
        bool currentGripperOpen = state.GripperOpen;
        
        if (currentGripperOpen != lastGripperState)
        {
            lastGripperState = currentGripperOpen;
            
            // Determine target position based on state
            float targetPosition = currentGripperOpen ? 1f : 0f;
            
            // Only move if not already at the target position
            if (Mathf.Abs(currentOpenAmount - targetPosition) > 0.01f)
            {
                StartCoroutine(MoveGripper(targetPosition));
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (robotManager != null)
        {
            robotManager.OnStateUpdated -= OnRobotStateUpdated;
        }
    }
}
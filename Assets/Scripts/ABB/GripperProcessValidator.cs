/****************************************************************************
Gripper Process Validator - Monitors gripper operations and validates process steps
****************************************************************************/

using System.Collections.Generic;
using UnityEngine;
using Preliy.Flange.Common;

[AddComponentMenu("ABB/Gripper Process Validator")]
public class GripperProcessValidator : MonoBehaviour
{
    [Header("Validation Settings")]
    [SerializeField] private bool validateGripOperations = true;
    [SerializeField] private bool trackObjectMovement = true;
    [SerializeField] private bool validateProcessSequence = true;
    [SerializeField] private float objectStabilityTime = 1.0f; // Time object must be stable
    [SerializeField] private float maxObjectVelocity = 0.01f; // Max velocity for "stable"
    
    [Header("Process Monitoring")]
    [SerializeField] private bool requireObjectInGripper = true;
    [SerializeField] private float gripperCloseTimeout = 5.0f;
    [SerializeField] private float objectDetectionRadius = 0.1f;
    [SerializeField] private LayerMask grippableObjectLayers = -1;
    
    [Header("Status (Read Only)")]
    [SerializeField, ReadOnly] private string currentProcess = "Idle";
    [SerializeField, ReadOnly] private bool objectInGripper = false;
    [SerializeField, ReadOnly] private string grippedObjectName = "";
    [SerializeField, ReadOnly] private bool objectStable = true;
    [SerializeField, ReadOnly] private float objectStabilityTimer = 0f;
    [SerializeField, ReadOnly] private Vector3 lastObjectPosition = Vector3.zero;
    [SerializeField, ReadOnly] private bool processValid = true;
    
    private ABBSafetyLogger safetyLogger;
    private ABBToolController toolController;
    private SchunkGripperController gripperController;
    private Gripper flangeGripper;
    
    // Object tracking
    private GameObject currentGrippedObject;
    private Rigidbody grippedObjectRigidbody;
    private Part grippedObjectPart;
    private Vector3 expectedObjectPosition;
    private Quaternion expectedObjectRotation;
    
    // Process validation
    private List<ProcessStep> processHistory = new List<ProcessStep>();
    private ProcessStep currentStep;
    private float processStartTime;
    
    public enum ProcessStepType
    {
        Idle,
        MovingToObject,
        OpeningGripper,
        PositioningForGrip,
        ClosingGripper,
        ValidatingGrip,
        MovingWithObject,
        PositioningForRelease,
        OpeningGripperToRelease,
        ValidatingRelease,
        RetractingFromObject,
        ProcessComplete,
        ProcessFailed
    }
    
    [System.Serializable]
    public class ProcessStep
    {
        public ProcessStepType type;
        public float startTime;
        public float endTime;
        public bool successful;
        public string errorMessage;
        public Vector3 robotPosition;
        public string objectName;
    }
    
    // Public properties
    public bool ProcessValid => processValid;
    
    // Events
    public System.Action<ProcessStepType> OnProcessStepChanged;
    public System.Action<string> OnObjectGripped;
    public System.Action<string> OnObjectReleased;
    public System.Action<string> OnProcessValidationFailed;
    public System.Action OnObjectMovementDetected;
    
    private void Start()
    {
        // Get references
        safetyLogger = ABBSafetyLogger.Instance;
        toolController = FindFirstObjectByType<ABBToolController>();
        gripperController = FindFirstObjectByType<SchunkGripperController>();
        
        // Find Flange gripper component
        if (gripperController != null)
        {
            flangeGripper = gripperController.GetComponent<Gripper>();
        }
        
        // Subscribe to events
        if (toolController != null)
        {
            toolController.OnGripperStateChanged += OnGripperStateChanged;
            toolController.OnToolCommandExecuted += OnToolCommandExecuted;
            toolController.OnToolError += OnToolError;
        }
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.System,
                "Gripper Process Validator initialized",
                $"Monitoring gripper operations and process validation");
        }
        
        // Initialize process
        StartProcessStep(ProcessStepType.Idle);
    }
    
    private void Update()
    {
        if (trackObjectMovement && currentGrippedObject != null)
        {
            MonitorObjectMovement();
        }
        
        if (validateProcessSequence)
        {
            ValidateCurrentProcess();
        }
    }
    
    private void OnGripperStateChanged(bool isOpen)
    {
        if (isOpen)
        {
            // Gripper opened
            if (currentStep?.type == ProcessStepType.OpeningGripper)
            {
                CompleteProcessStep(true);
                StartProcessStep(ProcessStepType.PositioningForGrip);
            }
            else if (currentStep?.type == ProcessStepType.OpeningGripperToRelease)
            {
                CompleteProcessStep(true);
                ValidateObjectRelease();
            }
        }
        else
        {
            // Gripper closed
            if (currentStep?.type == ProcessStepType.ClosingGripper)
            {
                CompleteProcessStep(true);
                StartProcessStep(ProcessStepType.ValidatingGrip);
                ValidateGripOperation();
            }
        }
    }
    
    private void OnToolCommandExecuted(string command)
    {
        if (safetyLogger != null)
        {
            safetyLogger.LogGripperOperation(command, true, grippedObjectName);
        }
    }
    
    private void OnToolError(string error)
    {
        if (safetyLogger != null)
        {
            safetyLogger.LogGripperOperation("Error", false, error);
        }
        
        ProcessValidationFailed($"Tool error: {error}");
    }
    
    private void StartProcessStep(ProcessStepType stepType)
    {
        // Complete previous step if exists
        if (currentStep != null && currentStep.endTime == 0)
        {
            CompleteProcessStep(false, "Step interrupted");
        }
        
        currentStep = new ProcessStep
        {
            type = stepType,
            startTime = Time.time,
            robotPosition = transform.position,
            objectName = grippedObjectName
        };
        
        currentProcess = stepType.ToString();
        processHistory.Add(currentStep);
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.Gripper,
                $"Process step started: {stepType}",
                $"Object: {grippedObjectName}");
        }
        
        OnProcessStepChanged?.Invoke(stepType);
    }
    
    private void CompleteProcessStep(bool successful, string errorMessage = "")
    {
        if (currentStep == null) return;
        
        currentStep.endTime = Time.time;
        currentStep.successful = successful;
        currentStep.errorMessage = errorMessage;
        
        if (safetyLogger != null)
        {
            string status = successful ? "completed" : "failed";
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.Gripper,
                $"Process step {status}: {currentStep.type}",
                $"Duration: {currentStep.endTime - currentStep.startTime:F2}s, Error: {errorMessage}");
        }
        
        if (!successful)
        {
            ProcessValidationFailed(errorMessage);
        }
    }
    
    private void ValidateGripOperation()
    {
        if (!validateGripOperations) return;
        
        // Check if object is actually in gripper
        DetectObjectInGripper();
        
        if (requireObjectInGripper && !objectInGripper)
        {
            ProcessValidationFailed("No object detected in gripper after close operation");
            return;
        }
        
        if (objectInGripper && currentGrippedObject != null)
        {
            // Validate object is properly gripped
            if (grippedObjectRigidbody != null && !grippedObjectRigidbody.isKinematic)
            {
                ProcessValidationFailed("Gripped object is not kinematic - grip may have failed");
                return;
            }
            
            // Check if object is parented to gripper
            if (currentGrippedObject.transform.parent != gripperController.transform)
            {
                ProcessValidationFailed("Gripped object not properly parented to gripper");
                return;
            }
            
            // Store expected position for movement monitoring
            expectedObjectPosition = currentGrippedObject.transform.position;
            expectedObjectRotation = currentGrippedObject.transform.rotation;
            
            if (safetyLogger != null)
            {
                safetyLogger.LogGripperOperation("Grip Validated", true, grippedObjectName);
            }
            
            OnObjectGripped?.Invoke(grippedObjectName);
            StartProcessStep(ProcessStepType.MovingWithObject);
        }
        else
        {
            StartProcessStep(ProcessStepType.Idle);
        }
    }
    
    private void ValidateObjectRelease()
    {
        // Wait a moment for physics to settle
        Invoke(nameof(CheckObjectRelease), 0.5f);
    }
    
    private void CheckObjectRelease()
    {
        bool objectReleased = false;
        
        if (currentGrippedObject != null)
        {
            // Check if object is no longer parented to gripper
            if (currentGrippedObject.transform.parent != gripperController.transform)
            {
                objectReleased = true;
            }
            
            // Check if object rigidbody is no longer kinematic (if it should be physics-based)
            if (grippedObjectPart != null && grippedObjectPart.Type == Part.PartPhysicsType.Physics)
            {
                if (grippedObjectRigidbody != null && grippedObjectRigidbody.isKinematic)
                {
                    ProcessValidationFailed("Object not properly released - still kinematic");
                    return;
                }
            }
        }
        
        if (objectReleased)
        {
            if (safetyLogger != null)
            {
                safetyLogger.LogGripperOperation("Release Validated", true, grippedObjectName);
            }
            
            OnObjectReleased?.Invoke(grippedObjectName);
            
            // Clear object references
            currentGrippedObject = null;
            grippedObjectRigidbody = null;
            grippedObjectPart = null;
            grippedObjectName = "";
            objectInGripper = false;
            
            StartProcessStep(ProcessStepType.ProcessComplete);
        }
        else
        {
            ProcessValidationFailed("Object release validation failed");
        }
    }
    
    private void DetectObjectInGripper()
    {
        // Use Flange Gripper component to check for objects
        if (flangeGripper != null && flangeGripper.Gripped)
        {
            objectInGripper = true;
            
            // Try to find the gripped object
            if (currentGrippedObject == null)
            {
                // Look for objects in gripper area
                Collider[] nearbyObjects = Physics.OverlapSphere(
                    gripperController.transform.position, 
                    objectDetectionRadius, 
                    grippableObjectLayers);
                
                foreach (var collider in nearbyObjects)
                {
                    if (collider.GetComponent<Part>() != null)
                    {
                        currentGrippedObject = collider.gameObject;
                        grippedObjectRigidbody = collider.GetComponent<Rigidbody>();
                        grippedObjectPart = collider.GetComponent<Part>();
                        grippedObjectName = currentGrippedObject.name;
                        break;
                    }
                }
            }
        }
        else
        {
            objectInGripper = false;
        }
    }
    
    private void MonitorObjectMovement()
    {
        if (currentGrippedObject == null) return;
        
        Vector3 currentPos = currentGrippedObject.transform.position;
        
        // Check if object has moved significantly from expected position
        float distanceFromExpected = Vector3.Distance(currentPos, expectedObjectPosition);
        
        if (distanceFromExpected > maxObjectVelocity)
        {
            // Object is moving unexpectedly
            objectStable = false;
            objectStabilityTimer = 0f;
            
            if (safetyLogger != null)
            {
                safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.Gripper,
                    "Unexpected object movement detected",
                    $"Object: {grippedObjectName}, Distance: {distanceFromExpected:F4}m");
            }
            
            OnObjectMovementDetected?.Invoke();
        }
        else
        {
            // Object is stable
            if (!objectStable)
            {
                objectStabilityTimer += Time.deltaTime;
                if (objectStabilityTimer >= objectStabilityTime)
                {
                    objectStable = true;
                    expectedObjectPosition = currentPos; // Update expected position
                    expectedObjectRotation = currentGrippedObject.transform.rotation;
                }
            }
        }
        
        lastObjectPosition = currentPos;
    }
    
    private void ValidateCurrentProcess()
    {
        if (currentStep == null) return;
        
        // Check for process timeouts
        float stepDuration = Time.time - currentStep.startTime;
        
        if (currentStep.type == ProcessStepType.ClosingGripper && stepDuration > gripperCloseTimeout)
        {
            ProcessValidationFailed($"Gripper close operation timed out after {gripperCloseTimeout}s");
        }
        
        // Validate process sequence logic
        if (currentStep.type == ProcessStepType.MovingWithObject && !objectInGripper)
        {
            ProcessValidationFailed("Moving with object but no object detected in gripper");
        }
    }
    
    private void ProcessValidationFailed(string reason)
    {
        processValid = false;
        
        if (safetyLogger != null)
        {
            safetyLogger.LogError(ABBSafetyLogger.LogCategory.Gripper,
                "Process validation failed",
                reason);
        }
        
        OnProcessValidationFailed?.Invoke(reason);
        
        CompleteProcessStep(false, reason);
        StartProcessStep(ProcessStepType.ProcessFailed);
    }
    
    // Public methods for external process control
    public void StartGripProcess(string targetObjectName = "")
    {
        grippedObjectName = targetObjectName;
        processValid = true;
        processStartTime = Time.time;
        StartProcessStep(ProcessStepType.MovingToObject);
    }
    
    public void StartReleaseProcess()
    {
        if (objectInGripper)
        {
            StartProcessStep(ProcessStepType.PositioningForRelease);
        }
        else
        {
            ProcessValidationFailed("Cannot start release process - no object in gripper");
        }
    }
    
    public bool IsProcessComplete()
    {
        return currentStep?.type == ProcessStepType.ProcessComplete;
    }
    
    public bool IsProcessFailed()
    {
        return currentStep?.type == ProcessStepType.ProcessFailed;
    }
    
    public List<ProcessStep> GetProcessHistory()
    {
        return new List<ProcessStep>(processHistory);
    }
    
    // Context menu for testing
    [ContextMenu("Test Grip Process")]
    private void TestGripProcess()
    {
        StartGripProcess("TestObject");
    }
    
    [ContextMenu("Reset Process")]
    private void ResetProcess()
    {
        processHistory.Clear();
        currentStep = null;
        processValid = true;
        StartProcessStep(ProcessStepType.Idle);
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (toolController != null)
        {
            toolController.OnGripperStateChanged -= OnGripperStateChanged;
            toolController.OnToolCommandExecuted -= OnToolCommandExecuted;
            toolController.OnToolError -= OnToolError;
        }
    }
    
    private void OnDrawGizmos()
    {
        if (currentGrippedObject != null)
        {
            // Draw object detection radius
            Gizmos.color = objectInGripper ? Color.green : Color.red;
            Gizmos.DrawWireSphere(transform.position, objectDetectionRadius);
            
            // Draw line to gripped object
            Gizmos.color = objectStable ? Color.blue : Color.yellow;
            Gizmos.DrawLine(transform.position, currentGrippedObject.transform.position);
        }
    }
}
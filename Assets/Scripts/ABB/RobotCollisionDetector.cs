/****************************************************************************
Robot Collision Detector - Detects and logs robot collisions with environment
****************************************************************************/

using System.Collections.Generic;
using UnityEngine;
using Preliy.Flange;

[AddComponentMenu("ABB/Robot Collision Detector")]
public class RobotCollisionDetector : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private LayerMask collisionLayers = -1;
    [SerializeField] private bool detectContinuousCollisions = true;
    [SerializeField] private float collisionCooldown = 0.5f;
    [SerializeField] private bool visualizeCollisions = true;
    
    [Header("Robot Parts to Monitor")]
    [SerializeField] private List<Transform> robotLinks = new List<Transform>();
    [SerializeField] private bool autoFindRobotParts = true;
    [SerializeField] private bool useExistingCollidersOnly = false;
    [SerializeField] private List<string> ignoreCollisionBetweenParts = new List<string>();
    
    [Header("Collision Response")]
    [SerializeField] private bool stopRobotOnCollision = true;
    [SerializeField] private bool emergencyStopOnCriticalCollision = true;
    [SerializeField] private List<string> criticalCollisionTags = new List<string> { "Safety_Fence", "Human", "Critical" };
    
    [Header("Status")]
    [SerializeField, ReadOnly] private int totalCollisions = 0;
    [SerializeField, ReadOnly] private string lastCollisionObject = "";
    [SerializeField, ReadOnly] private float lastCollisionTime = 0f;
    [SerializeField, ReadOnly] private bool robotStopped = false;
    
    private Dictionary<Transform, List<Collider>> robotColliders = new Dictionary<Transform, List<Collider>>();
    private Dictionary<string, float> collisionCooldowns = new Dictionary<string, float>();
    private ABBSafetyLogger safetyLogger;
    private ABBRobotWebServicesController abbController;
    private Controller flangeController;
    
    // Events
    public System.Action<string, string, Vector3> OnCollisionDetected;
    public System.Action<string> OnCriticalCollision;
    
    private void Start()
    {
        // Get references
        safetyLogger = ABBSafetyLogger.Instance;
        abbController = FindFirstObjectByType<ABBRobotWebServicesController>();
        flangeController = GetComponentInParent<Controller>();
        
        // Auto-find robot parts if enabled
        if (autoFindRobotParts)
        {
            FindRobotParts();
        }
        
        // Setup collision detection on robot parts
        SetupCollisionDetection();
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.System, 
                "Robot Collision Detector initialized", 
                $"Monitoring {robotLinks.Count} robot parts");
        }
    }
    
    private void FindRobotParts()
    {
        robotLinks.Clear();
        
        // Find all transforms with "link" or "joint" in name
        Transform[] allChildren = GetComponentsInChildren<Transform>();
        foreach (var child in allChildren)
        {
            string nameLower = child.name.ToLower();
            if (nameLower.Contains("link") || nameLower.Contains("joint") || 
                nameLower.Contains("base") || nameLower.Contains("flange"))
            {
                robotLinks.Add(child);
            }
        }
        
        // Also include any objects with MeshRenderer (robot parts usually have meshes)
        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in meshRenderers)
        {
            if (!robotLinks.Contains(renderer.transform))
            {
                robotLinks.Add(renderer.transform);
            }
        }
    }
    
    private void SetupCollisionDetection()
    {
        // Setup ignore list for adjacent robot parts
        SetupIgnoreList();
        
        foreach (var robotPart in robotLinks)
        {
            if (robotPart == null) continue;
            
            List<Collider> colliders = new List<Collider>();
            
            // Get existing colliders
            Collider[] existingColliders = robotPart.GetComponents<Collider>();
            
            if (useExistingCollidersOnly)
            {
                // Only use existing colliders - don't add new ones
                if (existingColliders.Length == 0)
                {
                    Debug.LogWarning($"[Robot Collision] No colliders found on {robotPart.name} - skipping collision detection for this part");
                    continue;
                }
                colliders.AddRange(existingColliders);
            }
            else
            {
                // Use existing or create new ones
                colliders.AddRange(existingColliders);
                
                // Add collider if none exists
                if (existingColliders.Length == 0)
                {
                    // Try to add appropriate collider based on mesh
                    MeshRenderer meshRenderer = robotPart.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        MeshCollider meshCollider = robotPart.gameObject.AddComponent<MeshCollider>();
                        meshCollider.convex = true; // Required for trigger detection
                        meshCollider.isTrigger = true; // Use as trigger to detect collisions without physics
                        colliders.Add(meshCollider);
                        
                        Debug.Log($"[Robot Collision] Added MeshCollider to {robotPart.name}");
                    }
                    else
                    {
                        // Only add box collider if specifically requested
                        Debug.LogWarning($"[Robot Collision] No mesh found on {robotPart.name} - consider adding colliders manually");
                    }
                }
            }
            
            // Setup collision detection on colliders
            foreach (var collider in colliders)
            {
                // Ensure colliders are triggers for detection
                if (!collider.isTrigger)
                {
                    collider.isTrigger = true;
                }
                
                // Add collision detector component
                RobotPartCollisionDetector detector = collider.gameObject.GetComponent<RobotPartCollisionDetector>();
                if (detector == null)
                {
                    detector = collider.gameObject.AddComponent<RobotPartCollisionDetector>();
                }
                detector.Initialize(this, robotPart.name);
            }
            
            robotColliders[robotPart] = colliders;
        }
        
        // Setup collision ignoring between adjacent parts
        SetupPhysicsIgnoring();
    }
    
    private void SetupIgnoreList()
    {
        // Default ignore patterns for adjacent robot parts
        if (ignoreCollisionBetweenParts.Count == 0)
        {
            ignoreCollisionBetweenParts.AddRange(new string[]
            {
                "base,link1", "link1,link2", "link2,link3", 
                "link3,link4", "link4,link5", "link5,link6",
                "link6,flange", "flange,gripper", "gripper,tcp"
            });
        }
    }
    
    private void SetupPhysicsIgnoring()
    {
        // Ignore collisions between adjacent robot parts to prevent false positives
        foreach (string ignorePair in ignoreCollisionBetweenParts)
        {
            string[] parts = ignorePair.Split(',');
            if (parts.Length != 2) continue;
            
            Transform part1 = GetRobotPartByName(parts[0].Trim());
            Transform part2 = GetRobotPartByName(parts[1].Trim());
            
            if (part1 != null && part2 != null)
            {
                IgnoreCollisionsBetweenParts(part1, part2);
            }
        }
    }
    
    private Transform GetRobotPartByName(string partName)
    {
        foreach (var robotPart in robotLinks)
        {
            if (robotPart != null && robotPart.name.ToLower().Contains(partName.ToLower()))
            {
                return robotPart;
            }
        }
        return null;
    }
    
    private void IgnoreCollisionsBetweenParts(Transform part1, Transform part2)
    {
        if (!robotColliders.ContainsKey(part1) || !robotColliders.ContainsKey(part2)) return;
        
        var colliders1 = robotColliders[part1];
        var colliders2 = robotColliders[part2];
        
        foreach (var col1 in colliders1)
        {
            foreach (var col2 in colliders2)
            {
                if (col1 != null && col2 != null)
                {
                    Physics.IgnoreCollision(col1, col2, true);
                    Debug.Log($"[Robot Collision] Ignoring collisions between {part1.name} and {part2.name}");
                }
            }
        }
    }
    
    public void OnRobotPartCollision(string robotPartName, Collider hitCollider)
    {
        // Safety check for null collider
        if (hitCollider == null)
        {
            Debug.LogWarning($"[Robot Collision] Null collider detected for part: {robotPartName}");
            return;
        }
        
        string hitObjectName = hitCollider.gameObject.name;
        Vector3 collisionPoint = hitCollider.ClosestPoint(transform.position);
        
        // Check collision cooldown (unless continuous detection is enabled)
        string collisionKey = $"{robotPartName}_{hitObjectName}";
        if (!detectContinuousCollisions && collisionCooldowns.ContainsKey(collisionKey))
        {
            if (Time.time - collisionCooldowns[collisionKey] < collisionCooldown)
            {
                return; // Still in cooldown
            }
        }
        
        // Update cooldown (unless continuous detection is enabled)
        if (!detectContinuousCollisions)
        {
            collisionCooldowns[collisionKey] = Time.time;
        }
        
        // Update status
        totalCollisions++;
        lastCollisionObject = hitObjectName;
        lastCollisionTime = Time.time;
        
        // Check if this is a critical collision
        bool isCritical = IsCriticalCollision(hitCollider);
        
        // Log collision
        if (safetyLogger != null)
        {
            safetyLogger.LogCollision(robotPartName, hitObjectName, collisionPoint);
            
            if (isCritical)
            {
                safetyLogger.LogCritical(ABBSafetyLogger.LogCategory.Collision,
                    $"CRITICAL COLLISION: {robotPartName} -> {hitObjectName}",
                    $"Position: {collisionPoint}");
            }
        }
        
        // Visual feedback
        if (visualizeCollisions)
        {
            StartCoroutine(VisualizeCollision(collisionPoint, isCritical));
        }
        
        // Fire events
        OnCollisionDetected?.Invoke(robotPartName, hitObjectName, collisionPoint);
        
        if (isCritical)
        {
            OnCriticalCollision?.Invoke($"{robotPartName} -> {hitObjectName}");
            
            if (emergencyStopOnCriticalCollision)
            {
                EmergencyStop("Critical collision detected");
            }
        }
        else if (stopRobotOnCollision)
        {
            StopRobot("Collision detected");
        }
    }
    
    private bool IsCriticalCollision(Collider hitCollider)
    {
        // Check the hit object itself
        if (HasCriticalTag(hitCollider.gameObject))
        {
            return true;
        }
        
        // Check parent objects up the hierarchy
        Transform current = hitCollider.transform.parent;
        while (current != null)
        {
            if (HasCriticalTag(current.gameObject))
            {
                return true;
            }
            current = current.parent;
        }
        
        return false;
    }
    
    private bool HasCriticalTag(GameObject obj)
    {
        foreach (string criticalTag in criticalCollisionTags)
        {
            if (obj.CompareTag(criticalTag) || 
                obj.name.ToLower().Contains(criticalTag.ToLower()))
            {
                return true;
            }
        }
        return false;
    }
    
    private void StopRobot(string reason)
    {
        if (robotStopped) return;
        
        robotStopped = true;
        
        if (safetyLogger != null)
        {
            safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.System,
                "Robot stopped due to collision", reason);
        }
        
        // TODO: Implement actual robot stop via RWS
        // This would send a stop command to the real ABB robot
        Debug.LogWarning($"[Robot Collision] ROBOT STOPPED: {reason}");
    }
    
    private void EmergencyStop(string reason)
    {
        if (safetyLogger != null)
        {
            safetyLogger.LogCritical(ABBSafetyLogger.LogCategory.System,
                "EMERGENCY STOP triggered", reason);
        }
        
        // TODO: Implement emergency stop via RWS
        // This would send an emergency stop command to the real ABB robot
        Debug.LogError($"[Robot Collision] EMERGENCY STOP: {reason}");
        
        robotStopped = true;
    }
    
    private System.Collections.IEnumerator VisualizeCollision(Vector3 position, bool isCritical)
    {
        Color collisionColor = isCritical ? Color.red : Color.yellow;
        float duration = isCritical ? 2f : 1f;
        
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            Debug.DrawRay(position, Vector3.up * 0.5f, collisionColor, Time.deltaTime);
            Debug.DrawRay(position, Vector3.forward * 0.5f, collisionColor, Time.deltaTime);
            Debug.DrawRay(position, Vector3.right * 0.5f, collisionColor, Time.deltaTime);
            yield return null;
        }
    }
    
    [ContextMenu("Resume Robot")]
    public void ResumeRobot()
    {
        robotStopped = false;
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.System,
                "Robot resumed", "Manual resume command");
        }
        
        Debug.Log("[Robot Collision] Robot operation resumed");
    }
    
    [ContextMenu("Test Collision")]
    private void TestCollision()
    {
        if (robotLinks.Count > 0)
        {
            // Find a robot part with a collider for testing
            Collider testCollider = null;
            string testPartName = "TestLink";
            
            foreach (var robotPart in robotLinks)
            {
                if (robotPart != null)
                {
                    testCollider = robotPart.GetComponent<Collider>();
                    if (testCollider != null)
                    {
                        testPartName = robotPart.name;
                        break;
                    }
                }
            }
            
            if (testCollider != null)
            {
                OnRobotPartCollision(testPartName, testCollider);
            }
            else
            {
                Debug.LogWarning("[Robot Collision] No colliders found on robot parts for testing. Add colliders first or use 'Use Existing Colliders Only' = false");
            }
        }
        else
        {
            Debug.LogWarning("[Robot Collision] No robot parts found for testing");
        }
    }
    
    private void OnDrawGizmos()
    {
        if (!visualizeCollisions) return;
        
        // Draw simple collision detection status indicators
        Gizmos.color = robotStopped ? Color.red : Color.green;
        
        foreach (var robotPart in robotLinks)
        {
            if (robotPart != null)
            {
                Gizmos.DrawWireSphere(robotPart.position, 0.1f);
            }
        }
    }
}

// Helper component for individual robot parts
public class RobotPartCollisionDetector : MonoBehaviour
{
    private RobotCollisionDetector parentDetector;
    private string partName;
    
    public void Initialize(RobotCollisionDetector parent, string name)
    {
        parentDetector = parent;
        partName = name;
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (parentDetector != null)
        {
            parentDetector.OnRobotPartCollision(partName, other);
        }
    }
}
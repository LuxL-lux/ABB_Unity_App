/****************************************************************************
Robot Safety Monitor - Monitors joint limits, speeds, and singularities using Flange data
****************************************************************************/

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Preliy.Flange;

[AddComponentMenu("ABB/Robot Safety Monitor")]
[RequireComponent(typeof(Controller))]
[ExecuteInEditMode]
public class RobotSafetyMonitor : MonoBehaviour
{
    [Header("Monitoring Settings")]
    [SerializeField] private bool monitorJointLimits = true;
    [SerializeField] private bool monitorJointSpeeds = true;
    [SerializeField] private bool monitorSingularities = true;
    [SerializeField] private float warningThreshold = 0.9f; // 90% of limit
    [SerializeField] private float criticalThreshold = 0.95f; // 95% of limit
    
    [Header("Speed Monitoring")]
    [SerializeField] private float speedSampleInterval = 0.1f; // Sample every 100ms
    [SerializeField] private int speedSampleCount = 5; // Rolling average over 5 samples
    
    [Header("Axis-Based Singularity Detection")]
    [SerializeField] private float singularityThreshold = 0.1f; // Manipulability threshold for logging
    [SerializeField] private float singularityCheckInterval = 0.2f; // Check every 200ms
    [SerializeField] private float axisAlignmentThreshold = 0.95f; // cos(~18°) - axes considered aligned
    [SerializeField] private float wristSingularityThreshold = 0.98f; // cos(~11°) - stricter for wrist
    [SerializeField] private float shoulderSingularityThreshold = 0.93f; // cos(~22°) - more lenient for shoulder
    
    [Header("Position Display Options")]
    [SerializeField] private bool showQuaternionFormat = true;
    [SerializeField] private bool showJointFormat = true;
    [SerializeField] private bool logPositionUpdates = false;
    [SerializeField] private float positionUpdateInterval = 1.0f; // Update every second
    
    [Header("Status (Read Only)")]
    [SerializeField, ReadOnly] private JointStatus[] jointStatuses = new JointStatus[6];
    [SerializeField, ReadOnly] private bool[] jointWarnings = new bool[6];
    [SerializeField, ReadOnly] private bool[] jointCritical = new bool[6];
    [SerializeField, ReadOnly] private float currentManipulability = 1.0f;
    [SerializeField, ReadOnly] private bool inSingularity = false;
    [SerializeField, ReadOnly] private Vector3 currentTCPPosition = Vector3.zero;
    [SerializeField, ReadOnly] private string singularityType = "None"; // Current singularity type
    [SerializeField, ReadOnly] private float[] axisAlignments = new float[0]; // Debug info for axis alignments
    
    [Header("RAPID Position Formats")]
    [SerializeField, ReadOnly] private string currentRobTarget = "";
    [SerializeField, ReadOnly] private string currentJoinTarget = "";
    
    private Controller flangeController;
    private ABBSafetyLogger safetyLogger;
    private MechanicalGroup mechanicalGroup;
    private Robot robot;
    
    // Speed monitoring data
    private List<float>[] jointSpeedHistory;
    private float[] previousJointPositions;
    private float lastSpeedSample = 0f;
    private float lastSingularityCheck = 0f;
    private float lastPositionUpdate = 0f;
    
    [System.Serializable]
    public class JointStatus
    {
        public string name = "";
        public float currentPosition = 0f;
        public float currentSpeed = 0f;
        public float currentAcceleration = 0f;
        public float limitMin = -180f;
        public float limitMax = 180f;
        public float maxSpeed = 100f;
        public float maxAcceleration = 500f;
        public float limitUsagePercent = 0f;
        public float speedUsagePercent = 0f;
        public bool isWarning = false;
        public bool isCritical = false;
    }
    
    // Events
    public System.Action<int, float> OnJointLimitWarning;
    public System.Action<int, float> OnJointSpeedWarning;
    public System.Action<float, Vector3> OnSingularityDetected;
    public System.Action OnSingularityResolved;
    
    private void Start()
    {
        // Get references
        flangeController = GetComponent<Controller>();
        safetyLogger = ABBSafetyLogger.Instance;
        mechanicalGroup = flangeController.MechanicalGroup;
        robot = mechanicalGroup?.Robot;
        
        if (robot == null)
        {
            Debug.LogError("[Robot Safety Monitor] No robot found in mechanical group!");
            enabled = false;
            return;
        }
        
        // Initialize monitoring arrays
        InitializeMonitoring();
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.System, 
                "Robot Safety Monitor initialized", 
                $"Monitoring {jointStatuses.Length} joints");
        }
    }
    
    private void InitializeMonitoring()
    {
        int jointCount = robot.Joints?.Count ?? 6;
        
        // Initialize arrays
        jointStatuses = new JointStatus[jointCount];
        jointWarnings = new bool[jointCount];
        jointCritical = new bool[jointCount];
        jointSpeedHistory = new List<float>[jointCount];
        previousJointPositions = new float[jointCount];
        
        // Initialize joint status from Flange configuration
        for (int i = 0; i < jointCount; i++)
        {
            jointStatuses[i] = new JointStatus();
            jointSpeedHistory[i] = new List<float>();
            
            if (robot.Joints != null && i < robot.Joints.Count)
            {
                var joint = robot.Joints[i];
                var config = joint.Config;
                
                jointStatuses[i].name = !string.IsNullOrEmpty(config.Name) ? config.Name : $"Joint_{i + 1}";
                jointStatuses[i].limitMin = config.Limits.x;
                jointStatuses[i].limitMax = config.Limits.y;
                jointStatuses[i].maxSpeed = config.SpeedMax;
                jointStatuses[i].maxAcceleration = config.AccMax;
                
                // Get initial position
                jointStatuses[i].currentPosition = joint.Position.Value;
                previousJointPositions[i] = jointStatuses[i].currentPosition;
            }
        }
    }
    
    private void Update()
    {
        if (robot == null) return;
        
        // Always update position formats when enabled (works in edit mode too)
        if ((showQuaternionFormat || showJointFormat) && Time.time - lastPositionUpdate >= positionUpdateInterval)
        {
            UpdatePositionFormats();
            lastPositionUpdate = Time.time;
        }
        
        // Only run monitoring in play mode
        if (!Application.isPlaying) return;
        
        // Monitor joint limits and positions
        if (monitorJointLimits)
        {
            MonitorJointLimits();
        }
        
        // Monitor joint speeds (with sampling interval)
        if (monitorJointSpeeds && Time.time - lastSpeedSample >= speedSampleInterval)
        {
            MonitorJointSpeeds();
            lastSpeedSample = Time.time;
        }
        
        // Monitor singularities (with check interval)
        if (monitorSingularities && Time.time - lastSingularityCheck >= singularityCheckInterval)
        {
            MonitorSingularities();
            lastSingularityCheck = Time.time;
        }
    }
    
    private void MonitorJointLimits()
    {
        for (int i = 0; i < jointStatuses.Length && i < robot.Joints.Count; i++)
        {
            var joint = robot.Joints[i];
            var config = joint.Config;
            var status = jointStatuses[i];
            
            // Update current position
            status.currentPosition = joint.Position.Value;
            
            // Calculate limit usage percentage
            float range = config.Limits.y - config.Limits.x;
            float normalizedPosition = (status.currentPosition - config.Limits.x) / range;
            status.limitUsagePercent = Mathf.Abs(normalizedPosition - 0.5f) * 2f; // 0 = center, 1 = at limit
            
            // Check if approaching limits
            bool wasWarning = status.isWarning;
            bool wasCritical = status.isCritical;
            
            status.isWarning = status.limitUsagePercent >= warningThreshold;
            status.isCritical = status.limitUsagePercent >= criticalThreshold;
            
            jointWarnings[i] = status.isWarning;
            jointCritical[i] = status.isCritical;
            
            // Log warnings/critical states (only on state change)
            if (status.isCritical && !wasCritical)
            {
                if (safetyLogger != null)
                {
                    safetyLogger.LogJointLimit(i, status.currentPosition, 
                        status.currentPosition > 0 ? config.Limits.y : config.Limits.x, 
                        status.currentPosition > 0);
                }
                OnJointLimitWarning?.Invoke(i, status.limitUsagePercent);
            }
            else if (status.isWarning && !wasWarning && !status.isCritical)
            {
                OnJointLimitWarning?.Invoke(i, status.limitUsagePercent);
            }
        }
    }
    
    private void MonitorJointSpeeds()
    {
        float deltaTime = speedSampleInterval;
        
        for (int i = 0; i < jointStatuses.Length && i < robot.Joints.Count; i++)
        {
            var joint = robot.Joints[i];
            var config = joint.Config;
            var status = jointStatuses[i];
            
            // Calculate speed (difference in position over time)
            float currentPos = joint.Position.Value;
            float speed = (currentPos - previousJointPositions[i]) / deltaTime;
            previousJointPositions[i] = currentPos;
            
            // Add to speed history for rolling average
            jointSpeedHistory[i].Add(Mathf.Abs(speed));
            if (jointSpeedHistory[i].Count > speedSampleCount)
            {
                jointSpeedHistory[i].RemoveAt(0);
            }
            
            // Calculate average speed
            float avgSpeed = 0f;
            foreach (float s in jointSpeedHistory[i])
            {
                avgSpeed += s;
            }
            avgSpeed /= jointSpeedHistory[i].Count;
            
            status.currentSpeed = avgSpeed;
            status.speedUsagePercent = avgSpeed / config.SpeedMax;
            
            // Check speed limits
            if (status.speedUsagePercent >= criticalThreshold)
            {
                if (safetyLogger != null)
                {
                    safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.JointLimit,
                        $"Joint {i + 1} speed critical",
                        $"Speed: {avgSpeed:F2}, Max: {config.SpeedMax:F2} ({status.speedUsagePercent * 100:F1}%)");
                }
                OnJointSpeedWarning?.Invoke(i, status.speedUsagePercent);
            }
            else if (status.speedUsagePercent >= warningThreshold)
            {
                OnJointSpeedWarning?.Invoke(i, status.speedUsagePercent);
            }
        }
    }
    
    private void MonitorSingularities()
    {
        if (robot.Joints == null || robot.Joints.Count == 0) return;
        
        // Calculate manipulability using axis-based geometric approach
        var singularityResult = DetectSingularityByAxes();
        currentManipulability = singularityResult.manipulability;
        singularityType = singularityResult.type;
        
        bool wasInSingularity = inSingularity;
        inSingularity = currentManipulability < singularityThreshold;
        
        // Get current TCP position from robot forward kinematics
        if (mechanicalGroup != null)
        {
            // Try to get TCP position from the flange or end effector
            var tcp = robot.transform.Find("TCP");
            if (tcp != null)
            {
                currentTCPPosition = tcp.position;
            }
            else
            {
                // Fallback to robot transform position
                currentTCPPosition = robot.transform.position;
            }
        }
        
        // Log singularity events
        if (inSingularity && !wasInSingularity)
        {
            float[] jointAngles = new float[robot.Joints.Count];
            for (int i = 0; i < robot.Joints.Count; i++)
            {
                jointAngles[i] = robot.Joints[i].Position.Value;
            }
            
            if (safetyLogger != null)
            {
                string details = $"TCP Position: {currentTCPPosition}\nManipulability: {currentManipulability:F4}\nType: {singularityType}";
                    
                safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.Singularity,
                    $"Singularity detected: {singularityType}",
                    details);
            }
            
            OnSingularityDetected?.Invoke(currentManipulability, currentTCPPosition);
        }
        else if (!inSingularity && wasInSingularity)
        {
            if (safetyLogger != null)
            {
                string details = $"Manipulability: {currentManipulability:F4}\nPrevious Type: {singularityType}";
                    
                safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.Singularity,
                    "Singularity resolved",
                    details);
            }
            
            singularityType = "None"; // Reset singularity type
            
            OnSingularityResolved?.Invoke();
        }
    }
    
    private (float manipulability, string type) DetectSingularityByAxes()
    {
        if (robot.Joints == null || robot.Joints.Count < 6) 
            return (1.0f, "None");

        // Get rotation axes (Z-axis) for each joint in world coordinates
        Vector3[] jointAxes = new Vector3[robot.Joints.Count];
        axisAlignments = new float[robot.Joints.Count * (robot.Joints.Count - 1) / 2]; // For debug display
        
        for (int i = 0; i < robot.Joints.Count; i++)
        {
            // Get the Z-axis (rotation axis) in world coordinates from joint transform
            var joint = robot.Joints[i];
            if (joint?.transform != null)
            {
                // Use the Z-axis as rotation axis (standard robotics convention)
                jointAxes[i] = joint.transform.TransformDirection(Vector3.forward).normalized;
            }
            else
            {
                jointAxes[i] = Vector3.forward; // Fallback
            }
        }

        // Check for different types of singularities based on axis alignments
        float minManipulability = 1.0f;
        string detectedType = "None";
        int alignmentIndex = 0;

        // Wrist singularity: Check alignment of wrist joints (4, 5, 6) - indices 3, 4, 5
        if (robot.Joints.Count >= 6)
        {
            // Joint 4 and Joint 6 axes alignment (classic wrist singularity)
            float dot46 = Mathf.Abs(Vector3.Dot(jointAxes[3], jointAxes[5]));
            axisAlignments[alignmentIndex++] = dot46;
            
            if (dot46 > wristSingularityThreshold)
            {
                float manipulability = 1.0f - ((dot46 - wristSingularityThreshold) / (1.0f - wristSingularityThreshold)) * 0.95f;
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = "Wrist Singularity (J4-J6 aligned)";
                }
            }

            // Joint 5 near zero (another wrist singularity condition)
            float joint5Angle = Mathf.Abs(robot.Joints[4].Position.Value);
            if (joint5Angle < 10f) // Within 10 degrees of zero
            {
                float manipulability = joint5Angle / 10f; // Scale from 0-1
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = "Wrist Singularity (J5 near zero)";
                }
            }
        }

        // Shoulder singularity: Check alignment of first three joints
        if (robot.Joints.Count >= 3)
        {
            // Joint 1 and Joint 3 alignment
            float dot13 = Mathf.Abs(Vector3.Dot(jointAxes[0], jointAxes[2]));
            if (alignmentIndex < axisAlignments.Length) axisAlignments[alignmentIndex++] = dot13;
            
            if (dot13 > shoulderSingularityThreshold)
            {
                float manipulability = 1.0f - ((dot13 - shoulderSingularityThreshold) / (1.0f - shoulderSingularityThreshold)) * 0.9f;
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = "Shoulder Singularity (J1-J3 aligned)";
                }
            }

            // Joint 2 and Joint 3 alignment
            float dot23 = Mathf.Abs(Vector3.Dot(jointAxes[1], jointAxes[2]));
            if (alignmentIndex < axisAlignments.Length) axisAlignments[alignmentIndex++] = dot23;
            
            if (dot23 > shoulderSingularityThreshold)
            {
                float manipulability = 1.0f - ((dot23 - shoulderSingularityThreshold) / (1.0f - shoulderSingularityThreshold)) * 0.9f;
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = "Shoulder Singularity (J2-J3 aligned)";
                }
            }
        }

        // Elbow singularity: Check for arm fully extended or folded
        if (robot.Joints.Count >= 3)
        {
            float elbowAngle = Mathf.Abs(robot.Joints[2].Position.Value);
            if (elbowAngle < 5f || elbowAngle > 175f)
            {
                float manipulability = elbowAngle < 5f ? elbowAngle / 5f : (180f - elbowAngle) / 5f;
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = elbowAngle < 5f ? "Elbow Singularity (Folded)" : "Elbow Singularity (Extended)";
                }
            }
        }

        // Check general axis alignment between adjacent joints
        for (int i = 0; i < robot.Joints.Count - 1; i++)
        {
            float dotProduct = Mathf.Abs(Vector3.Dot(jointAxes[i], jointAxes[i + 1]));
            if (alignmentIndex < axisAlignments.Length) axisAlignments[alignmentIndex++] = dotProduct;
            
            if (dotProduct > axisAlignmentThreshold)
            {
                float manipulability = 1.0f - ((dotProduct - axisAlignmentThreshold) / (1.0f - axisAlignmentThreshold)) * 0.85f;
                if (manipulability < minManipulability)
                {
                    minManipulability = manipulability;
                    detectedType = $"Axis Alignment Singularity (J{i+1}-J{i+2})";
                }
            }
        }

        return (minManipulability, detectedType);
    }
    
    private void UpdatePositionFormats()
    {
        if (robot?.Joints == null) return;
        
        // Get current joint positions directly from robot joints (works in edit mode)
        float[] jointAngles = new float[6];
        for (int i = 0; i < Mathf.Min(6, robot.Joints.Count); i++)
        {
            jointAngles[i] = robot.Joints[i].Position.Value;
        }
        
        // Get current TCP position and rotation from robot transform
        Vector3 tcpPos = robot.transform.position;
        Quaternion tcpRot = robot.transform.rotation;
        
        // Try to get TCP from flange/end effector if available
        var tcp = robot.transform.Find("TCP");
        if (tcp != null)
        {
            tcpPos = tcp.position;
            tcpRot = tcp.rotation;
        }
        else
        {
            // Try to get from last joint transform
            if (robot.Joints.Count > 0)
            {
                var lastJoint = robot.Joints[robot.Joints.Count - 1];
                if (lastJoint?.transform != null)
                {
                    tcpPos = lastJoint.transform.position;
                    tcpRot = lastJoint.transform.rotation;
                }
            }
        }
        
        // In play mode, try to get more accurate position from ABB controller data
        if (Application.isPlaying)
        {
            var abbData = GetRobTargetFromController();
            if (abbData.hasData)
            {
                tcpPos = abbData.position;
                tcpRot = abbData.rotation;
            }
        }
        
        // Update current TCP position for display
        currentTCPPosition = tcpPos;
        
        // Update JOINTTARGET format
        if (showJointFormat)
        {
            currentJoinTarget = FormatJoinTarget(jointAngles);
        }
        
        // Update ROBTARGET format  
        if (showQuaternionFormat)
        {
            currentRobTarget = FormatRobTarget(tcpPos, tcpRot);
        }
        
        // Log position updates if enabled (only in play mode)
        if (Application.isPlaying && logPositionUpdates && safetyLogger != null)
        {
            string details = "";
            if (showJointFormat) details += $"JOINTTARGET: {currentJoinTarget}\n";
            if (showQuaternionFormat) details += $"ROBTARGET: {currentRobTarget}";
            
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.Position, 
                "Robot Position Update", 
                details.TrimEnd());
        }
    }
    
    private (bool hasData, Vector3 position, Quaternion rotation) GetRobTargetFromController()
    {
        // Try to get position data from Controller.cs ABB_Stream_Data
        try
        {
            // Use fully qualified name to access global class
            var abbStreamDataType = System.Type.GetType("ABB_Stream_Data");
            if (abbStreamDataType != null)
            {
                var positionField = abbStreamDataType.GetField("C_Position", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var orientationField = abbStreamDataType.GetField("C_Orientation", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (positionField != null && orientationField != null)
                {
                    double[] pos = (double[])positionField.GetValue(null);
                    double[] orient = (double[])orientationField.GetValue(null);
                    
                    if (pos != null && pos.Length >= 3 && orient != null && orient.Length >= 4)
                    {
                        // Convert from ABB coordinate system (mm) to Unity (m)
                        Vector3 position = new Vector3((float)pos[0] / 1000f, (float)pos[2] / 1000f, (float)pos[1] / 1000f);
                        Quaternion rotation = new Quaternion((float)orient[0], (float)orient[1], (float)orient[2], (float)orient[3]);
                        
                        return (true, position, rotation);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Robot Safety Monitor] Could not access ABB_Stream_Data: {e.Message}");
        }
        
        return (false, Vector3.zero, Quaternion.identity);
    }
    
    private string FormatJoinTarget(float[] jointAngles)
    {
        if (jointAngles == null || jointAngles.Length < 6) return "JOINTTARGET: Invalid joint data";
        
        // RAPID JOINTTARGET format: [[j1,j2,j3,j4,j5,j6],[external_axis]]
        // Use InvariantCulture to ensure periods for decimal separators
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[[{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2}],[9E9,9E9,9E9,9E9,9E9,9E9]]",
            jointAngles[0], jointAngles[1], jointAngles[2], jointAngles[3], jointAngles[4], jointAngles[5]);
    }
    
    private string FormatRobTarget(Vector3 position, Quaternion rotation)
    {
        // Convert to ABB coordinate system (Unity Y->Z, Z->Y) and mm
        float x = position.x * 1000f;
        float y = position.z * 1000f;  
        float z = position.y * 1000f;
        
        // RAPID ROBTARGET format: [[x,y,z],[q1,q2,q3,q4],[confdata],[external_axis]]
        // Use InvariantCulture to ensure periods for decimal separators
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[[{0:F2},{1:F2},{2:F2}],[{3:F6},{4:F6},{5:F6},{6:F6}],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]]",
            x, y, z, rotation.x, rotation.y, rotation.z, rotation.w);
    }

    
    private float GetOverallJointUsage()
    {
        float totalUsage = 0f;
        for (int i = 0; i < jointStatuses.Length; i++)
        {
            totalUsage += jointStatuses[i].limitUsagePercent;
        }
        return totalUsage / jointStatuses.Length;
    }
    
    // Public methods for external access
    public JointStatus GetJointStatus(int jointIndex)
    {
        if (jointIndex >= 0 && jointIndex < jointStatuses.Length)
            return jointStatuses[jointIndex];
        return null;
    }
    
    public bool IsJointInWarning(int jointIndex)
    {
        return jointIndex >= 0 && jointIndex < jointWarnings.Length && jointWarnings[jointIndex];
    }
    
    public bool IsJointCritical(int jointIndex)
    {
        return jointIndex >= 0 && jointIndex < jointCritical.Length && jointCritical[jointIndex];
    }
    
    public bool HasAnyWarnings()
    {
        for (int i = 0; i < jointWarnings.Length; i++)
        {
            if (jointWarnings[i]) return true;
        }
        return false;
    }
    
    public bool HasAnyCritical()
    {
        for (int i = 0; i < jointCritical.Length; i++)
        {
            if (jointCritical[i]) return true;
        }
        return inSingularity;
    }
    
    // Context menu for testing
    [ContextMenu("Log Current Status")]
    private void LogCurrentStatus()
    {
        if (safetyLogger != null)
        {
            for (int i = 0; i < jointStatuses.Length; i++)
            {
                var status = jointStatuses[i];
                safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.JointLimit,
                    $"Joint {i + 1} Status",
                    $"Position: {status.currentPosition:F2}°, Speed: {status.currentSpeed:F2}°/s, " +
                    $"Limit Usage: {status.limitUsagePercent * 100:F1}%, Speed Usage: {status.speedUsagePercent * 100:F1}%");
            }
            
            string debugDetails = $"Method: Axis-Based Detection\nCurrent: {currentManipulability:F4}\nIn Singularity: {inSingularity}\nType: {singularityType}";
                
            if (axisAlignments != null && axisAlignments.Length > 0)
            {
                debugDetails += "\nAxis Alignments: [" + string.Join(", ", axisAlignments.Select(a => a.ToString("F3"))) + "]";
            }
                
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.Singularity,
                "Manipulability Status",
                debugDetails);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RobotSystem.Core;
using RobotSystem.Interfaces;
using Preliy.Flange;

namespace RobotSystem.Safety
{
    /// <summary>
    /// DH-parameter based singularity detection using Preliy.Flange framework
    /// Detects wrist, shoulder, and elbow singularities for 6R spherical wrist robots
    /// </summary>
    public class SingularityDetectionMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Singularity Detection Settings")]
        [SerializeField] private float wristSingularityThreshold = 10f; // degrees
        [SerializeField] private float shoulderSingularityThreshold = 0.1f; // meters
        [SerializeField] private float elbowSingularityThreshold = 0.01f; // normalized cross product threshold (0-1)
        [SerializeField] private bool checkWristSingularity = true;
        [SerializeField] private bool checkShoulderSingularity = true;
        [SerializeField] private bool checkElbowSingularity = true;
        
        [Header("Robot Configuration")]
        [SerializeField] private Robot6RSphericalWrist robot6R;
        [SerializeField] private bool autoFindRobot = true;
        
        [Header("Debug Settings")]
        [SerializeField] private bool debugLogging = false;
        
        public string MonitorName => "Singularity Detector";
        
        private void DebugLog(string message)
        {
            if (debugLogging) Debug.Log(message);
        }
        
        private void DebugLogWarning(string message)
        {
            if (debugLogging) Debug.LogWarning(message);
        }
        public bool IsActive { get; private set; } = true;
        
        public event Action<SafetyEvent> OnSafetyEventDetected;
        
        private float[] previousJointAngles = new float[6];
        private DateTime lastSingularityTime = DateTime.MinValue;
        private readonly float cooldownTime = 2.0f;
        
        private Frame[] robotFrames;
        private bool isInitialized = false;
        
        // Singularity state tracking
        private Dictionary<string, bool> currentSingularityStates = new Dictionary<string, bool>
        {
            { "Wrist", false },
            { "Shoulder", false },
            { "Elbow", false }
        };
        
        void Awake()
        {
            // Find robot components on main thread
            if (autoFindRobot && robot6R == null)
            {
                robot6R = FindFirstObjectByType<Robot6RSphericalWrist>();
            }
            
            if (robot6R != null)
            {
                robotFrames = robot6R.GetComponentsInChildren<Frame>();
                Array.Sort(robotFrames, (a, b) => GetHierarchyDepth(a.transform).CompareTo(GetHierarchyDepth(b.transform)));
            }
            
            isInitialized = true;
            DebugLog($"[{MonitorName}] Pre-initialized with robot: {(robot6R != null ? robot6R.name : "None")}");
            
            // Subscribe to joint state changes for automatic detection
            if (robot6R != null)
            {
                robot6R.OnJointStateChanged += OnJointStateChanged;
            }
        }
        
        public void Initialize()
        {
            if (!isInitialized)
            {
                DebugLogWarning($"[{MonitorName}] Initialize called but component not properly pre-initialized in Awake");
            }
            else
            {
                DebugLog($"[{MonitorName}] Initialization confirmed with {(robotFrames?.Length ?? 0)} frames");
            }
        }
        
        public void UpdateState(RobotState state)
        {
            if (!IsActive || robot6R == null) return;
            
            // Read joint angles directly from Robot6RSphericalWrist component
            var jointAngles = GetCurrentJointAngles();
            if (jointAngles != null && jointAngles.Length >= 6)
            {
                CheckForSingularities(jointAngles);
                Array.Copy(jointAngles, previousJointAngles, 6);
            }
        }
        
        public void SetActive(bool active)
        {
            IsActive = active;
        }
        
        public void Shutdown()
        {
            IsActive = false;
            
            // Unsubscribe from joint state changes
            if (robot6R != null)
            {
                robot6R.OnJointStateChanged -= OnJointStateChanged;
            }
        }
        
        private void OnJointStateChanged()
        {
            if (!IsActive) return;
            
            // Automatically check for singularities when joints move
            var jointAngles = GetCurrentJointAngles();
            if (jointAngles != null && jointAngles.Length >= 6)
            {
                CheckForSingularities(jointAngles);
            }
        }
        
        private void CheckForSingularities(float[] jointAngles)
        {
            if (robot6R == null || robotFrames == null || robotFrames.Length < 6)
                return;
            
            // Check each singularity type and track state changes
            if (checkWristSingularity)
            {
                bool isInWristSingularity = IsWristSingularityDH(jointAngles);
                CheckSingularityStateChange("Wrist", "Wrist Singularity (θ₅ ≈ 0°)", isInWristSingularity, jointAngles);
            }
            
            if (checkShoulderSingularity)
            {
                bool isInShoulderSingularity = IsShoulderSingularityDH(jointAngles);
                CheckSingularityStateChange("Shoulder", "Shoulder Singularity (Wrist on Y₀)", isInShoulderSingularity, jointAngles);
            }
            
            if (checkElbowSingularity)
            {
                bool isInElbowSingularity = IsElbowSingularityDH(jointAngles);
                CheckSingularityStateChange("Elbow", "Elbow Singularity (J2-J3-J5 Coplanar)", isInElbowSingularity, jointAngles);
            }
        }
        
        private void CheckSingularityStateChange(string singularityType, string description, bool isCurrentlyInSingularity, float[] jointAngles)
        {
            bool wasInSingularity = currentSingularityStates[singularityType];
            
            // State change detected
            if (isCurrentlyInSingularity != wasInSingularity)
            {
                currentSingularityStates[singularityType] = isCurrentlyInSingularity;
                
                if (isCurrentlyInSingularity)
                {
                    // Entering singularity
                    HandleSingularityDetected(description, jointAngles, true);
                }
                else
                {
                    // Exiting singularity
                    HandleSingularityResolved(singularityType, jointAngles);
                }
            }
            // No state change = no event (prevents spam)
        }
        
        private bool IsWristSingularityDH(float[] jointAngles)
        {
            // Wrist singularity: sin(θ₅) = 0, meaning θ₅ = 0° or ±180°
            float theta5_deg = Mathf.Abs(jointAngles[4]);
            return theta5_deg < wristSingularityThreshold || 
                   Mathf.Abs(180f - theta5_deg) < wristSingularityThreshold;
        }
        
        private bool IsShoulderSingularityDH(float[] jointAngles)
        {
            if (robotFrames.Length < 4) return false;
            
            // Calculate wrist center position using forward kinematics
            Vector3 wristCenter = ComputeJointPosition(jointAngles, 5);
            
            // Shoulder singularity: wrist center lies on Y₀ axis (base rotation axis in Unity)
            // Y-axis is typically the vertical/rotation axis for base joint
            Vector3 basePosition = robotFrames[0].transform.position;
            Vector3 wristToBase = wristCenter - basePosition;
            
            // Project onto XZ plane (perpendicular to Y₀ in Unity coordinate system)
            float distanceFromY0 = Mathf.Sqrt(wristToBase.x * wristToBase.x + wristToBase.z * wristToBase.z);
            
            return distanceFromY0 < shoulderSingularityThreshold;
        }
        
        private bool IsElbowSingularityDH(float[] jointAngles)
        {
            if (robotFrames.Length < 4) return false;
            
            // Elbow singularity: wrist center lies on the same plane as joints 2 and 3
            // This happens when the arm is fully extended or fully retracted
            
            try
            {
                // Calculate positions of joint 2, joint 3, and joint 5 (wrist center) using forward kinematics
                Vector3 joint2Position = ComputeJointPosition(jointAngles, 2);  // Joint 2 position 
                Vector3 joint3Position = ComputeJointPosition(jointAngles, 3);  // Joint 3 position
                Vector3 joint5Position = ComputeJointPosition(jointAngles, 5);  // Joint 5 position
                
                
                // Check if the three points are coplanar (lie on the same plane)
                // Elbow singularity: shoulder, elbow, and wrist center coplanar
                return ArePointsCoplanar(joint2Position, joint3Position, joint5Position, elbowSingularityThreshold);
            }
            catch (System.Exception e)
            {
                DebugLogWarning($"[{MonitorName}] Elbow singularity calculation failed: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if three points are coplanar (lie on the same plane) within a given threshold
        /// Uses cross product to detect when joints 2, 3, und 5 (Shoulder, Elbow, Wrist Center) are coplanar
        /// </summary>
        private bool ArePointsCoplanar(Vector3 p1, Vector3 p2, Vector3 p3, float threshold)
        {
            // Calculate vectors from first point to the other two
            Vector3 v1 = p2 - p1; // Shoulder to Elbow
            Vector3 v2 = p3 - p1; // Shoulder to Wrist Center
            
            // If any vector is near zero, points might be coincident
            if (v1.magnitude < 0.001f || v2.magnitude < 0.001f)
                return true;
                
            // Use cross product to check coplanarity
            // When three points are coplanar (on same line/plane), the cross product magnitude approaches zero
            Vector3 crossProduct = Vector3.Cross(v1, v2);
            float crossMagnitude = crossProduct.magnitude;
            
            // Normalize by the magnitudes of the input vectors to get a relative measure
            float normalizedCross = crossMagnitude / (v1.magnitude * v2.magnitude);
            
            // Points are coplanar if normalized cross product is below threshold
            return normalizedCross < threshold;
        }
        
        private Vector3 ComputeJointPosition(float[] jointAngles, int jointIndex)
        {
            // Use forward kinematics to compute position after applying jointIndex transformations
            
            Matrix4x4 baseTransform = Matrix4x4.identity;
            
            // Apply joint transformations to get to desired joint position
            // To get joint N position, apply transformations 0 to N-1
            for (int i = 0; i < jointIndex - 1 && i < robotFrames.Length - 1; i++)
            {
                var frame = robotFrames[i + 1]; // Frame i+1 corresponds to joint i
                var config = frame.Config;
                
                // Create transformation matrix using DH parameters
                float theta = jointAngles[i] * Mathf.Deg2Rad + config.Theta;
                Matrix4x4 dhTransform = HomogeneousMatrix.CreateRaw(new FrameConfig(
                    config.Alpha, config.A, config.D, theta
                ));
                
                baseTransform = baseTransform * dhTransform;
            }
            
            return baseTransform.GetPosition();
        }
        
        private int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            Transform current = transform;
            while (current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }
        
        private float[] GetCurrentJointAngles()
        {
            if (robot6R == null || robot6R.Joints.Count < 6) return null;
            
            var jointAngles = new float[6];
            for (int i = 0; i < 6; i++)
            {
                // Get joint position value from TransformJoint
                jointAngles[i] = robot6R[i]; // Uses MechanicalUnit indexer
            }
            return jointAngles;
        }
        
        // Method to manually trigger singularity check (for testing)
        public void CheckSingularitiesNow()
        {
            if (!IsActive || robot6R == null) return;
            
            var jointAngles = GetCurrentJointAngles();
            if (jointAngles != null && jointAngles.Length >= 6)
            {
                DebugLog($"[{MonitorName}] Manual check - Joint angles: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]");
                CheckForSingularities(jointAngles);
            }
        }
        
        private void HandleSingularityDetected(string singularityType, float[] jointAngles, bool entering = true)
        {
            lastSingularityTime = DateTime.Now;
            
            var singularityData = new SingularityInfo
            {
                singularityType = singularityType,
                jointAngles = (float[])jointAngles.Clone(),
                wristThreshold = wristSingularityThreshold,
                shoulderThreshold = shoulderSingularityThreshold,
                elbowThreshold = elbowSingularityThreshold,
                isEntering = entering
            };
            
            string eventDescription = entering ?
                $"ENTERING {singularityType} at joint configuration: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]°" :
                $"EXITING {singularityType} at joint configuration: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]°";
            
            // Create safety event - safety manager will provide robot state
            var safetyEvent = new SafetyEvent(
                MonitorName,
                entering ? SafetyEventType.Warning : SafetyEventType.Info,
                eventDescription,
                null // Safety manager will provide robot state
            );
            
            // Add singularity-specific data
            safetyEvent.SetEventData(singularityData);
            
            // Trigger event
            OnSafetyEventDetected?.Invoke(safetyEvent);
        }
        
        private void HandleSingularityResolved(string singularityType, float[] jointAngles)
        {
            HandleSingularityDetected($"{singularityType} Resolved", jointAngles, false);
        }
    }
    
    [Serializable]
    public class SingularityInfo
    {
        public string singularityType;
        public float[] jointAngles;
        public float wristThreshold;
        public float shoulderThreshold;
        public float elbowThreshold;
        public DateTime detectionTime = DateTime.Now;
        public string dhAnalysis;
        public bool isEntering = true; // true = entering singularity, false = exiting
        
        public SingularityInfo()
        {
            dhAnalysis = "DH-parameter based detection using Preliy.Flange framework (Unity Y-up coordinate system)";
        }
    }
}
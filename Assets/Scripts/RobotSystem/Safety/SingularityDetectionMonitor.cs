using System;
using UnityEngine;
using RobotSystem.Core;
using RobotSystem.Interfaces;

namespace RobotSystem.Safety
{
    /// <summary>
    /// Singularity detection safety monitor for 6-DOF robots
    /// </summary>
    public class SingularityDetectionMonitor : MonoBehaviour, IRobotSafetyMonitor
    {
        [Header("Singularity Detection Settings")]
        [SerializeField] private float singularityThreshold = 0.1f;
        [SerializeField] private bool checkWristSingularity = true;
        [SerializeField] private bool checkShoulderSingularity = true;
        [SerializeField] private bool checkElbowSingularity = true;
        
        public string MonitorName => "Singularity Detector";
        public bool IsActive { get; private set; } = true;
        
        public event Action<SafetyEvent> OnSafetyEventDetected;
        
        private float[] previousJointAngles = new float[6];
        private DateTime lastSingularityTime = DateTime.MinValue;
        private readonly float cooldownTime = 2.0f;
        
        private bool isInitialized = false;
        
        void Awake()
        {
            // Pre-initialize on main thread
            isInitialized = true;
            Debug.Log($"[{MonitorName}] Pre-initialized with threshold: {singularityThreshold}");
        }
        
        public void Initialize()
        {
            // This method is now called from background threads, so we just verify initialization
            if (!isInitialized)
            {
                Debug.LogWarning($"[{MonitorName}] Initialize called but component not properly pre-initialized in Awake");
            }
            else
            {
                Debug.Log($"[{MonitorName}] Initialization confirmed with threshold: {singularityThreshold}");
            }
        }
        
        public void UpdateState(RobotState state)
        {
            if (!IsActive || state == null || !state.hasValidJointData) return;
            
            var jointAngles = state.GetJointAngles();
            if (jointAngles.Length >= 6)
            {
                CheckForSingularities(jointAngles, state);
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
        }
        
        private void CheckForSingularities(float[] jointAngles, RobotState state)
        {
            // Prevent singularity spam
            if ((DateTime.Now - lastSingularityTime).TotalSeconds < cooldownTime)
                return;
            
            if (checkWristSingularity && IsWristSingularity(jointAngles))
            {
                HandleSingularityDetected("Wrist Singularity", jointAngles, state);
            }
            else if (checkShoulderSingularity && IsShoulderSingularity(jointAngles))
            {
                HandleSingularityDetected("Shoulder Singularity", jointAngles, state);
            }
            else if (checkElbowSingularity && IsElbowSingularity(jointAngles))
            {
                HandleSingularityDetected("Elbow Singularity", jointAngles, state);
            }
        }
        
        private bool IsWristSingularity(float[] joints)
        {
            // Wrist singularity occurs when J5 (wrist bend) is near 0° or 180°
            float j5_deg = Mathf.Abs(joints[4]);
            return Mathf.Abs(j5_deg) < singularityThreshold || 
                   Mathf.Abs(180f - j5_deg) < singularityThreshold;
        }
        
        private bool IsShoulderSingularity(float[] joints)
        {
            // Shoulder singularity occurs when the robot is fully extended
            // This is a simplified check - in reality, this would involve forward kinematics
            float j2_deg = joints[1];
            float j3_deg = joints[2];
            
            // Check if arm is nearly straight (J2 + J3 ≈ 0)
            return Mathf.Abs(j2_deg + j3_deg) < singularityThreshold;
        }
        
        private bool IsElbowSingularity(float[] joints)
        {
            // Elbow singularity occurs when J3 (elbow) is near 0°
            float j3_deg = Mathf.Abs(joints[2]);
            return j3_deg < singularityThreshold;
        }
        
        private void HandleSingularityDetected(string singularityType, float[] jointAngles, RobotState state)
        {
            lastSingularityTime = DateTime.Now;
            
            var singularityData = new SingularityInfo
            {
                singularityType = singularityType,
                jointAngles = (float[])jointAngles.Clone(),
                threshold = singularityThreshold
            };
            
            // Create safety event
            var safetyEvent = new SafetyEvent(
                MonitorName,
                SafetyEventType.Warning,
                $"{singularityType} detected at joint configuration: [{string.Join(", ", Array.ConvertAll(jointAngles, x => x.ToString("F1")))}]°",
                state
            );
            
            // Add singularity-specific data
            safetyEvent.SetEventData(singularityData);
            
            // Trigger event
            OnSafetyEventDetected?.Invoke(safetyEvent);
        }
    }
    
    [Serializable]
    public class SingularityInfo
    {
        public string singularityType;
        public float[] jointAngles;
        public float threshold;
        public DateTime detectionTime = DateTime.Now;
    }
}
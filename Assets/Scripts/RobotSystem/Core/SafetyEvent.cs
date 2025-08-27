using System;
using UnityEngine;

namespace RobotSystem.Core
{
    /// <summary>
    /// Represents a safety event detected by a safety monitor
    /// </summary>
    [Serializable]
    public class SafetyEvent
    {
        [Header("Event Info")]
        public string monitorName = "";
        public SafetyEventType eventType = SafetyEventType.Warning;
        public DateTime timestamp = DateTime.Now;
        public string description = "";
        
        [Header("Robot State Snapshot")]
        public RobotStateSnapshot robotStateSnapshot;
        
        [Header("Event Specific Data")]
        public object eventData = null;
        
        public SafetyEvent(string monitorName, SafetyEventType eventType, string description, RobotState currentState)
        {
            this.monitorName = monitorName;
            this.eventType = eventType;
            this.description = description;
            this.timestamp = DateTime.Now;
            this.robotStateSnapshot = currentState != null ? new RobotStateSnapshot(currentState) : null;
        }
        
        /// <summary>
        /// Set event-specific data (e.g., collision points, singularity details)
        /// </summary>
        public void SetEventData<T>(T data)
        {
            eventData = data;
        }
        
        /// <summary>
        /// Get event-specific data
        /// </summary>
        public T GetEventData<T>() where T : new()
        {
            try
            {
                return eventData is T ? (T)eventData : new T();
            }
            catch
            {
                return new T();
            }
        }
        
        /// <summary>
        /// Convert to JSON format for logging
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }
    
    public enum SafetyEventType
    {
        Info,
        Warning,
        Critical,
        Emergency
    }
}
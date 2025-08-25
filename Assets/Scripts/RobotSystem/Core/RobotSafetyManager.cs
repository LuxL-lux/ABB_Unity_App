using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RobotSystem.Interfaces;

namespace RobotSystem.Core
{
    public class RobotSafetyManager : MonoBehaviour
    {
        [Header("Safety Monitors")]
        [SerializeField] private List<MonoBehaviour> safetyMonitorComponents = new List<MonoBehaviour>();
        
        [Header("Logging Settings")]
        [SerializeField] private bool enableJsonLogging = true;
        [SerializeField] private bool logOnlyWhenProgramRunning = true;
        [SerializeField] private string logDirectory = "SafetyLogs";
        [SerializeField] private SafetyEventType minimumLogLevel = SafetyEventType.Warning;
        
        private List<IRobotSafetyMonitor> safetyMonitors = new List<IRobotSafetyMonitor>();
        private RobotManager robotManager;
        private RobotState lastKnownState;
        
        public event Action<SafetyEvent> OnSafetyEventDetected;
        
        void Start()
        {
            InitializeSafetyMonitors();
            
            // Find and subscribe to robot manager
            robotManager = FindFirstObjectByType<RobotManager>();
            if (robotManager != null)
            {
                robotManager.OnStateUpdated += OnRobotStateUpdated;
            }
            else
            {
                Debug.LogWarning("[Safety Manager] RobotManager not found. Safety monitors will not receive state updates.");
            }
            
            // Ensure log directory exists
            if (enableJsonLogging)
            {
                string fullLogPath = Path.Combine(Application.persistentDataPath, logDirectory);
                if (!Directory.Exists(fullLogPath))
                {
                    Directory.CreateDirectory(fullLogPath);
                }
            }
        }
        
        
        private void InitializeSafetyMonitors()
        {
            safetyMonitors.Clear();
            
            // Convert MonoBehaviour components to IRobotSafetyMonitor interfaces
            foreach (var component in safetyMonitorComponents)
            {
                if (component != null && component is IRobotSafetyMonitor monitor)
                {
                    try
                    {
                        monitor.Initialize();
                        monitor.OnSafetyEventDetected += OnSafetyEventOccurred;
                        safetyMonitors.Add(monitor);
                        
                        Debug.Log($"[Safety Manager] Initialized safety monitor: {monitor.MonitorName}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Safety Manager] Failed to initialize safety monitor {monitor.MonitorName}: {e.Message}");
                    }
                }
                else if (component != null)
                {
                    Debug.LogWarning($"[Safety Manager] Component {component.name} does not implement IRobotSafetyMonitor interface");
                }
            }
            
            Debug.Log($"[Safety Manager] Initialized {safetyMonitors.Count} safety monitors");
        }
        
        private void OnRobotStateUpdated(RobotState state)
        {
            lastKnownState = state;
            
            foreach (var monitor in safetyMonitors)
            {
                if (monitor.IsActive)
                {
                    try
                    {
                        monitor.UpdateState(state);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Safety Manager] Error updating monitor {monitor.MonitorName}: {e.Message}");
                    }
                }
            }
        }
        
        private void OnSafetyEventOccurred(SafetyEvent safetyEvent)
        {
            OnSafetyEventDetected?.Invoke(safetyEvent);
            
            bool shouldLogToJson = enableJsonLogging && 
                                 safetyEvent.eventType >= minimumLogLevel &&
                                 (!logOnlyWhenProgramRunning || safetyEvent.robotStateSnapshot.isProgramRunning);
            
            if (shouldLogToJson)
            {
                LogSafetyEventToFile(safetyEvent);
            }
            else
            {
                LogSafetyEventToConsole(safetyEvent);
            }
        }
        
        private void LogSafetyEventToFile(SafetyEvent safetyEvent)
        {
            try
            {
                string fileName = $"Safety_{safetyEvent.monitorName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json";
                string fullPath = Path.Combine(Application.persistentDataPath, logDirectory, fileName);
                
                string jsonContent = safetyEvent.ToJson();
                File.WriteAllText(fullPath, jsonContent);
                
                Debug.Log($"[Safety Manager] {safetyEvent.eventType} - {safetyEvent.monitorName}: {safetyEvent.description} [Logged to: {fileName}]");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Safety Manager] Failed to log safety event to file: {e.Message}");
                LogSafetyEventToConsole(safetyEvent);
            }
        }
        
        private void LogSafetyEventToConsole(SafetyEvent safetyEvent)
        {
            string logLevel = safetyEvent.eventType.ToString().ToUpper();
            string programContext = safetyEvent.robotStateSnapshot.GetProgramContext();
            
            Debug.Log($"[Safety Manager] {logLevel} - {safetyEvent.monitorName}: {safetyEvent.description} | Program: {programContext}");
        }
        
        public void SetMonitorActive(string monitorName, bool active)
        {
            foreach (var monitor in safetyMonitors)
            {
                if (monitor.MonitorName == monitorName)
                {
                    monitor.SetActive(active);
                    Debug.Log($"[Safety Manager] {monitorName} monitor {(active ? "enabled" : "disabled")}");
                    break;
                }
            }
        }
        
        public List<string> GetActiveMonitors()
        {
            var activeMonitors = new List<string>();
            foreach (var monitor in safetyMonitors)
            {
                if (monitor.IsActive)
                {
                    activeMonitors.Add(monitor.MonitorName);
                }
            }
            return activeMonitors;
        }
        
        void OnDestroy()
        {
            if (robotManager != null)
            {
                robotManager.OnStateUpdated -= OnRobotStateUpdated;
            }
            
            foreach (var monitor in safetyMonitors)
            {
                try
                {
                    monitor.OnSafetyEventDetected -= OnSafetyEventOccurred;
                    monitor.Shutdown();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Safety Manager] Error shutting down monitor {monitor.MonitorName}: {e.Message}");
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using RobotSystem.Interfaces;

namespace RobotSystem.Core
{
    public class RobotManager : MonoBehaviour
    {
        // Public event for external components to subscribe to state updates
        public event Action<RobotState> OnStateUpdated;
        
        [Header("Robot Connector")]
        [SerializeField] private MonoBehaviour connectorComponent;

        [Header("Visualization Systems")]
        [SerializeField] private List<MonoBehaviour> visualizationComponents = new List<MonoBehaviour>();

        private IRobotConnector robotConnector;
        private List<IRobotVisualization> visualizers = new List<IRobotVisualization>();

        [Header("Status")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private string currentProgram = "";
        [SerializeField] private bool gripperOpen = false;
        [SerializeField] private float[] currentJointAngles = new float[6];
        [SerializeField] private double motionUpdateFreq = 0.0;

        void Start()
        {
            if (connectorComponent != null)
            {
                robotConnector = connectorComponent as IRobotConnector;
                if (robotConnector != null)
                {
                    // Subscribe to events
                    robotConnector.OnConnectionStateChanged += OnConnectionChanged;
                    robotConnector.OnRobotStateUpdated += OnRobotStateUpdated;

                }
                else
                {
                    Debug.LogError($"Component {connectorComponent.GetType().Name} does not implement IRobotConnector");
                }
            }

            // Initialize visualization systems
            InitializeVisualizationSystems();
        }

        void OnDestroy()
        {
            if (robotConnector != null)
            {
                robotConnector.OnConnectionStateChanged -= OnConnectionChanged;
                robotConnector.OnRobotStateUpdated -= OnRobotStateUpdated;
            }

            // Shutdown visualization systems
            foreach (var visualizer in visualizers)
            {
                visualizer.Shutdown();
            }
        }

        private void OnConnectionChanged(bool connected)
        {
            isConnected = connected;
        }

        private void OnRobotStateUpdated(RobotState state)
        {
            // Update UI/status variables
            currentProgram = $"{state.currentModule}.{state.currentRoutine}:{state.currentLine}";
            gripperOpen = state.GripperOpen;
            
            // Trigger public event for external subscribers
            OnStateUpdated?.Invoke(state);

            // Update motion data
            if (state.hasValidJointData)
            {
                currentJointAngles = state.GetJointAngles();
                motionUpdateFreq = state.motionUpdateFrequencyHz;

                // Forward joint angles to all visualization systems
                foreach (var visualizer in visualizers)
                {
                    if (visualizer.IsConnected && visualizer.IsValid)
                    {
                        visualizer.UpdateJointAngles(currentJointAngles);
                    }
                }
            }

            if (state.isRunning && !string.IsNullOrEmpty(state.currentRoutine))
            {
                // Robot is executing a program
            }

            if (state.GripperOpen != gripperOpen)
            {
                // Gripper state changed
                // Debug.Log($"Gripper: {(state.GripperOpen ? "OPENED" : "CLOSED")}");
            }
        }

        // Public methods that work with any robot type
        public void ConnectToRobot()
        {
            robotConnector?.Connect();
        }

        public void DisconnectFromRobot()
        {
            robotConnector?.Disconnect();
        }

        public RobotState GetCurrentState()
        {
            return robotConnector?.CurrentState;
        }

        public bool IsRobotConnected()
        {
            return robotConnector?.IsConnected ?? false;
        }

        // Convenience methods for joint data access
        public float[] GetCurrentJointAngles()
        {
            return GetCurrentState()?.GetJointAngles() ?? new float[6];
        }

        public bool HasValidMotionData()
        {
            return GetCurrentState()?.hasValidJointData ?? false;
        }

        public double GetMotionUpdateFrequency()
        {
            return GetCurrentState()?.motionUpdateFrequencyHz ?? 0.0;
        }

        private void InitializeVisualizationSystems()
        {
            visualizers.Clear();

            foreach (var component in visualizationComponents)
            {
                if (component != null)
                {
                    var visualizer = component as IRobotVisualization;
                    if (visualizer != null)
                    {
                        visualizer.Initialize();
                        visualizers.Add(visualizer);
                    }
                    else
                    {
                        Debug.LogError($"Component {component.GetType().Name} does not implement IRobotVisualization");
                    }
                }
            }
        }

        public void AddVisualizationSystem(IRobotVisualization visualizer)
        {
            if (visualizer != null && !visualizers.Contains(visualizer))
            {
                visualizer.Initialize();
                visualizers.Add(visualizer);
            }
        }

        public void RemoveVisualizationSystem(IRobotVisualization visualizer)
        {
            if (visualizer != null && visualizers.Contains(visualizer))
            {
                visualizer.Shutdown();
                visualizers.Remove(visualizer);
            }
        }

        public List<IRobotVisualization> GetVisualizationSystems()
        {
            return new List<IRobotVisualization>(visualizers);
        }
    }
}
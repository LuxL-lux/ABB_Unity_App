/****************************************************************************
Example usage of ABB Robot Web Services Controller
****************************************************************************/

using UnityEngine;
using Preliy.Flange;

[RequireComponent(typeof(ABBRobotWebServicesController))]
public class ABBRobotExample : MonoBehaviour
{
    [Header("Example Settings")]
    [SerializeField] private bool logJointUpdates = false;
    [SerializeField] private bool showGUI = true;
    
    private ABBRobotWebServicesController abbController;
    private Controller flangeController;
    
    // Statistics
    private int updateCount = 0;
    private float[] lastJointAngles = new float[6];
    
    private void Awake()
    {
        abbController = GetComponent<ABBRobotWebServicesController>();
        flangeController = GetComponent<Controller>();
        
        // Subscribe to events
        abbController.OnConnected += HandleConnected;
        abbController.OnDisconnected += HandleDisconnected;
        abbController.OnJointDataReceived += HandleJointDataReceived;
        abbController.OnError += HandleError;
        abbController.OnRapidStatusChanged += HandleRapidStatusChanged;
        abbController.OnJointLimitWarning += HandleJointLimitWarning;
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (abbController != null)
        {
            abbController.OnConnected -= HandleConnected;
            abbController.OnDisconnected -= HandleDisconnected;
            abbController.OnJointDataReceived -= HandleJointDataReceived;
            abbController.OnError -= HandleError;
            abbController.OnRapidStatusChanged -= HandleRapidStatusChanged;
            abbController.OnJointLimitWarning -= HandleJointLimitWarning;
        }
    }
    
    private void HandleConnected()
    {
        Debug.Log("[ABB Example] Robot connected successfully!");
        updateCount = 0;
    }
    
    private void HandleDisconnected()
    {
        Debug.Log("[ABB Example] Robot disconnected.");
    }
    
    private void HandleJointDataReceived(float[] jointAngles)
    {
        updateCount++;
        lastJointAngles = (float[])jointAngles.Clone();
        
        if (logJointUpdates)
        {
            Debug.Log($"[ABB Example] Joint update #{updateCount}: [{string.Join(", ", System.Array.ConvertAll(jointAngles, x => x.ToString("F2")))}]");
        }
        
        // You can add custom logic here to process joint data
        // For example: collision detection, workspace validation, etc.
    }
    
    private void HandleError(string errorMessage)
    {
        Debug.LogError($"[ABB Example] Error occurred: {errorMessage}");
    }
    
    private void HandleRapidStatusChanged(string statusMessage)
    {
        Debug.Log($"[ABB Example] RAPID Status Changed: {statusMessage}");
    }
    
    private void HandleJointLimitWarning(int jointIndex, bool isWarning)
    {
        if (isWarning)
        {
            Debug.LogWarning($"[ABB Example] Joint {jointIndex + 1} approaching limit!");
        }
        else
        {
            Debug.Log($"[ABB Example] Joint {jointIndex + 1} limit warning cleared.");
        }
    }
    
    // Simple GUI for testing
    private void OnGUI()
    {
        if (!showGUI) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 400));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("ABB Robot Web Services", GUI.skin.GetStyle("label"));
        GUILayout.Space(10);
        
        // Connection status
        GUILayout.Label($"Status: {abbController.Status}");
        GUILayout.Label($"Connected: {abbController.IsConnected}");
        GUILayout.Label($"Update Rate: {abbController.UpdateFrequency:F1} Hz");
        GUILayout.Label($"Updates Received: {updateCount}");
        
        GUILayout.Space(10);
        
        // Control buttons
        if (Application.isPlaying)
        {
            if (abbController.IsConnected)
            {
                if (GUILayout.Button("Disconnect"))
                {
                    abbController.StopConnection();
                }
            }
            else
            {
                if (GUILayout.Button("Connect"))
                {
                    abbController.StartConnection();
                }
            }
            
            if (GUILayout.Button("Test Connection"))
            {
                abbController.TestConnection();
            }
        }
        
        GUILayout.Space(10);
        
        // Joint angles display with limit warnings
        if (abbController.IsConnected)
        {
            GUILayout.Label("Joint Angles & Limits:");
            var limitWarnings = abbController.JointLimitWarnings;
            var limitPercentages = abbController.JointLimitPercentages;
            var minLimits = abbController.JointMinLimits;
            var maxLimits = abbController.JointMaxLimits;
            
            for (int i = 0; i < lastJointAngles.Length; i++)
            {
                Color originalColor = GUI.color;
                if (i < limitWarnings.Length && limitWarnings[i])
                {
                    GUI.color = Color.red;
                    GUILayout.Label($"J{i + 1}: {lastJointAngles[i]:F2}° [LIMIT WARNING!]");
                }
                else if (i < limitPercentages.Length && limitPercentages[i] > 80f)
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label($"J{i + 1}: {lastJointAngles[i]:F2}° [{limitPercentages[i]:F0}%]");
                }
                else
                {
                    GUILayout.Label($"J{i + 1}: {lastJointAngles[i]:F2}°");
                }
                
                // Display limit range
                if (i < minLimits.Length && i < maxLimits.Length)
                {
                    GUI.color = Color.gray;
                    GUILayout.Label($"    Range: {minLimits[i]:F1}° to {maxLimits[i]:F1}°", GUI.skin.GetStyle("label"));
                }
                
                GUI.color = originalColor;
            }
            
            GUILayout.Space(5);
            
            // RAPID Program Status
            GUILayout.Label("RAPID Program Status:");
            GUILayout.Label($"Program: {abbController.CurrentProgramName}");
            GUILayout.Label($"Execution: {abbController.ExecutionState}");
            GUILayout.Label($"Mode: {abbController.OperationMode}");
            GUILayout.Label($"Controller: {abbController.ControllerState}");
        }
        
        // Flange controller status
        GUILayout.Space(10);
        if (flangeController != null)
        {
            GUILayout.Label($"Flange Controller Valid: {flangeController.IsValid.Value}");
            
            if (flangeController.IsValid.Value)
            {
                var tcp = flangeController.PoseObserver.ToolCenterPointWorld.Value;
                GUILayout.Label($"TCP Position: ({tcp.m03:F2}, {tcp.m13:F2}, {tcp.m23:F2})");
                
                // Joint limit status
                Color originalColor = GUI.color;
                if (abbController.JointLimitStatus.Contains("WARNING"))
                {
                    GUI.color = Color.red;
                }
                else
                {
                    GUI.color = Color.green;
                }
                GUILayout.Label($"Joint Status: {abbController.JointLimitStatus}");
                GUI.color = originalColor;
            }
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
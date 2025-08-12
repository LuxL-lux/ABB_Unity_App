/****************************************************************************
Custom Inspector for ABB Robot Web Services Controller
****************************************************************************/

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;

[CustomEditor(typeof(ABBRobotWebServicesController))]
public class ABBRobotWebServicesControllerEditor : Editor
{
    private ABBRobotWebServicesController controller;
    private GUIStyle headerStyle;
    private GUIStyle statusStyle;
    private bool showAdvancedSettings = false;
    
    private void OnEnable()
    {
        controller = (ABBRobotWebServicesController)target;
    }
    
    public override void OnInspectorGUI()
    {
        InitializeStyles();
        
        serializedObject.Update();
        
        DrawHeader();
        EditorGUILayout.Space(10);
        
        DrawConnectionSettings();
        EditorGUILayout.Space(5);
        
        DrawDataSettings();
        EditorGUILayout.Space(5);
        
        DrawDebugSettings();
        EditorGUILayout.Space(10);
        
        DrawStatusSection();
        EditorGUILayout.Space(10);
        
        DrawControlButtons();
        
        serializedObject.ApplyModifiedProperties();
        
        // Repaint to update status in real-time
        if (Application.isPlaying && controller.IsConnected)
        {
            Repaint();
        }
    }
    
    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
        }
        
        if (statusStyle == null)
        {
            statusStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };
        }
    }
    
    private new void DrawHeader()
    {
        EditorGUILayout.LabelField("ABB Robot Web Services Controller", headerStyle);
        
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Controller will start automatically when entering Play Mode (if Auto Start is enabled).", MessageType.Info);
        }
    }
    
    private void DrawConnectionSettings()
    {
        EditorGUILayout.LabelField("Connection Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ipAddress"), new GUIContent("IP Address"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("port"), new GUIContent("Port"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("username"), new GUIContent("Username"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("password"), new GUIContent("Password"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("taskName"), new GUIContent("Task Name"));
        
        // Validation
        if (string.IsNullOrWhiteSpace(controller.GetType().GetField("ipAddress", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(controller)?.ToString()))
        {
            EditorGUILayout.HelpBox("IP Address is required!", MessageType.Warning);
        }
    }
    
    private void DrawDataSettings()
    {
        EditorGUILayout.LabelField("Data Update Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pollingIntervalMs"), new GUIContent("Polling Interval (ms)"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useWebSocketWhenAvailable"), new GUIContent("Use WebSocket When Available"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoStartOnEnable"), new GUIContent("Auto Start On Enable"));
        
        if (serializedObject.FindProperty("pollingIntervalMs").intValue < 10)
        {
            EditorGUILayout.HelpBox("Warning: Very low polling intervals may impact performance!", MessageType.Warning);
        }
    }
    
    private void DrawDebugSettings()
    {
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Debug Settings", true);
        
        if (showAdvancedSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDebugLogging"), new GUIContent("Enable Debug Logging"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showPerformanceMetrics"), new GUIContent("Show Performance Metrics"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("logJointUpdates"), new GUIContent("Log Joint Updates"));
            EditorGUI.indentLevel--;
        }
    }
    
    private void DrawStatusSection()
    {
        EditorGUILayout.LabelField("Status Information", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginVertical(statusStyle);
        
        // Connection Status with color coding
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Connection Status:", GUILayout.Width(150));
        
        Color originalColor = GUI.color;
        switch (controller.Status)
        {
            case ABBRobotWebServicesController.ConnectionStatus.Connected:
                GUI.color = Color.green;
                break;
            case ABBRobotWebServicesController.ConnectionStatus.Connecting:
                GUI.color = Color.yellow;
                break;
            case ABBRobotWebServicesController.ConnectionStatus.Error:
                GUI.color = Color.red;
                break;
            default:
                GUI.color = Color.gray;
                break;
        }
        
        EditorGUILayout.LabelField(controller.Status.ToString(), EditorStyles.boldLabel);
        GUI.color = originalColor;
        GUILayout.EndHorizontal();
        
        // Other status information
        EditorGUILayout.LabelField($"Using WebSocket: {(controller.IsConnected ? (serializedObject.FindProperty("isUsingWebSocket").boolValue ? "Yes" : "No") : "N/A")}");
        
        if (controller.IsConnected)
        {
            EditorGUILayout.LabelField($"Update Frequency: {controller.UpdateFrequency:F1} Hz");
            EditorGUILayout.LabelField($"Total Updates: {serializedObject.FindProperty("totalUpdatesReceived").intValue}");
            
            // RAPID Program Status
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("RAPID Program Status:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  Program: {controller.CurrentProgramName}");
            
            // Color-code execution state
            Color statusColor = GUI.color;
            if (controller.ExecutionState.ToLower().Contains("running"))
                GUI.color = Color.green;
            else if (controller.ExecutionState.ToLower().Contains("stopped"))
                GUI.color = Color.red;
            else if (controller.ExecutionState.ToLower().Contains("ready"))
                GUI.color = Color.yellow;
            
            EditorGUILayout.LabelField($"  Execution: {controller.ExecutionState}", EditorStyles.boldLabel);
            GUI.color = statusColor;
            
            EditorGUILayout.LabelField($"  Mode: {controller.OperationMode}");
            EditorGUILayout.LabelField($"  Controller: {controller.ControllerState}");
            
            // Current joint angles with limit warnings
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Joint Angles & Limits:", EditorStyles.boldLabel);
            
            var jointAngles = controller.JointAngles;
            var limitWarnings = controller.JointLimitWarnings;
            var limitPercentages = controller.JointLimitPercentages;
            var minLimits = controller.JointMinLimits;
            var maxLimits = controller.JointMaxLimits;
            
            for (int i = 0; i < jointAngles.Length; i++)
            {
                GUILayout.BeginVertical("box");
                
                // Joint header with current angle
                GUILayout.BeginHorizontal();
                
                // Color-code joints approaching limits
                Color jointColor = GUI.color;
                if (i < limitWarnings.Length && limitWarnings[i])
                {
                    GUI.color = Color.red;
                }
                else if (i < limitPercentages.Length && limitPercentages[i] > 80f)
                {
                    GUI.color = Color.yellow;
                }
                
                EditorGUILayout.LabelField($"J{i + 1}: {jointAngles[i]:F2}°", EditorStyles.boldLabel, GUILayout.Width(80));
                
                if (i < limitPercentages.Length)
                {
                    EditorGUILayout.LabelField($"({limitPercentages[i]:F0}% of range)", GUILayout.Width(100));
                }
                
                GUI.color = jointColor;
                GUILayout.EndHorizontal();
                
                // Limit range display
                if (i < minLimits.Length && i < maxLimits.Length)
                {
                    EditorGUILayout.LabelField($"  Range: {minLimits[i]:F1}° to {maxLimits[i]:F1}°", EditorStyles.miniLabel);
                    
                    // Progress bar showing position within limits
                    if (maxLimits[i] != minLimits[i])
                    {
                        float normalizedPosition = (jointAngles[i] - minLimits[i]) / (maxLimits[i] - minLimits[i]);
                        normalizedPosition = Mathf.Clamp01(normalizedPosition);
                        
                        Rect progressRect = GUILayoutUtility.GetRect(100, 8);
                        EditorGUI.ProgressBar(progressRect, normalizedPosition, "");
                    }
                }
                
                GUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
            
            // Joint limit status
            EditorGUILayout.Space(5);
            Color limitStatusColor = GUI.color;
            if (controller.JointLimitStatus.Contains("WARNING"))
            {
                GUI.color = Color.red;
                EditorGUILayout.LabelField($"Joint Status: {controller.JointLimitStatus}", EditorStyles.boldLabel);
            }
            else
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField($"Joint Status: {controller.JointLimitStatus}");
            }
            GUI.color = limitStatusColor;
        }
        
        // Last error
        string lastError = serializedObject.FindProperty("lastErrorMessage").stringValue;
        if (!string.IsNullOrEmpty(lastError))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Last Error:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lastError, MessageType.Error);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawControlButtons()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Control buttons are only available in Play Mode", MessageType.Info);
            GUI.enabled = false;
        }
        
        EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
        
        GUILayout.BeginHorizontal();
        
        // Start/Stop button
        if (controller.IsConnected)
        {
            if (GUILayout.Button("Stop Connection", GUILayout.Height(30)))
            {
                controller.StopConnection();
            }
        }
        else
        {
            if (GUILayout.Button("Start Connection", GUILayout.Height(30)))
            {
                controller.StartConnection();
            }
        }
        
        // Test connection button
        if (GUILayout.Button("Test Connection", GUILayout.Height(30)))
        {
            controller.TestConnection();
        }
        
        GUILayout.EndHorizontal();
        
        // Additional info section
        if (Application.isPlaying && controller.IsConnected)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Monitoring Information:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "• RAPID program status updates every 2 seconds\n" +
                "• Joint limit warnings appear when within 5% of limits\n" +
                "• Progress bars show position within joint range\n" +
                "• Yellow: 80%+ of joint range used\n" +
                "• Red: Approaching joint limits",
                MessageType.Info);
        }
        
        GUI.enabled = true;
    }
}
#endif
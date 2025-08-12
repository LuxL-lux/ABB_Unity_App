/****************************************************************************
Custom Inspector for ABB Tool Controller
****************************************************************************/

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;

[CustomEditor(typeof(ABBToolController))]
public class ABBToolControllerEditor : Editor
{
    private ABBToolController toolController;
    private GUIStyle headerStyle;
    private GUIStyle statusStyle;
    private bool showToolSettings = true;
    private bool showIOSettings = false;
    private bool showRapidSettings = false;
    
    private void OnEnable()
    {
        toolController = (ABBToolController)target;
    }
    
    public override void OnInspectorGUI()
    {
        InitializeStyles();
        
        serializedObject.Update();
        
        DrawHeader();
        EditorGUILayout.Space(10);
        
        DrawToolConfiguration();
        EditorGUILayout.Space(5);
        
        DrawIOConfiguration();
        EditorGUILayout.Space(5);
        
        DrawRapidConfiguration();
        EditorGUILayout.Space(5);
        
        DrawStatusSection();
        EditorGUILayout.Space(10);
        
        DrawControlButtons();
        
        serializedObject.ApplyModifiedProperties();
        // Repaint to update status in real-time
        if (Application.isPlaying && toolController.ActiveTool != null)
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
    EditorGUILayout.LabelField("ABB Tool/Gripper Controller", headerStyle);

    if (!Application.isPlaying)
    {
        EditorGUILayout.HelpBox("Tool controls will be available when entering Play Mode.", MessageType.Info);
    }
}

private void DrawToolConfiguration()
{
    showToolSettings = EditorGUILayout.Foldout(showToolSettings, "Tool Configuration", true, EditorStyles.foldoutHeader);

    if (showToolSettings)
    {
        EditorGUI.indentLevel++;

        // Tools list
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tools"), new GUIContent("Tools"), true);

        // Active tool selection
        var activeToolIndexProp = serializedObject.FindProperty("activeToolIndex");
        int toolCount = toolController.Tools.Count;

        if (toolCount > 0)
        {
            string[] toolNames = new string[toolCount];
            for (int i = 0; i < toolCount; i++)
            {
                toolNames[i] = $"{i}: {(toolController.Tools[i].name ?? "Unnamed Tool")}";
            }

            activeToolIndexProp.intValue = EditorGUILayout.Popup("Active Tool", activeToolIndexProp.intValue, toolNames);

            // Show active tool info
            if (Application.isPlaying && toolController.ActiveTool != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Active Tool Info:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"  Name: {toolController.ActiveTool.name}");
                EditorGUILayout.LabelField($"  Control Type: {toolController.ActiveTool.controlType}");

                if (toolController.ActiveTool.flangeToolComponent != null)
                {
                    EditorGUILayout.LabelField($"  Flange Tool: {toolController.ActiveTool.flangeToolComponent.name}");
                }

                if (toolController.ActiveTool.gripperComponent != null)
                {
                    EditorGUILayout.LabelField($"  Gripper: {toolController.ActiveTool.gripperComponent.name}");
                    EditorGUILayout.LabelField($"  Gripper State: {(toolController.ActiveTool.gripperComponent.Gripped ? "Closed" : "Open")}");
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No tools configured. Add tools to enable gripper control.", MessageType.Warning);
        }

        EditorGUI.indentLevel--;
    }
}

private void DrawIOConfiguration()
{
    showIOSettings = EditorGUILayout.Foldout(showIOSettings, "Digital I/O Settings", true, EditorStyles.foldoutHeader);

    if (showIOSettings)
    {
        EditorGUI.indentLevel++;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("ioNetwork"), new GUIContent("I/O Network"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ioDevice"), new GUIContent("I/O Device"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("gripperOpenSignal"), new GUIContent("Gripper Open Signal"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("gripperCloseSignal"), new GUIContent("Gripper Close Signal"));

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "Digital I/O signals control gripper via robot controller outputs.\n" +
            "Example: Local/DRV_1/DO_GripperOpen",
            MessageType.Info);

        EditorGUI.indentLevel--;
    }
}

private void DrawRapidConfiguration()
{
    showRapidSettings = EditorGUILayout.Foldout(showRapidSettings, "RAPID Procedure Settings", true, EditorStyles.foldoutHeader);

    if (showRapidSettings)
    {
        EditorGUI.indentLevel++;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("useRapidProcedures"), new GUIContent("Use RAPID Procedures"));

        if (serializedObject.FindProperty("useRapidProcedures").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rapidTaskName"), new GUIContent("RAPID Task Name"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gripperOpenProcedure"), new GUIContent("Open Procedure"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gripperCloseProcedure"), new GUIContent("Close Procedure"));
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox(
            "RAPID procedures allow complex gripper control logic.\n" +
            "Create procedures like 'GripperOpen' and 'GripperClose' in your RAPID program.",
            MessageType.Info);

        EditorGUI.indentLevel--;
    }
}

private void DrawStatusSection()
{
    EditorGUILayout.LabelField("Tool Status", EditorStyles.boldLabel);

    EditorGUILayout.BeginVertical(statusStyle);

    if (Application.isPlaying && toolController.ActiveTool != null)
    {
        // Gripper state with color coding
        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Gripper State:", GUILayout.Width(100));

        Color originalColor = GUI.color;
        GUI.color = toolController.IsGripperOpen ? Color.green : Color.red;
        EditorGUILayout.LabelField(toolController.IsGripperOpen ? "OPEN" : "CLOSED", EditorStyles.boldLabel);
        GUI.color = originalColor;
        GUILayout.EndHorizontal();

        // Last command and status
        EditorGUILayout.LabelField($"Last Command: {serializedObject.FindProperty("lastToolCommand").stringValue}");
        EditorGUILayout.LabelField($"Status: {serializedObject.FindProperty("toolCommandStatus").stringValue}");
    }
    else if (!Application.isPlaying)
    {
        EditorGUILayout.LabelField("Tool status available in Play Mode");
    }
    else
    {
        EditorGUILayout.LabelField("No active tool configured");
    }

    EditorGUILayout.EndVertical();
}

private void DrawControlButtons()
{
    if (!Application.isPlaying)
    {
        EditorGUILayout.HelpBox("Tool control buttons are only available in Play Mode", MessageType.Info);
        GUI.enabled = false;
    }
    else if (toolController.ActiveTool == null)
    {
        EditorGUILayout.HelpBox("No active tool configured for control", MessageType.Warning);
        GUI.enabled = false;
    }

    EditorGUILayout.LabelField("Tool Controls", EditorStyles.boldLabel);

    GUILayout.BeginHorizontal();

    // Open Gripper button
    Color originalColor = GUI.backgroundColor;
    GUI.backgroundColor = toolController.IsGripperOpen ? Color.green : Color.gray;
    if (GUILayout.Button("Open Gripper", GUILayout.Height(40)))
    {
        toolController.OpenGripper();
    }

    // Close Gripper button
    GUI.backgroundColor = !toolController.IsGripperOpen ? Color.red : Color.gray;
    if (GUILayout.Button("Close Gripper", GUILayout.Height(40)))
    {
        toolController.CloseGripper();
    }

    GUI.backgroundColor = originalColor;
    GUILayout.EndHorizontal();

    // Tool selection buttons
    if (toolController.Tools.Count > 1)
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Quick Tool Selection:", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        for (int i = 0; i < toolController.Tools.Count; i++)
        {
            GUI.backgroundColor = (i == toolController.ActiveToolIndex) ? Color.cyan : originalColor;
            if (GUILayout.Button($"{i}: {toolController.Tools[i].name}", GUILayout.Height(25)))
            {
                toolController.SetActiveTool(i);
            }
        }
        GUI.backgroundColor = originalColor;
        GUILayout.EndHorizontal();
    }

    GUI.enabled = true;

    // Information section
    EditorGUILayout.Space(5);
    EditorGUILayout.LabelField("Integration Information:", EditorStyles.boldLabel);
    EditorGUILayout.HelpBox(
        "• Tools integrate Flange library components with ABB RWS\n" +
        "• Digital I/O sends signals to robot controller outputs\n" +
        "• RAPID procedures call custom gripper control code\n" +
        "• Flange gripper components provide Unity physics simulation",
        MessageType.Info);
    }
}
#endif
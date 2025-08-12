/****************************************************************************
Example usage of ABB Tool Controller
****************************************************************************/

using UnityEngine;
using Preliy.Flange.Common;

[RequireComponent(typeof(ABBToolController))]
public class ABBToolControllerExample : MonoBehaviour
{
    [Header("Example Settings")]
    [SerializeField] private bool logToolEvents = true;
    [SerializeField] private bool showGUI = true;

    private ABBToolController toolController;
    private string lastToolEvent = "None";
    private string lastErrorMessage = "";

    private void Awake()
    {
        toolController = GetComponent<ABBToolController>();

        // Subscribe to tool events
        toolController.OnGripperStateChanged += HandleGripperStateChanged;
        toolController.OnActiveToolChanged += HandleActiveToolChanged;
        toolController.OnToolCommandExecuted += HandleToolCommandExecuted;
        toolController.OnToolError += HandleToolError;
    }

    private void OnDestroy()
    {
        // Unsubscribe from tool events
        if (toolController != null)
        {
            toolController.OnGripperStateChanged -= HandleGripperStateChanged;
            toolController.OnActiveToolChanged -= HandleActiveToolChanged;
            toolController.OnToolCommandExecuted -= HandleToolCommandExecuted;
            toolController.OnToolError -= HandleToolError;
        }
    }
    private void HandleGripperStateChanged(bool isOpen)
    {
        lastToolEvent = $"Gripper {(isOpen ? "Opened" : "Closed")}";

        if (logToolEvents)
        {
            Debug.Log($"[Tool Example] {lastToolEvent}");
        }
    }

    private void HandleActiveToolChanged(int toolIndex)
    {
        var tool = toolController.ActiveTool;
        lastToolEvent = $"Tool changed to: {tool?.name ?? "Unknown"} (Index: {toolIndex})";

        if (logToolEvents)
        {
            Debug.Log($"[Tool Example] {lastToolEvent}");
        }
    }

    private void HandleToolCommandExecuted(string command)
    {
        lastToolEvent = $"Command executed: {command}";

        if (logToolEvents)
        {
            Debug.Log($"[Tool Example] {lastToolEvent}");
        }
    }

    private void HandleToolError(string errorMessage)
    {
        lastErrorMessage = errorMessage;
        lastToolEvent = $"Error: {errorMessage}";

        if (logToolEvents)
        {
            Debug.LogError($"[Tool Example] Tool error: {errorMessage}");
        }
    }

    // Example method to demonstrate automated gripper control
    public async void PerformPickAndPlace()
    {
        if (toolController == null || toolController.ActiveTool == null)
        {
            Debug.LogWarning("[Tool Example] No active tool available for pick and place operation");
            return;
        }

        Debug.Log("[Tool Example] Starting pick and place operation...");

        // Step 1: Open gripper
        bool success = await toolController.ExecuteToolCommand(true);
        if (!success)
        {
            Debug.LogError("[Tool Example] Failed to open gripper");
            return;
        }

        // Step 2: Wait for positioning (simulated)
        await System.Threading.Tasks.Task.Delay(1000);
        Debug.Log("[Tool Example] Moving to pick position (simulated)");

        // Step 3: Close gripper to pick object
        success = await toolController.ExecuteToolCommand(false);
        if (!success)
        {
            Debug.LogError("[Tool Example] Failed to close gripper");
            return;
        }

        // Step 4: Wait for movement (simulated)
        await System.Threading.Tasks.Task.Delay(2000);
        Debug.Log("[Tool Example] Moving to place position (simulated)");

        // Step 5: Open gripper to release object
        success = await toolController.ExecuteToolCommand(true);
        if (!success)
        {
            Debug.LogError("[Tool Example] Failed to release object");
            return;
        }

        Debug.Log("[Tool Example] Pick and place operation completed!");
    }

    // Example method to cycle through tools
    public void CycleTools()
    {
        if (toolController.Tools.Count <= 1) return;

        int nextTool = (toolController.ActiveToolIndex + 1) % toolController.Tools.Count;
        toolController.SetActiveTool(nextTool);
    }

    // Simple GUI for testing
    private void OnGUI()
    {
        if (!showGUI) return;

        GUILayout.BeginArea(new Rect(320, 10, 300, 500));
        GUILayout.BeginVertical("box");

        GUILayout.Label("ABB Tool Controller", GUI.skin.GetStyle("label"));
        GUILayout.Space(10);

        // Tool status
        if (toolController.ActiveTool != null)
        {
            GUILayout.Label($"Active Tool: {toolController.ActiveTool.name}");
            GUILayout.Label($"Control Type: {toolController.ActiveTool.controlType}");

            Color originalColor = GUI.color;
            GUI.color = toolController.IsGripperOpen ? Color.green : Color.red;
            GUILayout.Label($"Gripper: {(toolController.IsGripperOpen ? "OPEN" : "CLOSED")}");
            GUI.color = originalColor;
        }
        else
        {
            GUILayout.Label("No active tool configured");
        }

        GUILayout.Space(10);

        // Control buttons
        if (Application.isPlaying && toolController.ActiveTool != null)
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Gripper"))
            {
                toolController.OpenGripper();
            }

            if (GUILayout.Button("Close Gripper"))
            {
                toolController.CloseGripper();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            if (GUILayout.Button("Pick & Place Demo"))
            {
                PerformPickAndPlace();
            }

            if (toolController.Tools.Count > 1)
            {
                if (GUILayout.Button("Cycle Tools"))
                {
                    CycleTools();
                }
            }
        }

        GUILayout.Space(10);

        // Status information
        GUILayout.Label("Recent Events:");
        GUILayout.Label($"Last Event: {lastToolEvent}");

        if (!string.IsNullOrEmpty(lastErrorMessage))
        {
            Color originalColor = GUI.color;
            GUI.color = Color.red;
            GUILayout.Label($"Last Error: {lastErrorMessage}");
            GUI.color = originalColor;
        }

        GUILayout.Space(10);

        // Integration info
        GUILayout.Label("Integration Status:");
        GUILayout.Label("• Flange Library: Tool definitions");
        GUILayout.Label("• ABB RWS: I/O & RAPID control");
        GUILayout.Label("• Unity Physics: Gripper simulation");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}

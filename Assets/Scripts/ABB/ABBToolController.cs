/****************************************************************************
ABB Tool/Gripper Controller - Integrates Flange Library Tools with RWS
****************************************************************************/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Preliy.Flange;
using Preliy.Flange.Common;
using Debug = UnityEngine.Debug;

[AddComponentMenu("ABB/ABB Tool Controller")]
[RequireComponent(typeof(ABBRobotWebServicesController))]
public class ABBToolController : MonoBehaviour
{
    [Header("Tool Configuration")]
    [SerializeField] private List<ToolDefinition> tools = new List<ToolDefinition>();
    [SerializeField] private int activeToolIndex = 0;
    
    [Header("Digital I/O Settings")]
    [SerializeField] private string ioNetwork = "Local";
    [SerializeField] private string ioDevice = "DRV_1";
    [SerializeField] private string gripperOpenSignal = "DO_GripperOpen";
    [SerializeField] private string gripperCloseSignal = "DO_GripperClose";
    
    [Header("RAPID Procedure Calls")]
    [SerializeField] private bool useRapidProcedures = true;
    [SerializeField] private string rapidTaskName = "T_ROB1";
    [SerializeField] private string gripperOpenProcedure = "GripperOpen";
    [SerializeField] private string gripperCloseProcedure = "GripperClose";
    
    [Header("Status (Read Only)")]
    [SerializeField, ReadOnly] private bool isGripperOpen = false;
    [SerializeField, ReadOnly] private string lastToolCommand = "";
    [SerializeField, ReadOnly] private string toolCommandStatus = "";
    
    // Dependencies
    private ABBRobotWebServicesController abbController;
    private Controller flangeController;
    
    // Current active tool components
    private Tool currentFlangeTools;
    private Gripper currentGripper;
    
    // Events
    public event Action<bool> OnGripperStateChanged;
    public event Action<int> OnActiveToolChanged;
    public event Action<string> OnToolCommandExecuted;
    public event Action<string> OnToolError;
    
    [Serializable]
    public class ToolDefinition
    {
        [SerializeField] public string name = "Tool";
        [SerializeField] public Tool flangeToolComponent;
        [SerializeField] public Gripper gripperComponent;
        [SerializeField] public ToolControlType controlType = ToolControlType.DigitalIO;
        
        [Header("Custom I/O Signals (Optional)")]
        [SerializeField] public string customOpenSignal = "";
        [SerializeField] public string customCloseSignal = "";
        
        [Header("Custom RAPID Procedures (Optional)")]
        [SerializeField] public string customOpenProcedure = "";
        [SerializeField] public string customCloseProcedure = "";
    }
    
    public enum ToolControlType
    {
        DigitalIO,
        RapidProcedure,
        Both
    }
    
    // Properties
    public bool IsGripperOpen => isGripperOpen;
    public int ActiveToolIndex => activeToolIndex;
    public ToolDefinition ActiveTool => activeToolIndex >= 0 && activeToolIndex < tools.Count ? tools[activeToolIndex] : null;
    public List<ToolDefinition> Tools => tools;
    
    private void Awake()
    {
        abbController = GetComponent<ABBRobotWebServicesController>();
        flangeController = GetComponent<Controller>();
        
        if (abbController == null)
            Debug.LogError("[ABB Tool Controller] ABBRobotWebServicesController not found!");
        if (flangeController == null)
            Debug.LogError("[ABB Tool Controller] Flange Controller not found!");
    }
    
    private void Start()
    {
        // Set initial tool
        if (tools.Count > 0 && activeToolIndex >= 0 && activeToolIndex < tools.Count)
        {
            SetActiveTool(activeToolIndex);
        }
    }
    
    [ContextMenu("Open Gripper")]
    public async void OpenGripper()
    {
        await ExecuteToolCommand(true);
    }
    
    [ContextMenu("Close Gripper")]
    public async void CloseGripper()
    {
        await ExecuteToolCommand(false);
    }
    
    public async Task<bool> ExecuteToolCommand(bool open)
    {
        if (ActiveTool == null)
        {
            LogError("No active tool defined");
            return false;
        }
        
        if (!abbController.IsConnected)
        {
            LogError("ABB controller not connected");
            return false;
        }
        
        lastToolCommand = open ? "Open" : "Close";
        toolCommandStatus = "Executing...";
        
        bool success = false;
        
        try
        {
            // Execute based on control type
            switch (ActiveTool.controlType)
            {
                case ToolControlType.DigitalIO:
                    success = await ExecuteDigitalIOCommand(open);
                    break;
                    
                case ToolControlType.RapidProcedure:
                    success = useRapidProcedures ? await ExecuteRapidProcedureCommand(open) : false;
                    if (!useRapidProcedures)
                    {
                        Debug.LogWarning("RAPID procedures disabled - enable 'Use Rapid Procedures' to use this control type");
                    }
                    break;
                    
                case ToolControlType.Both:
                    bool ioSuccess = await ExecuteDigitalIOCommand(open);
                    bool rapidSuccess = useRapidProcedures ? await ExecuteRapidProcedureCommand(open) : true;
                    if (!useRapidProcedures)
                    {
                        Debug.LogWarning("RAPID procedures disabled - only using Digital I/O");
                    }
                    success = ioSuccess && rapidSuccess;
                    break;
            }
            
            if (success)
            {
                // Update local gripper state
                isGripperOpen = open;
                toolCommandStatus = $"Success - Gripper {(open ? "Opened" : "Closed")}";
                
                // Update Flange gripper component
                if (currentGripper != null)
                {
                    currentGripper.Grip(!open); // Gripper.Grip(true) = closed, Grip(false) = open
                }
                
                // Fire events
                OnGripperStateChanged?.Invoke(isGripperOpen);
                OnToolCommandExecuted?.Invoke($"{lastToolCommand} - Success");
                
                LogInfo($"Gripper {(open ? "opened" : "closed")} successfully");
            }
            else
            {
                toolCommandStatus = $"Failed - {lastToolCommand}";
                OnToolError?.Invoke($"Failed to {lastToolCommand.ToLower()} gripper");
            }
        }
        catch (Exception e)
        {
            toolCommandStatus = $"Error - {e.Message}";
            OnToolError?.Invoke(e.Message);
            LogError($"Tool command failed: {e.Message}");
        }
        
        return success;
    }
    
    private async Task<bool> ExecuteDigitalIOCommand(bool open)
    {
        try
        {
            // Determine which signal to use
            string signalName = open ? 
                (!string.IsNullOrEmpty(ActiveTool.customOpenSignal) ? ActiveTool.customOpenSignal : gripperOpenSignal) :
                (!string.IsNullOrEmpty(ActiveTool.customCloseSignal) ? ActiveTool.customCloseSignal : gripperCloseSignal);
            
            // Build RWS I/O signal URL
            string url = $"http://{abbController.IPAddress}:{abbController.Port}/rw/iosystem/signals/{ioNetwork}/{ioDevice}/{signalName}?action=set";
            
            // Create form data
            var formParams = new List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("lvalue", "1") // Set signal high
            };
            
            using (var client = abbController.CreateHttpClient())
            {
                var content = new FormUrlEncodedContent(formParams);
                var response = await client.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    LogInfo($"Digital I/O command sent: {signalName} = 1");
                    
                    // For momentary signals, send low signal after delay
                    await Task.Delay(100); // 100ms pulse
                    
                    formParams[0] = new System.Collections.Generic.KeyValuePair<string, string>("lvalue", "0");
                    content = new FormUrlEncodedContent(formParams);
                    await client.PostAsync(url, content);
                    
                    LogInfo($"Digital I/O command completed: {signalName} = 0");
                    return true;
                }
                else
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    LogError($"Digital I/O command failed: {response.StatusCode} - {responseContent}");
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            LogError($"Digital I/O command exception: {e.Message}");
            return false;
        }
    }
    
    private async Task<bool> ExecuteRapidProcedureCommand(bool open)
    {
        try
        {
            // Determine which procedure to call
            string procedureName = open ?
                (!string.IsNullOrEmpty(ActiveTool.customOpenProcedure) ? ActiveTool.customOpenProcedure : gripperOpenProcedure) :
                (!string.IsNullOrEmpty(ActiveTool.customCloseProcedure) ? ActiveTool.customCloseProcedure : gripperCloseProcedure);
            
            // Build RWS RAPID procedure call URL
            string url = $"http://{abbController.IPAddress}:{abbController.Port}/rw/rapid/tasks/{rapidTaskName}/procedure?action=call";
            
            // Create form data for procedure call
            var formParams = new List<System.Collections.Generic.KeyValuePair<string, string>>
            {
                new System.Collections.Generic.KeyValuePair<string, string>("procedure", procedureName)
            };
            
            using (var client = abbController.CreateHttpClient())
            {
                var content = new FormUrlEncodedContent(formParams);
                var response = await client.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    LogInfo($"RAPID procedure called: {procedureName}");
                    return true;
                }
                else
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    LogError($"RAPID procedure call failed: {response.StatusCode} - {responseContent}");
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            LogError($"RAPID procedure call exception: {e.Message}");
            return false;
        }
    }
    
    public void SetActiveTool(int toolIndex)
    {
        if (toolIndex < 0 || toolIndex >= tools.Count)
        {
            LogError($"Invalid tool index: {toolIndex}");
            return;
        }
        
        activeToolIndex = toolIndex;
        var tool = tools[toolIndex];
        
        // Find the tool's index in the Flange Controller's tools list
        if (flangeController != null && tool.flangeToolComponent != null)
        {
            var flangeTools = flangeController.Tools;
            for (int i = 0; i < flangeTools.Count; i++)
            {
                if (flangeTools[i] == tool.flangeToolComponent)
                {
                    flangeController.Tool.Value = i;
                    break;
                }
            }
        }
        
        // Update current tool references
        currentFlangeTools = tool.flangeToolComponent;
        currentGripper = tool.gripperComponent;
        
        LogInfo($"Active tool set to: {tool.name}");
        OnActiveToolChanged?.Invoke(toolIndex);
    }
    
    public void AddTool(ToolDefinition tool)
    {
        tools.Add(tool);
        LogInfo($"Tool added: {tool.name}");
    }
    
    public void RemoveTool(int index)
    {
        if (index >= 0 && index < tools.Count)
        {
            string toolName = tools[index].name;
            tools.RemoveAt(index);
            
            // Adjust active tool index if necessary
            if (activeToolIndex >= tools.Count)
            {
                activeToolIndex = System.Math.Max(0, tools.Count - 1);
            }
            
            LogInfo($"Tool removed: {toolName}");
        }
    }
    
    // Logging methods
    private void LogInfo(string message)
    {
        Debug.Log($"[ABB Tool Controller] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[ABB Tool Controller] {message}");
    }
    
    private void OnValidate()
    {
        // Ensure active tool index is within bounds
        if (tools.Count > 0)
        {
            activeToolIndex = Mathf.Clamp(activeToolIndex, 0, tools.Count - 1);
        }
    }
}
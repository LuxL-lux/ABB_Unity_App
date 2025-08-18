/****************************************************************************
ABB Robot Web Services Integration for Flange Controller
MIT License
****************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Diagnostics;
using UnityEngine;
using Preliy.Flange;
using ABB.RWS;

[AddComponentMenu("ABB/ABB Robot Web Services Controller")]
[RequireComponent(typeof(Controller))]
public class ABBRobotWebServicesController : MonoBehaviour, IRWSController
{
    [Header("Connection Settings")]
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private int port = 80;
    [SerializeField] private string username = "Default User";
    [SerializeField] private string password = "robotics";
    [SerializeField] private string taskName = "T_ROB1";
    
    [Header("Data Update Settings")]
    [SerializeField] private int pollingIntervalMs = 100;
    [SerializeField] private bool useWebSocketWhenAvailable = true;
    [SerializeField] private bool autoStartOnEnable = true;
    
    [Header("I/O Signal Monitoring")]
    [SerializeField] private bool monitorIOSignals = true;
    [SerializeField] private float ioPollingInterval = 0.5f;
    [SerializeField] private List<ABB.RWS.IOSignalDefinition> ioSignalsToMonitor = new List<ABB.RWS.IOSignalDefinition>();
    
    [Header("Status (Read Only)")]
    [SerializeField, ReadOnly] private ConnectionStatus connectionStatus = ConnectionStatus.Disconnected;
    [SerializeField, ReadOnly] private bool isUsingWebSocket = false;
    [SerializeField, ReadOnly] private double updateFrequencyHz = 0;
    [SerializeField, ReadOnly] private long lastUpdateTimestamp = 0;
    [SerializeField, ReadOnly] private int totalUpdatesReceived = 0;
    [SerializeField, ReadOnly] private string lastErrorMessage = "";
    
    [Header("RAPID Program Status (Read Only)")]
    [SerializeField, ReadOnly] private string currentProgramName = "Unknown";
    [SerializeField, ReadOnly] private string executionState = "Unknown";
    [SerializeField, ReadOnly] private string operationMode = "Unknown";
    [SerializeField, ReadOnly] private string controllerState = "Unknown";
    [SerializeField, ReadOnly] private string rapidTaskType = "Unknown";
    [SerializeField, ReadOnly] private bool rapidTaskActive = false;
    [SerializeField, ReadOnly] private string programPointer = "Unknown";
    
    [Header("Joint Limit Monitoring (Read Only)")]
    [SerializeField, ReadOnly] private bool[] jointLimitWarnings = new bool[6];
    [SerializeField, ReadOnly] private float[] jointLimitPercentages = new float[6];
    [SerializeField, ReadOnly] private float[] jointMinLimits = new float[6];
    [SerializeField, ReadOnly] private float[] jointMaxLimits = new float[6];
    [SerializeField, ReadOnly] private string jointLimitStatus = "OK";
    
    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool showPerformanceMetrics = true;
    [SerializeField] private bool logJointUpdates = false;
    
    // Flange Controller Integration
    private Controller flangeController;
    private ABBDataStream dataStream;
    private ABBSafetyLogger safetyLogger;
    
    // Current robot data
    private readonly float[] currentJointAngles = new float[6];
    private readonly object dataLock = new object();
    
    // Main thread update queue
    private readonly Queue<float[]> pendingJointUpdates = new Queue<float[]>();
    private readonly object updateQueueLock = new object();
    private bool hasNewData = false;
    
    // RAPID program status monitoring
    private DateTime lastRapidStatusUpdate = DateTime.MinValue;
    private readonly TimeSpan rapidStatusUpdateInterval = TimeSpan.FromSeconds(1.5f); // Update every 1.5 seconds
    private bool rapidStatusInitialized = false;
    
    // I/O signal monitoring
    private DateTime lastIOPoll = DateTime.MinValue;
    private readonly Dictionary<string, bool> ioSignalStates = new Dictionary<string, bool>();
    
    // Joint limit monitoring
    private readonly float[] jointLimitWarningThreshold = { 95f, 95f, 95f, 95f, 95f, 95f }; // 95% of max range
    
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
        Stopping
    }
    
    // Public properties for external access
    public ConnectionStatus Status => connectionStatus;
    public bool IsConnected => connectionStatus == ConnectionStatus.Connected;
    public double UpdateFrequency => updateFrequencyHz;
    public string CurrentProgramName => currentProgramName;
    public string ExecutionState => executionState;
    public string OperationMode => operationMode;
    public string RapidTaskType => rapidTaskType;
    public bool RapidTaskActive => rapidTaskActive;
    public string ProgramPointer => programPointer;
    public float[] JointAngles 
    {
        get 
        {
            lock (dataLock)
            {
                return (float[])currentJointAngles.Clone();
            }
        }
    }
    
    // RAPID program status properties
    public string ControllerState => controllerState;
    
    // Get formatted RAPID context for error logging
    public string GetRapidContext()
    {
        return $"Program: {currentProgramName}, State: {executionState}, Mode: {operationMode}, Controller: {controllerState}, Active: {rapidTaskActive}";
    }
    
    // Joint limit properties
    public bool[] JointLimitWarnings => (bool[])jointLimitWarnings.Clone();
    public float[] JointLimitPercentages => (float[])jointLimitPercentages.Clone();
    public float[] JointMinLimits => (float[])jointMinLimits.Clone();
    public float[] JointMaxLimits => (float[])jointMaxLimits.Clone();
    public string JointLimitStatus => jointLimitStatus;
    
    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<float[]> OnJointDataReceived;
    public event Action<string> OnError;
    public event Action<string> OnRapidStatusChanged;
    public event Action<int, bool> OnJointLimitWarning; // jointIndex, isWarning
    public event Action<string, bool> OnGripperSignalReceived; // signalName, signalState
    public event Action<string, bool> OnIOSignalChanged; // signalName, newState
    
    private void Awake()
    {
        flangeController = GetComponent<Controller>();
        safetyLogger = ABBSafetyLogger.Instance;
        
        if (flangeController == null)
        {
            LogError("Flange Controller component not found! This component requires a Controller.");
        }
        
        // Initialize data stream
        dataStream = new ABBDataStream(this);
    }
    
    private void OnEnable()
    {
        if (autoStartOnEnable && flangeController != null)
        {
            StartConnection();
        }
    }
    
    private void OnDisable()
    {
        StopConnection();
    }
    
    private void Update()
    {
        // Process pending updates on main thread
        ProcessPendingUpdates();
        
        // Update RAPID program status periodically
        if (IsConnected && DateTime.Now - lastRapidStatusUpdate > rapidStatusUpdateInterval)
        {
            _ = UpdateRapidProgramStatusAsync();
            lastRapidStatusUpdate = DateTime.Now;
        }
        
        // Poll I/O signals periodically
        if (IsConnected && monitorIOSignals && DateTime.Now - lastIOPoll > TimeSpan.FromSeconds(ioPollingInterval))
        {
            _ = PollIOSignalsAsync();
            lastIOPoll = DateTime.Now;
        }
    }
    
    private void OnApplicationQuit()
    {
        StopConnection();
    }
    
    [ContextMenu("Start Connection")]
    public void StartConnection()
    {
        if (connectionStatus == ConnectionStatus.Connected || connectionStatus == ConnectionStatus.Connecting)
        {
            LogWarning("Already connected or connecting.");
            return;
        }
        
        if (flangeController == null)
        {
            LogError("Cannot start connection: Flange Controller not found.");
            return;
        }
        
        connectionStatus = ConnectionStatus.Connecting;
        dataStream = new ABBDataStream(this);
        dataStream.Start();
        
        LogInfo($"Starting ABB Robot Web Services connection to {ipAddress}:{port}");
    }
    
    [ContextMenu("Stop Connection")]
    public void StopConnection()
    {
        if (connectionStatus == ConnectionStatus.Disconnected)
            return;
            
        connectionStatus = ConnectionStatus.Stopping;
        dataStream?.Stop();
        
        LogInfo("Stopping ABB Robot Web Services connection");
    }
    
    [ContextMenu("Test Connection")]
    public async void TestConnection()
    {
        LogInfo("Testing connection...");
        
        try
        {
            using (var client = CreateHttpClient())
            {
                string testUrl = $"http://{ipAddress}:{port}/rw/system";
                var response = await client.GetAsync(testUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    LogInfo("Connection test successful!");
                }
                else
                {
                    LogError($"Connection test failed: {response.StatusCode}");
                }
            }
        }
        catch (Exception e)
        {
            LogError($"Connection test failed: {e.Message}");
        }
    }
    
    internal HttpClient CreateHttpClient()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler 
        { 
            Credentials = new NetworkCredential(username, password),
            Proxy = null,
            UseProxy = false,
            CookieContainer = cookieContainer
        };
        
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }
    
    public void OnConnectionEstablished(bool usingWebSocket)
    {
        connectionStatus = ConnectionStatus.Connected;
        isUsingWebSocket = usingWebSocket;
        lastErrorMessage = "";
        
        // Log connection success
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS, 
                "RWS Connection established", 
                $"Method: {(usingWebSocket ? "WebSocket" : "HTTP Polling")}, Target: {ipAddress}:{port}");
        }
        
        LogInfo($"Connection established. Using {(usingWebSocket ? "WebSocket" : "HTTP polling")}");
        OnConnected?.Invoke();
    }
    
    public void OnConnectionLost(string error = "")
    {
        connectionStatus = ConnectionStatus.Error;
        lastErrorMessage = error;
        isUsingWebSocket = false;
        
        // Log connection loss with RAPID context
        if (safetyLogger != null)
        {
            string context = rapidStatusInitialized ? $", RAPID Context: {GetRapidContext()}" : "";
            safetyLogger.LogError(ABBSafetyLogger.LogCategory.RWS, 
                "RWS Connection lost", 
                $"Error: {error}, Target: {ipAddress}:{port}{context}");
        }
        
        LogError($"Connection lost: {error}");
        OnDisconnected?.Invoke();
        OnError?.Invoke(error);
    }
    
    public void OnDataReceived(double[] jointData)
    {
        if (jointData == null || jointData.Length < 6) return;
        
        float[] jointAnglesFloat = new float[6];
        for (int i = 0; i < 6; i++)
        {
            jointAnglesFloat[i] = (float)jointData[i];
        }
        
        // Queue update for main thread processing
        lock (updateQueueLock)
        {
            pendingJointUpdates.Enqueue(jointAnglesFloat);
            hasNewData = true;
        }
        
        // Update performance metrics (safe to do on background thread)
        UpdatePerformanceMetrics();
    }
    
    private void ProcessPendingUpdates()
    {
        if (!hasNewData) return;
        
        float[] latestJointAngles = null;
        
        // Get the latest joint data from queue
        lock (updateQueueLock)
        {
            if (pendingJointUpdates.Count > 0)
            {
                // Get the most recent update and clear older ones to avoid lag
                while (pendingJointUpdates.Count > 1)
                {
                    pendingJointUpdates.Dequeue();
                }
                latestJointAngles = pendingJointUpdates.Dequeue();
            }
            hasNewData = pendingJointUpdates.Count > 0;
        }
        
        if (latestJointAngles != null)
        {
            // Update current joint angles
            lock (dataLock)
            {
                Array.Copy(latestJointAngles, currentJointAngles, 6);
            }
            
            // Update Flange Controller (now on main thread)
            UpdateFlangeController(latestJointAngles);
            
            // Fire event
            OnJointDataReceived?.Invoke((float[])latestJointAngles.Clone());
            
            if (logJointUpdates)
            {
                LogInfo($"Joint data: [{string.Join(", ", Array.ConvertAll(latestJointAngles, x => x.ToString("F2")))}]");
            }
        }
    }
    
    private void UpdateFlangeController(float[] jointAngles)
    {
        if (flangeController == null || !flangeController.IsValid.Value) return;
        
        try
        {
            // Check joint limits before updating
            CheckJointLimits(jointAngles);
            
            var jointTarget = new JointTarget(jointAngles);
            flangeController.MechanicalGroup.SetJoints(jointTarget, notify: true);
        }
        catch (Exception e)
        {
            LogError($"Failed to update Flange Controller: {e.Message}");
        }
    }
    
    private void CheckJointLimits(float[] jointAngles)
    {
        if (flangeController?.MechanicalGroup?.RobotJoints == null) return;
        
        bool anyLimitWarning = false;
        
        for (int i = 0; i < System.Math.Min(jointAngles.Length, flangeController.MechanicalGroup.RobotJoints.Count); i++)
        {
            var joint = flangeController.MechanicalGroup.RobotJoints[i];
            if (joint?.Config == null) continue;
            
            float minLimit = joint.Config.Limits.x;
            float maxLimit = joint.Config.Limits.y;
            float currentAngle = jointAngles[i];
            
            // Store limit values for inspector display
            jointMinLimits[i] = minLimit;
            jointMaxLimits[i] = maxLimit;
            
            // Calculate percentage of limit range used
            float range = maxLimit - minLimit;
            float position = currentAngle - minLimit;
            float percentage = (position / range) * 100f;
            
            jointLimitPercentages[i] = Mathf.Abs(percentage);
            
            // Check if approaching limits
            bool wasWarning = jointLimitWarnings[i];
            bool isWarning = (currentAngle <= minLimit + (range * 0.05f)) || // Within 5% of min limit
                           (currentAngle >= maxLimit - (range * 0.05f));   // Within 5% of max limit
            
            jointLimitWarnings[i] = isWarning;
            
            if (isWarning != wasWarning)
            {
                OnJointLimitWarning?.Invoke(i, isWarning);
                if (isWarning)
                {
                    LogWarning($"Joint {i + 1} approaching limit: {currentAngle:F2}° (range: {minLimit:F2}° to {maxLimit:F2}°)");
                }
            }
            
            if (isWarning) anyLimitWarning = true;
        }
        
        string newStatus = anyLimitWarning ? "WARNING - Joint limits approached!" : "OK";
        if (newStatus != jointLimitStatus)
        {
            jointLimitStatus = newStatus;
            if (anyLimitWarning)
            {
                LogWarning($"Joint limit status: {jointLimitStatus}");
            }
        }
    }
    
    private async Task UpdateRapidProgramStatusAsync()
    {
        try
        {
            using (var client = CreateHttpClient())
            {
                // Get RAPID status endpoints using correct RWS API URLs
                var tasks = new Task<(string data, string endpoint)>[]
                {
                    GetRapidDataWithEndpointAsync(client, $"/rw/rapid/tasks/{taskName}"), // Specific task state
                    GetRapidDataWithEndpointAsync(client, "/rw/panel/ctrlstate"), // Controller state
                    GetRapidDataWithEndpointAsync(client, "/rw/panel/opmode"), // Operation mode  
                    GetRapidDataWithEndpointAsync(client, $"/rw/rapid/tasks/{taskName}/pcp"), // Program pointer
                    GetRapidDataWithEndpointAsync(client, "/rw/rapid/execution") // Global execution state
                };
                
                var results = await Task.WhenAll(tasks);
                
                // Parse results with better error context
                ParseRapidStatusImproved(results);
                
                if (!rapidStatusInitialized)
                {
                    rapidStatusInitialized = true;
                    LogInfo("RAPID status monitoring initialized successfully");
                    
                    if (safetyLogger != null)
                    {
                        safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                            "RAPID Status Initialized",
                            GetRapidContext());
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to update RAPID status: {e.Message}");
            }
            
            // Log RAPID monitoring errors to safety logger
            if (safetyLogger != null)
            {
                safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.RWS,
                    "RAPID Status Update Failed", 
                    $"Error: {e.Message}, Target: {ipAddress}:{port}");
            }
        }
    }
    
    private async Task<(string data, string endpoint)> GetRapidDataWithEndpointAsync(HttpClient client, string endpoint)
    {
        try
        {
            string url = $"http://{ipAddress}:{port}{endpoint}";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                string data = await response.Content.ReadAsStringAsync();
                return (data, endpoint);
            }
            else
            {
                if (enableDebugLogging)
                {
                    LogWarning($"RWS API returned {response.StatusCode} for {endpoint}");
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to get data from {endpoint}: {e.Message}");
            }
        }
        return ("", endpoint);
    }
    
    private void ParseRapidStatusImproved((string data, string endpoint)[] results)
    {
        try
        {
            string newProgramName = "Unknown";
            string newExecutionState = "Unknown";
            string newOperationMode = "Unknown";
            string newControllerState = "Unknown";
            string newRapidTaskType = "Unknown";
            bool newRapidTaskActive = false;
            string newProgramPointer = "Unknown";
            
            foreach (var (data, endpoint) in results)
            {
                if (string.IsNullOrEmpty(data)) continue;
                
                try
                {
                    if (endpoint.Contains("/ctrlstate"))
                    {
                        // Parse controller state from panel endpoint
                        newControllerState = ExtractValueFromResponse(data, "ctrlstate") ?? 
                                           ExtractXmlValue(data, "ctrlstate") ?? 
                                           ExtractClassValue(data, "pnl-ctrlstate-ev") ?? "Unknown";
                    }
                    else if (endpoint.Contains("/opmode"))
                    {
                        // Parse operation mode from panel endpoint  
                        newOperationMode = ExtractValueFromResponse(data, "opmode") ?? 
                                         ExtractXmlValue(data, "opmode") ?? 
                                         ExtractClassValue(data, "pnl-opmode-ev") ?? "Unknown";
                    }
                    else if (endpoint.Contains($"/tasks/{taskName}") && !endpoint.Contains("/pcp"))
                    {
                        // Specific task information
                        newExecutionState = ExtractValueFromResponse(data, "excstate") ?? ExtractXmlValue(data, "excstate") ?? "Unknown";
                        newProgramName = ExtractValueFromResponse(data, "name") ?? ExtractXmlValue(data, "name") ?? "Unknown";
                        newRapidTaskType = ExtractValueFromResponse(data, "type") ?? ExtractXmlValue(data, "type") ?? "Unknown";
                        newRapidTaskActive = ExtractBoolValueFromResponse(data, "active");
                    }
                    else if (endpoint.Contains("/pcp"))
                    {
                        // Program counter/pointer information
                        newProgramPointer = ExtractValueFromResponse(data, "routine") ?? ExtractXmlValue(data, "routine") ?? "Unknown";
                        if (newProgramPointer == "Unknown")
                        {
                            // Try to get module and routine info
                            string module = ExtractValueFromResponse(data, "module") ?? ExtractXmlValue(data, "module");
                            string routine = ExtractValueFromResponse(data, "routine") ?? ExtractXmlValue(data, "routine");
                            if (!string.IsNullOrEmpty(module) && !string.IsNullOrEmpty(routine))
                            {
                                newProgramPointer = $"{module}.{routine}";
                            }
                        }
                    }
                    else if (endpoint.Contains("/execution"))
                    {
                        // Global execution state
                        string globalExecState = ExtractValueFromResponse(data, "ctrlexecstate") ?? ExtractXmlValue(data, "ctrlexecstate");
                        if (!string.IsNullOrEmpty(globalExecState) && globalExecState != "Unknown")
                        {
                            // Use global state if task-specific state is unknown
                            if (newExecutionState == "Unknown")
                            {
                                newExecutionState = globalExecState;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (enableDebugLogging)
                    {
                        LogWarning($"Failed to parse data from {endpoint}: {ex.Message}");
                    }
                }
            }
            
            // Update status if changed and log changes
            bool statusChanged = false;
            
            if (newProgramName != currentProgramName)
            {
                currentProgramName = newProgramName;
                statusChanged = true;
            }
            if (newExecutionState != executionState)
            {
                executionState = newExecutionState;
                statusChanged = true;
            }
            if (newOperationMode != operationMode)
            {
                operationMode = newOperationMode;
                statusChanged = true;
            }
            if (newControllerState != controllerState)
            {
                controllerState = newControllerState;
                statusChanged = true;
            }
            if (newRapidTaskType != rapidTaskType)
            {
                rapidTaskType = newRapidTaskType;
                statusChanged = true;
            }
            if (newRapidTaskActive != rapidTaskActive)
            {
                rapidTaskActive = newRapidTaskActive;
                statusChanged = true;
            }
            if (newProgramPointer != programPointer)
            {
                programPointer = newProgramPointer;
                statusChanged = true;
            }
            
            if (statusChanged)
            {
                string statusMessage = GetRapidContext();
                LogInfo($"RAPID Status Update: {statusMessage}");
                OnRapidStatusChanged?.Invoke(statusMessage);
                
                // Log status change to safety logger
                if (safetyLogger != null)
                {
                    safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                        "RAPID Status Changed",
                        $"{statusMessage}, Pointer: {programPointer}");
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to parse RAPID status: {e.Message}");
            }
        }
    }
    
    private string ExtractValueFromResponse(string response, string key)
    {
        try
        {
            // Try JSON pattern first (RWS sometimes returns JSON)
            string jsonPattern = $"\"{key}\":\"";
            int startIndex = response.IndexOf(jsonPattern);
            if (startIndex >= 0)
            {
                startIndex += jsonPattern.Length;
                int endIndex = response.IndexOf('"', startIndex);
                if (endIndex > startIndex)
                {
                    return response.Substring(startIndex, endIndex - startIndex);
                }
            }
            
            // Try XML pattern
            return ExtractXmlValue(response, key);
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to extract {key} from response: {e.Message}");
            }
        }
        return null;
    }
    
    private string ExtractXmlValue(string response, string key)
    {
        try
        {
            // Standard XML tags
            string startTag = $"<{key}>";
            string endTag = $"</{key}>";
            int startIndex = response.IndexOf(startTag);
            if (startIndex >= 0)
            {
                startIndex += startTag.Length;
                int endIndex = response.IndexOf(endTag, startIndex);
                if (endIndex > startIndex)
                {
                    return response.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
            
            // Try with attributes (common in RWS responses)
            string attrPattern = $"{key}=\"";
            startIndex = response.IndexOf(attrPattern);
            if (startIndex >= 0)
            {
                startIndex += attrPattern.Length;
                int endIndex = response.IndexOf('"', startIndex);
                if (endIndex > startIndex)
                {
                    return response.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to extract XML value {key}: {e.Message}");
            }
        }
        return null;
    }
    
    private bool ExtractBoolValueFromResponse(string response, string key)
    {
        string value = ExtractValueFromResponse(response, key) ?? ExtractXmlValue(response, key);
        if (string.IsNullOrEmpty(value)) return false;
        
        return value.ToLower() == "true" || value == "1" || value.ToLower() == "on";
    }
    
    private string ExtractClassValue(string response, string className)
    {
        try
        {
            // Look for elements with specific class (common in RWS panel responses)
            string classPattern = $"class='{className}'";
            int startIndex = response.IndexOf(classPattern);
            if (startIndex >= 0)
            {
                // Find the next > to get to the content
                int contentStart = response.IndexOf('>', startIndex);
                if (contentStart >= 0)
                {
                    contentStart++;
                    int contentEnd = response.IndexOf('<', contentStart);
                    if (contentEnd > contentStart)
                    {
                        return response.Substring(contentStart, contentEnd - contentStart).Trim();
                    }
                }
            }
            
            // Also try with single quotes
            classPattern = $"class='{className}'";
            startIndex = response.IndexOf(classPattern);
            if (startIndex >= 0)
            {
                int contentStart = response.IndexOf('>', startIndex);
                if (contentStart >= 0)
                {
                    contentStart++;
                    int contentEnd = response.IndexOf('<', contentStart);
                    if (contentEnd > contentStart)
                    {
                        return response.Substring(contentStart, contentEnd - contentStart).Trim();
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to extract class value {className}: {e.Message}");
            }
        }
        return null;
    }
    
    private void UpdatePerformanceMetrics()
    {
        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        if (lastUpdateTimestamp > 0)
        {
            long timeDiff = currentTime - lastUpdateTimestamp;
            updateFrequencyHz = timeDiff > 0 ? 1000.0 / timeDiff : 0;
        }
        
        lastUpdateTimestamp = currentTime;
        totalUpdatesReceived++;
    }
    
    public void OnConnectionStopped()
    {
        connectionStatus = ConnectionStatus.Disconnected;
        isUsingWebSocket = false;
        updateFrequencyHz = 0;
        
        LogInfo("Connection stopped");
        OnDisconnected?.Invoke();
    }
    
    // Logging methods
    private void LogInfo(string message)
    {
        if (enableDebugLogging)
            UnityEngine.Debug.Log($"[ABB RWS] {message}");
    }
    
    private void LogWarning(string message)
    {
        if (enableDebugLogging)
            UnityEngine.Debug.LogWarning($"[ABB RWS] {message}");
    }
    
    private void LogError(string message)
    {
        UnityEngine.Debug.LogError($"[ABB RWS] {message}");
    }
    
    // Inspector GUI
    private void OnValidate()
    {
        if (port <= 0) port = 80;
        if (pollingIntervalMs < 10) pollingIntervalMs = 10;
        if (string.IsNullOrEmpty(ipAddress)) ipAddress = "127.0.0.1";
        if (string.IsNullOrEmpty(username)) username = "Default User";
        if (string.IsNullOrEmpty(taskName)) taskName = "T_ROB1";
    }
    
    // IRWSController implementation - public for interface
    public string IPAddress => ipAddress;
    public int Port => port;
    public string Username => username;
    public string Password => password;
    public string TaskName => taskName;
    public int PollingIntervalMs => pollingIntervalMs;
    public bool UseWebSocket => useWebSocketWhenAvailable;
    public bool ShowPerformanceMetrics => showPerformanceMetrics;
    
    private async Task PollIOSignalsAsync()
    {
        if (ioSignalsToMonitor.Count == 0) return;
        
        try
        {
            using (var client = CreateHttpClient())
            {
                foreach (var signalDef in ioSignalsToMonitor)
                {
                    if (string.IsNullOrEmpty(signalDef.signalName)) continue;
                    
                    string url = $"http://{ipAddress}:{port}/rw/iosystem/signals/{signalDef.ioNetwork}/{signalDef.ioDevice}/{signalDef.signalName}";
                    
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        bool currentState = ParseIOSignalState(content, signalDef.signalType);
                        
                        // Check if state changed
                        bool stateChanged = false;
                        if (ioSignalStates.ContainsKey(signalDef.signalName))
                        {
                            stateChanged = ioSignalStates[signalDef.signalName] != currentState;
                        }
                        else
                        {
                            stateChanged = true; // First time reading this signal
                        }
                        
                        ioSignalStates[signalDef.signalName] = currentState;
                        
                        if (stateChanged)
                        {
                            if (enableDebugLogging)
                            {
                                LogInfo($"I/O Signal changed: {signalDef.signalName} = {currentState}");
                            }
                            
                            if (safetyLogger != null)
                            {
                                string rapidContext = rapidStatusInitialized ? $", RAPID: {GetRapidContext()}" : "";
                                safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                                    "I/O Signal State Change",
                                    $"Signal: {signalDef.signalName}, State: {currentState}, Type: {signalDef.signalType}{rapidContext}");
                            }
                            
                            OnIOSignalChanged?.Invoke(signalDef.signalName, currentState);
                            
                            if (signalDef.isGripperSignal)
                            {
                                OnGripperSignalReceived?.Invoke(signalDef.signalName, currentState);
                            }
                        }
                    }
                    else
                    {
                        if (enableDebugLogging)
                        {
                            LogWarning($"Failed to read I/O signal {signalDef.signalName}: {response.StatusCode}");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogError($"I/O polling failed: {e.Message}");
            }
        }
    }
    
    private bool ParseIOSignalState(string xmlResponse, ABB.RWS.IOSignalType signalType)
    {
        try
        {
            if (signalType == ABB.RWS.IOSignalType.DigitalInput || signalType == ABB.RWS.IOSignalType.DigitalOutput)
            {
                string searchPattern = "<lvalue>";
                int startIndex = xmlResponse.IndexOf(searchPattern);
                if (startIndex >= 0)
                {
                    startIndex += searchPattern.Length;
                    int endIndex = xmlResponse.IndexOf("</lvalue>", startIndex);
                    if (endIndex > startIndex)
                    {
                        string value = xmlResponse.Substring(startIndex, endIndex - startIndex).Trim();
                        return value == "1" || value.ToLower() == "true";
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to parse I/O signal state: {e.Message}");
            }
        }
        
        return false;
    }
    
    [ContextMenu("Add Sample Gripper Signals")]
    public void AddGripperSignal()
    {
        ioSignalsToMonitor.Clear();
        
        ioSignalsToMonitor.Add(new ABB.RWS.IOSignalDefinition
        {
            signalName = "DO_GripperOpen",
            ioNetwork = "Local",
            ioDevice = "DRV_1",
            signalType = ABB.RWS.IOSignalType.DigitalOutput,
            isGripperSignal = true,
            description = "Gripper open command"
        });
        
        ioSignalsToMonitor.Add(new ABB.RWS.IOSignalDefinition
        {
            signalName = "DO_GripperClose",
            ioNetwork = "Local",
            ioDevice = "DRV_1",
            signalType = ABB.RWS.IOSignalType.DigitalOutput,
            isGripperSignal = true,
            description = "Gripper close command"
        });
        
        LogInfo("Sample gripper signals added to monitor list");
    }
    
    [ContextMenu("Test RAPID Status")]
    public async void TestRapidStatus()
    {
        if (!IsConnected)
        {
            LogWarning("Cannot test RAPID status: Not connected to robot controller");
            return;
        }
        
        LogInfo("Testing RAPID status endpoints...");
        await UpdateRapidProgramStatusAsync();
        
        LogInfo($"RAPID Status Test Results:");
        LogInfo($"Current Context: {GetRapidContext()}");
        LogInfo($"Program Pointer: {programPointer}");
    }
}

// IOSignalDefinition and IOSignalType moved to ABB.RWS namespace

// Custom ReadOnly attribute for inspector
public class ReadOnlyAttribute : PropertyAttribute { }
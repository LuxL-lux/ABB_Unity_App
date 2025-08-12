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

[AddComponentMenu("ABB/ABB Robot Web Services Controller")]
[RequireComponent(typeof(Controller))]
public class ABBRobotWebServicesController : MonoBehaviour
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
    
    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool showPerformanceMetrics = true;
    [SerializeField] private bool logJointUpdates = false;
    
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
    
    [Header("Joint Limit Monitoring (Read Only)")]
    [SerializeField, ReadOnly] private bool[] jointLimitWarnings = new bool[6];
    [SerializeField, ReadOnly] private float[] jointLimitPercentages = new float[6];
    [SerializeField, ReadOnly] private float[] jointMinLimits = new float[6];
    [SerializeField, ReadOnly] private float[] jointMaxLimits = new float[6];
    [SerializeField, ReadOnly] private string jointLimitStatus = "OK";
    
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
    private readonly TimeSpan rapidStatusUpdateInterval = TimeSpan.FromSeconds(2); // Update every 2 seconds
    
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
    
    internal void OnConnectionEstablished(bool usingWebSocket)
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
    
    internal void OnConnectionLost(string error = "")
    {
        connectionStatus = ConnectionStatus.Error;
        lastErrorMessage = error;
        isUsingWebSocket = false;
        
        // Log connection loss
        if (safetyLogger != null)
        {
            safetyLogger.LogError(ABBSafetyLogger.LogCategory.RWS, 
                "RWS Connection lost", 
                $"Error: {error}, Target: {ipAddress}:{port}");
        }
        
        LogError($"Connection lost: {error}");
        OnDisconnected?.Invoke();
        OnError?.Invoke(error);
    }
    
    internal void OnDataReceived(double[] jointData)
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
                // Get multiple status endpoints in parallel
                var tasks = new Task<string>[]
                {
                    GetRapidDataAsync(client, "/rw/rapid/tasks"), // Task list
                    GetRapidDataAsync(client, "/rw/panel/ctrlstate"), // Controller state
                    GetRapidDataAsync(client, "/rw/panel/opmode"), // Operation mode
                    GetRapidDataAsync(client, $"/rw/rapid/tasks/{taskName}") // Specific task info
                };
                
                var results = await Task.WhenAll(tasks);
                
                // Parse results
                ParseRapidStatus(results[0], results[1], results[2], results[3]);
            }
        }
        catch (Exception e)
        {
            // Don't spam errors for status updates
            if (enableDebugLogging)
            {
                LogWarning($"Failed to update RAPID status: {e.Message}");
            }
        }
    }
    
    private async Task<string> GetRapidDataAsync(HttpClient client, string endpoint)
    {
        try
        {
            string url = $"http://{ipAddress}:{port}{endpoint}";
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch (Exception e)
        {
            if (enableDebugLogging)
            {
                LogWarning($"Failed to get data from {endpoint}: {e.Message}");
            }
        }
        return "";
    }
    
    private void ParseRapidStatus(string tasksData, string ctrlStateData, string opModeData, string taskData)
    {
        try
        {
            string newProgramName = "Unknown";
            string newExecutionState = "Unknown";
            string newOperationMode = "Unknown";
            string newControllerState = "Unknown";
            
            // Parse controller state
            if (!string.IsNullOrEmpty(ctrlStateData))
            {
                if (ctrlStateData.Contains("ctrlstate"))
                {
                    // Extract controller state from response
                    newControllerState = ExtractValueFromResponse(ctrlStateData, "ctrlstate");
                }
            }
            
            // Parse operation mode
            if (!string.IsNullOrEmpty(opModeData))
            {
                if (opModeData.Contains("opmode"))
                {
                    newOperationMode = ExtractValueFromResponse(opModeData, "opmode");
                }
            }
            
            // Parse task information
            if (!string.IsNullOrEmpty(taskData))
            {
                if (taskData.Contains("excstate"))
                {
                    newExecutionState = ExtractValueFromResponse(taskData, "excstate");
                }
                if (taskData.Contains("name"))
                {
                    newProgramName = ExtractValueFromResponse(taskData, "name");
                }
            }
            
            // Update status if changed
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
            
            if (statusChanged)
            {
                string statusMessage = $"Program: {currentProgramName}, State: {executionState}, Mode: {operationMode}, Controller: {controllerState}";
                LogInfo($"RAPID Status: {statusMessage}");
                OnRapidStatusChanged?.Invoke(statusMessage);
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
            // Simple extraction for common patterns
            string searchPattern = $"{key}\":\"";
            int startIndex = response.IndexOf(searchPattern);
            if (startIndex >= 0)
            {
                startIndex += searchPattern.Length;
                int endIndex = response.IndexOf('"', startIndex);
                if (endIndex > startIndex)
                {
                    return response.Substring(startIndex, endIndex - startIndex);
                }
            }
            
            // Try XML pattern
            searchPattern = $"<{key}>";
            string endPattern = $"</{key}>";
            startIndex = response.IndexOf(searchPattern);
            if (startIndex >= 0)
            {
                startIndex += searchPattern.Length;
                int endIndex = response.IndexOf(endPattern, startIndex);
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
                LogWarning($"Failed to extract {key} from response: {e.Message}");
            }
        }
        return "Unknown";
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
    
    internal void OnConnectionStopped()
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
    
    // Properties for internal access by ABBDataStream
    internal string IPAddress => ipAddress;
    internal int Port => port;
    internal string Username => username;
    internal string Password => password;
    internal string TaskName => taskName;
    internal int PollingIntervalMs => pollingIntervalMs;
    internal bool UseWebSocket => useWebSocketWhenAvailable;
    internal bool ShowPerformanceMetrics => showPerformanceMetrics;
}

// Custom ReadOnly attribute for inspector
public class ReadOnlyAttribute : PropertyAttribute { }
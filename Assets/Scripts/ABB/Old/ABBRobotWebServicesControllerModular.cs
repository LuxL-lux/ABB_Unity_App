/****************************************************************************
ABB Robot Web Services Controller (Modular) - Main controller using modular components
****************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using Preliy.Flange;
using ABB.RWS;

[AddComponentMenu("ABB/ABB Robot Web Services Controller (Modular)")]
[RequireComponent(typeof(Controller))]
public class ABBRobotWebServicesControllerModular : MonoBehaviour, IRWSApiClient, IRWSController
{
    [Header("Connection Settings")]
    [SerializeField] private RWSConnectionSettings connectionSettings = new RWSConnectionSettings();
    
    [Header("Data Update Settings")]
    [SerializeField] private int pollingIntervalMs = 100;
    [SerializeField] private bool useWebSocketWhenAvailable = true;
    [SerializeField] private bool autoStartOnEnable = true;
    
    [Header("Monitoring Settings")]
    [SerializeField] private bool enableRapidMonitoring = true;
    [SerializeField] private bool enableIOMonitoring = true;
    [SerializeField] private float rapidStatusUpdateInterval = 1.5f;
    [SerializeField] private float ioPollingInterval = 0.5f;
    [SerializeField] private string taskName = "T_ROB1";
    
    [Header("I/O Signal Configuration")]
    [SerializeField] private List<ABB.RWS.IOSignalDefinition> ioSignalsToMonitor = new List<ABB.RWS.IOSignalDefinition>();
    
    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogging = true;
    [SerializeField] private bool showPerformanceMetrics = true;
    [SerializeField] private bool logJointUpdates = false;
    
    [Header("Status (Read Only)")]
    [SerializeField, ReadOnly] private ConnectionStatus connectionStatus = ConnectionStatus.Disconnected;
    [SerializeField, ReadOnly] private bool isUsingWebSocket = false;
    [SerializeField, ReadOnly] private double updateFrequencyHz = 0;
    [SerializeField, ReadOnly] private int totalUpdatesReceived = 0;
    [SerializeField, ReadOnly] private string lastErrorMessage = "";
    
    // Modular components
    private ABBRWSApiClient apiClient;
    private ABBRapidStatusMonitor rapidMonitor;
    private ABBIOSignalMonitor ioMonitor;
    private ABBJointLimitMonitor jointLimitMonitor;
    
    // Core components
    private Controller flangeController;
    private ABBDataStream dataStream;
    private ABBSafetyLogger safetyLogger;
    
    // Data management
    private readonly float[] currentJointAngles = new float[6];
    private readonly object dataLock = new object();
    private readonly Queue<float[]> pendingJointUpdates = new Queue<float[]>();
    private readonly object updateQueueLock = new object();
    private bool hasNewData = false;
    
    // Timing
    private DateTime lastRapidUpdate = DateTime.MinValue;
    private DateTime lastIOUpdate = DateTime.MinValue;
    private long lastUpdateTimestamp = 0;
    
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
        Stopping
    }
    
    // Public properties
    public ConnectionStatus Status => connectionStatus;
    public bool IsConnected => connectionStatus == ConnectionStatus.Connected;
    public double UpdateFrequency => updateFrequencyHz;
    
    // IRWSApiClient implementation
    public string IPAddress => connectionSettings.ipAddress;
    public int Port => connectionSettings.port;
    public string Username => connectionSettings.username;
    public string Password => connectionSettings.password;
    
    // IRWSController implementation - TaskName property
    public string TaskName => taskName;
    
    // RAPID Status properties
    public string CurrentProgramName => rapidMonitor?.CurrentStatus?.programName ?? "Unknown";
    public string ExecutionState => rapidMonitor?.CurrentStatus?.executionState ?? "Unknown";
    public string OperationMode => rapidMonitor?.CurrentStatus?.operationMode ?? "Unknown";
    public string ControllerState => rapidMonitor?.CurrentStatus?.controllerState ?? "Unknown";
    public string GetRapidContext() => rapidMonitor?.CurrentStatus?.GetFormattedContext() ?? "RAPID monitor not available";
    
    // Joint data properties
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
    
    // Joint limit properties
    public bool[] JointLimitWarnings => jointLimitMonitor?.CurrentStatus?.limitWarnings ?? new bool[6];
    public string JointLimitStatus => jointLimitMonitor?.CurrentStatus?.overallStatus ?? "Unknown";
    
    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<float[]> OnJointDataReceived;
    public event Action<string> OnError;
    public event Action<string> OnRapidStatusChanged;
    public event Action<int, bool> OnJointLimitWarning;
    public event Action<string, bool> OnGripperSignalReceived;
    public event Action<string, bool> OnIOSignalChanged;
    
    private void Awake()
    {
        // Initialize core components
        flangeController = GetComponent<Controller>();
        safetyLogger = ABBSafetyLogger.Instance;
        
        if (flangeController == null)
        {
            LogError("Flange Controller component not found!");
        }
        
        // Initialize modular components
        InitializeModularComponents();
        
        // Initialize data stream
        dataStream = new ABBDataStream(this);
    }
    
    private void InitializeModularComponents()
    {
        // Create API client
        apiClient = new ABBRWSApiClient(connectionSettings);
        
        // Create RAPID status monitor
        if (enableRapidMonitoring)
        {
            rapidMonitor = new ABBRapidStatusMonitor(apiClient, taskName, enableDebugLogging);
            rapidMonitor.OnStatusChanged += OnRapidStatusChangedInternal;
            rapidMonitor.OnError += OnRapidMonitorError;
        }
        
        // Create I/O signal monitor
        if (enableIOMonitoring)
        {
            ioMonitor = new ABBIOSignalMonitor(apiClient, enableDebugLogging);
            ioMonitor.OnSignalChanged += OnIOSignalChangedInternal;
            ioMonitor.OnGripperSignalReceived += OnGripperSignalReceivedInternal;
            ioMonitor.OnError += OnIOMonitorError;
            
            // Add configured signals
            foreach (var signal in ioSignalsToMonitor)
            {
                ioMonitor.AddSignal(signal);
            }
        }
        
        // Create joint limit monitor
        if (flangeController != null)
        {
            jointLimitMonitor = new ABBJointLimitMonitor(flangeController, enableDebugLogging);
            jointLimitMonitor.OnJointLimitWarning += OnJointLimitWarningInternal;
        }
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
        // Process pending joint updates
        ProcessPendingUpdates();
        
        if (!IsConnected) return;
        
        // Update RAPID status
        if (enableRapidMonitoring && rapidMonitor != null && 
            DateTime.Now - lastRapidUpdate > TimeSpan.FromSeconds(rapidStatusUpdateInterval))
        {
            _ = rapidMonitor.UpdateStatusAsync();
            lastRapidUpdate = DateTime.Now;
        }
        
        // Update I/O signals
        if (enableIOMonitoring && ioMonitor != null && 
            DateTime.Now - lastIOUpdate > TimeSpan.FromSeconds(ioPollingInterval))
        {
            _ = ioMonitor.PollSignalsAsync();
            lastIOUpdate = DateTime.Now;
        }
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
        apiClient?.SetConnectionState(false);
        dataStream = new ABBDataStream(this);
        dataStream.Start();
        
        LogInfo($"Starting ABB Robot Web Services connection to {IPAddress}:{Port}");
    }
    
    [ContextMenu("Stop Connection")]
    public void StopConnection()
    {
        if (connectionStatus == ConnectionStatus.Disconnected) return;
        
        connectionStatus = ConnectionStatus.Stopping;
        apiClient?.SetConnectionState(false);
        dataStream?.Stop();
        
        LogInfo("Stopping ABB Robot Web Services connection");
    }
    
    [ContextMenu("Add Sample Gripper Signals")]
    public void AddGripperSignal()
    {
        ioMonitor?.AddGripperSignal();
        
        // Also update the serialized list for inspector visibility
        ioSignalsToMonitor.Clear();
        if (ioMonitor != null)
        {
            foreach (var signal in ioMonitor.SignalsToMonitor)
            {
                ioSignalsToMonitor.Add(signal);
            }
        }
        
        LogInfo("Sample gripper signals added to monitor list");
    }

    [ContextMenu("Test JSON Response Format")]
    public async void TestJSONResponseFormat()
    {
        if (!IsConnected)
        {
            LogWarning("Cannot test JSON format: Not connected to robot controller");
            return;
        }

        LogInfo("Testing JSON response format...");
        
        try
        {
            using (var client = CreateHttpClient())
            {
                // Test different endpoints to verify JSON format
                string[] testEndpoints = {
                    "/rw/system",
                    $"/rw/rapid/tasks/{taskName}",
                    "/rw/panel/ctrlstate",
                    "/rw/panel/opmode"
                };

                foreach (string endpoint in testEndpoints)
                {
                    try
                    {
                        string url = BuildUrl(endpoint);
                        LogInfo($"Testing endpoint: {url}");
                        
                        var response = await client.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            string content = await response.Content.ReadAsStringAsync();
                            
                            // Check if response is JSON
                            bool isJson = content.TrimStart().StartsWith("{") || content.TrimStart().StartsWith("[");
                            bool isXml = content.TrimStart().StartsWith("<");
                            
                            LogInfo($"Endpoint {endpoint}: Status={response.StatusCode}, " +
                                   $"Format={(isJson ? "JSON" : isXml ? "XML" : "Unknown")}, " +
                                   $"Length={content.Length}, " +
                                   $"ContentType={response.Content.Headers.ContentType}");
                                   
                            if (enableDebugLogging && content.Length < 500)
                            {
                                LogInfo($"Response preview: {content.Substring(0, System.Math.Min(200, content.Length))}...");
                            }
                        }
                        else
                        {
                            LogWarning($"Endpoint {endpoint} returned {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Failed to test endpoint {endpoint}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            LogError($"JSON format test failed: {e.Message}");
        }
    }
    
    // IRWSApiClient implementation
    public System.Net.Http.HttpClient CreateHttpClient()
    {
        return apiClient?.CreateHttpClient();
    }
    
    public string BuildUrl(string endpoint)
    {
        return apiClient?.BuildUrl(endpoint);
    }
    
    // Public event handlers for IRWSController interface
    public void OnConnectionEstablished(bool usingWebSocket)
    {
        connectionStatus = ConnectionStatus.Connected;
        isUsingWebSocket = usingWebSocket;
        lastErrorMessage = "";
        apiClient?.SetConnectionState(true);
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                "RWS Connection established",
                $"Method: {(usingWebSocket ? "WebSocket" : "HTTP Polling")}, Target: {IPAddress}:{Port}");
        }
        
        LogInfo($"Connection established. Using {(usingWebSocket ? "WebSocket" : "HTTP polling")}");
        OnConnected?.Invoke();
    }
    
    public void OnConnectionLost(string error = "")
    {
        connectionStatus = ConnectionStatus.Error;
        lastErrorMessage = error;
        isUsingWebSocket = false;
        apiClient?.SetConnectionState(false);
        
        if (safetyLogger != null)
        {
            string rapidContext = rapidMonitor?.IsInitialized == true ? $", RAPID: {GetRapidContext()}" : "";
            safetyLogger.LogError(ABBSafetyLogger.LogCategory.RWS,
                "RWS Connection lost",
                $"Error: {error}, Target: {IPAddress}:{Port}{rapidContext}");
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
        
        lock (updateQueueLock)
        {
            pendingJointUpdates.Enqueue(jointAnglesFloat);
            hasNewData = true;
        }
        
        UpdatePerformanceMetrics();
    }
    
    private void ProcessPendingUpdates()
    {
        if (!hasNewData) return;
        
        float[] latestJointAngles = null;
        
        lock (updateQueueLock)
        {
            if (pendingJointUpdates.Count > 0)
            {
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
            lock (dataLock)
            {
                Array.Copy(latestJointAngles, currentJointAngles, 6);
            }
            
            UpdateFlangeController(latestJointAngles);
            jointLimitMonitor?.CheckJointLimits(latestJointAngles);
            
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
            var jointTarget = new JointTarget(jointAngles);
            flangeController.MechanicalGroup.SetJoints(jointTarget, notify: true);
        }
        catch (Exception e)
        {
            LogError($"Failed to update Flange Controller: {e.Message}");
        }
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
    
    // Event handlers for modular components
    private void OnRapidStatusChangedInternal(ABBRapidStatusMonitor.RapidStatus status)
    {
        string statusMessage = status.GetFormattedContext();
        OnRapidStatusChanged?.Invoke(statusMessage);
        
        if (safetyLogger != null)
        {
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                "RAPID Status Changed",
                $"{statusMessage}, Pointer: {status.programPointer}");
        }
    }
    
    private void OnRapidMonitorError(string error)
    {
        if (safetyLogger != null)
        {
            safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.RWS,
                "RAPID Monitor Error",
                error);
        }
    }
    
    private void OnIOSignalChangedInternal(string signalName, bool signalState)
    {
        OnIOSignalChanged?.Invoke(signalName, signalState);
        
        if (safetyLogger != null)
        {
            string rapidContext = rapidMonitor?.IsInitialized == true ? $", RAPID: {GetRapidContext()}" : "";
            safetyLogger.LogInfo(ABBSafetyLogger.LogCategory.RWS,
                "I/O Signal State Change",
                $"Signal: {signalName}, State: {signalState}{rapidContext}");
        }
    }
    
    private void OnGripperSignalReceivedInternal(string signalName, bool signalState)
    {
        OnGripperSignalReceived?.Invoke(signalName, signalState);
    }
    
    private void OnIOMonitorError(string error)
    {
        if (safetyLogger != null)
        {
            safetyLogger.LogWarning(ABBSafetyLogger.LogCategory.RWS,
                "I/O Monitor Error",
                error);
        }
    }
    
    private void OnJointLimitWarningInternal(int jointIndex, bool isWarning)
    {
        OnJointLimitWarning?.Invoke(jointIndex, isWarning);
    }
    
    public void OnConnectionStopped()
    {
        connectionStatus = ConnectionStatus.Disconnected;
        isUsingWebSocket = false;
        updateFrequencyHz = 0;
        apiClient?.SetConnectionState(false);
        
        LogInfo("Connection stopped");
        OnDisconnected?.Invoke();
    }
    
    // Logging methods
    private void LogInfo(string message)
    {
        if (enableDebugLogging)
            Debug.Log($"[ABB RWS Modular] {message}");
    }
    
    private void LogWarning(string message)
    {
        if (enableDebugLogging)
            Debug.LogWarning($"[ABB RWS Modular] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[ABB RWS Modular] {message}");
    }
    
    // Inspector validation
    private void OnValidate()
    {
        if (connectionSettings.port <= 0) connectionSettings.port = 80;
        if (pollingIntervalMs < 10) pollingIntervalMs = 10;
        if (string.IsNullOrEmpty(connectionSettings.ipAddress)) connectionSettings.ipAddress = "127.0.0.1";
        if (string.IsNullOrEmpty(connectionSettings.username)) connectionSettings.username = "Default User";
        if (string.IsNullOrEmpty(taskName)) taskName = "T_ROB1";
    }
    
    // IRWSController implementation - public for interface
    public int PollingIntervalMs => pollingIntervalMs;
    public bool UseWebSocket => useWebSocketWhenAvailable;
    public bool ShowPerformanceMetrics => showPerformanceMetrics;
}
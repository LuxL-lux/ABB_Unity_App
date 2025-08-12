/****************************************************************************
ABB Safety Logger - Comprehensive logging for RWS operations and safety events
****************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Preliy.Flange;

[AddComponentMenu("ABB/ABB Safety Logger")]
public class ABBSafetyLogger : MonoBehaviour
{
    [Header("Logging Configuration")]
    [SerializeField] private bool enableFileLogging = true;
    [SerializeField] private bool enableConsoleLogging = true;
    [SerializeField] private string logDirectory = "Logs";
    [SerializeField] private string logPrefix = "ABB_Safety";
    [SerializeField] private int maxLogFiles = 10;
    
    [Header("Log Categories")]
    [SerializeField] private bool logRWSCommands = true;
    [SerializeField] private bool logCollisions = true;
    [SerializeField] private bool logJointLimits = true;
    [SerializeField] private bool logGripperOperations = true;
    [SerializeField] private bool logSingularities = true;
    [SerializeField] private bool logPositionData = true;
    
    [Header("Status")]
    [SerializeField, ReadOnly] private string currentLogFile = "";
    [SerializeField, ReadOnly] private int logEntriesWritten = 0;
    [SerializeField, ReadOnly] private DateTime sessionStartTime;
    
    private StreamWriter logWriter;
    private readonly Queue<LogEntry> logQueue = new Queue<LogEntry>();
    private readonly object logLock = new object();
    
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    public enum LogCategory
    {
        RWS,
        Collision,
        JointLimit,
        Gripper,
        Singularity,
        Position,
        System
    }
    
    [Serializable]
    private class LogEntry
    {
        public DateTime timestamp;
        public LogLevel level;
        public LogCategory category;
        public string message;
        public string details;
        public Vector3 robotPosition;
        public float[] jointAngles;
        public RAPIDContext rapidContext;
    }
    
    [Serializable]
    public class RAPIDContext
    {
        public string programName = "";
        public string currentInstruction = "";
        public int currentLine = -1;
        public string executionState = "";
        public string operationMode = "";
        public string activeTask = "";
        public string currentProcedure = "";
        public Vector3 robotTarget = Vector3.zero;
        public string movementType = "";
    }
    
    // Static instance for easy access
    public static ABBSafetyLogger Instance { get; private set; }
    
    // Events for external subscribers
    public event Action<LogLevel, LogCategory, string> OnLogEntry;
    public event Action<string> OnCriticalError;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        sessionStartTime = DateTime.Now;
        InitializeLogging();
    }
    
    private void Start()
    {
        LogInfo(LogCategory.System, "ABB Safety Logger initialized", 
               $"Session started at {sessionStartTime:yyyy-MM-dd HH:mm:ss}");
    }
    
    private void InitializeLogging()
    {
        if (!enableFileLogging) return;
        
        try
        {
            // Create logs directory inside project
            string fullLogDir = Path.Combine(Application.dataPath, "..", logDirectory);
            if (!Directory.Exists(fullLogDir))
            {
                Directory.CreateDirectory(fullLogDir);
            }
            
            // Clean up old log files
            CleanupOldLogs(fullLogDir);
            
            // Create new log file
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{logPrefix}_{timestamp}.log";
            currentLogFile = Path.Combine(fullLogDir, fileName);
            
            logWriter = new StreamWriter(currentLogFile, true);
            logWriter.WriteLine($"=== ABB Safety Logger Session Started: {sessionStartTime:yyyy-MM-dd HH:mm:ss} ===");
            logWriter.WriteLine($"Unity Version: {Application.unityVersion}");
            logWriter.WriteLine($"Platform: {Application.platform}");
            logWriter.WriteLine("================================================================================");
            logWriter.Flush();
            
            Debug.Log($"[ABB Safety Logger] Logging to: {currentLogFile}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ABB Safety Logger] Failed to initialize file logging: {e.Message}");
            enableFileLogging = false;
        }
    }
    
    private void CleanupOldLogs(string logDir)
    {
        try
        {
            var logFiles = Directory.GetFiles(logDir, $"{logPrefix}_*.log");
            if (logFiles.Length > maxLogFiles)
            {
                Array.Sort(logFiles);
                for (int i = 0; i < logFiles.Length - maxLogFiles; i++)
                {
                    File.Delete(logFiles[i]);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ABB Safety Logger] Failed to cleanup old logs: {e.Message}");
        }
    }
    
    // Public logging methods
    public void LogInfo(LogCategory category, string message, string details = "")
    {
        Log(LogLevel.Info, category, message, details);
    }
    
    public void LogWarning(LogCategory category, string message, string details = "")
    {
        Log(LogLevel.Warning, category, message, details);
    }
    
    public void LogError(LogCategory category, string message, string details = "")
    {
        Log(LogLevel.Error, category, message, details);
    }
    
    public void LogCritical(LogCategory category, string message, string details = "")
    {
        Log(LogLevel.Critical, category, message, details);
        OnCriticalError?.Invoke($"[{category}] {message}");
    }
    
    // RWS specific logging methods
    public void LogRWSCommand(string command, string url, bool success, string response = "")
    {
        if (!logRWSCommands) return;
        
        LogLevel level = success ? LogLevel.Info : LogLevel.Error;
        string message = $"RWS Command: {command}";
        string details = $"URL: {url}\nSuccess: {success}\nResponse: {response}";
        
        Log(level, LogCategory.RWS, message, details);
    }
    
    public void LogCollision(string robotPart, string hitObject, Vector3 position)
    {
        if (!logCollisions) return;
        
        string message = $"Collision detected: {robotPart} -> {hitObject}";
        string details = $"Position: {position}\nTime: {Time.time:F2}s";
        
        var entry = CreateLogEntry(LogLevel.Warning, LogCategory.Collision, message, details);
        entry.robotPosition = position;
        
        WriteLog(entry);
    }
    
    public void LogJointLimit(int jointIndex, float currentAngle, float limitAngle, bool isMax)
    {
        if (!logJointLimits) return;
        
        string limitType = isMax ? "maximum" : "minimum";
        string message = $"Joint {jointIndex + 1} approaching {limitType} limit";
        string details = $"Current: {currentAngle:F2}°, Limit: {limitAngle:F2}°";
        
        Log(LogLevel.Warning, LogCategory.JointLimit, message, details);
    }
    
    public void LogGripperOperation(string operation, bool success, string objectName = "")
    {
        if (!logGripperOperations) return;
        
        LogLevel level = success ? LogLevel.Info : LogLevel.Error;
        string message = $"Gripper {operation}";
        string details = success ? 
            $"Object: {objectName}" : 
            $"Failed - Object: {objectName}";
        
        Log(level, LogCategory.Gripper, message, details);
    }
    
    public void LogSingularity(Vector3 tcpPosition, float[] jointAngles, float manipulability)
    {
        if (!logSingularities) return;
        
        string message = "Singularity detected";
        string details = $"TCP Position: {tcpPosition}\nManipulability: {manipulability:F4}";
        
        var entry = CreateLogEntry(LogLevel.Warning, LogCategory.Singularity, message, details);
        entry.robotPosition = tcpPosition;
        entry.jointAngles = (float[])jointAngles.Clone();
        
        WriteLog(entry);
    }
    
    private void Log(LogLevel level, LogCategory category, string message, string details)
    {
        // Skip if category is disabled
        if (!IsCategoryEnabled(category)) return;
        
        var entry = CreateLogEntry(level, category, message, details);
        WriteLog(entry);
    }
    
    private bool IsCategoryEnabled(LogCategory category)
    {
        return category switch
        {
            LogCategory.RWS => logRWSCommands,
            LogCategory.Collision => logCollisions,
            LogCategory.JointLimit => logJointLimits,
            LogCategory.Gripper => logGripperOperations,
            LogCategory.Singularity => logSingularities,
            LogCategory.Position => logPositionData,
            LogCategory.System => true,
            _ => true
        };
    }
    
    private LogEntry CreateLogEntry(LogLevel level, LogCategory category, string message, string details)
    {
        var entry = new LogEntry
        {
            timestamp = DateTime.Now,
            level = level,
            category = category,
            message = message,
            details = details
        };
        
        // Try to get current robot position and joint angles
        var controller = FindFirstObjectByType<Controller>();
        var abbController = FindFirstObjectByType<ABBRobotWebServicesController>();
        
        if (controller != null)
        {
            // Get TCP position
            var mechanicalGroup = controller.MechanicalGroup;
            if (mechanicalGroup?.Robot != null)
            {
                entry.robotPosition = mechanicalGroup.Robot.transform.position;
            }
            
            // Get joint angles from ABB controller if available
            if (abbController != null && abbController.IsConnected)
            {
                entry.jointAngles = abbController.JointAngles;
            }
        }
        
        // Get RAPID context from ABB controller
        if (abbController != null && abbController.IsConnected)
        {
            entry.rapidContext = GetCurrentRAPIDContext(abbController);
        }
        
        return entry;
    }
    
    private RAPIDContext GetCurrentRAPIDContext(ABBRobotWebServicesController abbController)
    {
        var context = new RAPIDContext();
        
        // Get basic program status (already available in controller)
        context.programName = abbController.CurrentProgramName;
        context.executionState = abbController.ExecutionState;
        context.operationMode = abbController.OperationMode;
        context.activeTask = abbController.TaskName;
        
        // Fetch current RAPID instruction via RWS API
        _ = FetchCurrentRAPIDInstruction(abbController, context);
        
        return context;
    }
    
    private async System.Threading.Tasks.Task FetchCurrentRAPIDInstruction(ABBRobotWebServicesController abbController, RAPIDContext context)
    {
        try
        {
            using (var client = abbController.CreateHttpClient())
            {
                // Get current RAPID execution pointer
                string pointerUrl = $"http://{abbController.IPAddress}:{abbController.Port}/rw/rapid/execution/ctrlexecstate";
                var response = await client.GetAsync(pointerUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ParseExecutionState(content, context);
                }
                
                // Get current program pointer position
                string programUrl = $"http://{abbController.IPAddress}:{abbController.Port}/rw/rapid/tasks/{abbController.TaskName}/pcp";
                response = await client.GetAsync(programUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ParseProgramPointer(content, context);
                }
                
                // Get current robot target if in motion
                string robotTargetUrl = $"http://{abbController.IPAddress}:{abbController.Port}/rw/motionsystem/mechunits/ROB_1/robtargets/CRobT";
                response = await client.GetAsync(robotTargetUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    ParseRobotTarget(content, context);
                }
            }
        }
        catch (System.Exception e)
        {
            // Don't log errors for RAPID context fetching to avoid log spam
            context.currentInstruction = $"Error fetching context: {e.Message}";
        }
    }
    
    private void ParseExecutionState(string xmlContent, RAPIDContext context)
    {
        try
        {
            // Parse XML response for execution state
            var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
            var stateElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "ctrlexecstate");
            if (stateElement != null)
            {
                context.executionState = stateElement.Value;
            }
        }
        catch (System.Exception)
        {
            context.executionState = "Parse error";
        }
    }
    
    private void ParseProgramPointer(string xmlContent, RAPIDContext context)
    {
        try
        {
            // Parse XML response for program counter position
            var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
            
            // Look for module and routine information
            var moduleElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "module");
            var routineElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "routine");
            var lineElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "line");
            
            if (moduleElement != null && routineElement != null)
            {
                context.currentProcedure = $"{moduleElement.Value}.{routineElement.Value}";
            }
            
            if (lineElement != null && int.TryParse(lineElement.Value, out int line))
            {
                context.currentLine = line;
            }
            
            // Try to get the actual instruction text (this might require additional API calls)
            context.currentInstruction = $"Line {context.currentLine} in {context.currentProcedure}";
        }
        catch (System.Exception)
        {
            context.currentInstruction = "Parse error";
        }
    }
    
    private void ParseRobotTarget(string xmlContent, RAPIDContext context)
    {
        try
        {
            // Parse XML response for robot target position
            var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
            
            // Look for X, Y, Z coordinates
            var xElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "x");
            var yElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "y");
            var zElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "z");
            
            if (xElement != null && yElement != null && zElement != null)
            {
                if (float.TryParse(xElement.Value, out float x) &&
                    float.TryParse(yElement.Value, out float y) &&
                    float.TryParse(zElement.Value, out float z))
                {
                    context.robotTarget = new Vector3(x / 1000f, z / 1000f, y / 1000f); // Convert mm to m and swap Y/Z
                }
            }
            
            // Try to determine movement type
            var movementElement = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "movetype");
            if (movementElement != null)
            {
                context.movementType = movementElement.Value;
            }
        }
        catch (System.Exception)
        {
            context.movementType = "Parse error";
        }
    }
    
    private void WriteLog(LogEntry entry)
    {
        lock (logLock)
        {
            logQueue.Enqueue(entry);
        }
        
        // Process on main thread
        ProcessLogQueue();
    }
    
    private void ProcessLogQueue()
    {
        lock (logLock)
        {
            while (logQueue.Count > 0)
            {
                var entry = logQueue.Dequeue();
                
                // Console logging
                if (enableConsoleLogging)
                {
                    string consoleMessage = $"[{entry.level}][{entry.category}] {entry.message}";
                    if (!string.IsNullOrEmpty(entry.details))
                        consoleMessage += $" - {entry.details}";
                    
                    switch (entry.level)
                    {
                        case LogLevel.Error:
                        case LogLevel.Critical:
                            Debug.LogError(consoleMessage);
                            break;
                        case LogLevel.Warning:
                            Debug.LogWarning(consoleMessage);
                            break;
                        default:
                            Debug.Log(consoleMessage);
                            break;
                    }
                }
                
                // File logging
                if (enableFileLogging && logWriter != null)
                {
                    try
                    {
                        string logLine = FormatLogEntry(entry);
                        logWriter.WriteLine(logLine);
                        logWriter.Flush();
                        logEntriesWritten++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ABB Safety Logger] Failed to write log entry: {e.Message}");
                    }
                }
                
                // Fire event
                OnLogEntry?.Invoke(entry.level, entry.category, entry.message);
            }
        }
    }
    
    private string FormatLogEntry(LogEntry entry)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{entry.timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.level}] [{entry.category}] {entry.message}");
        
        if (!string.IsNullOrEmpty(entry.details))
            sb.AppendLine($"  Details: {entry.details}");
        
        if (entry.robotPosition != Vector3.zero)
            sb.AppendLine($"  Robot Position: {entry.robotPosition}");
        
        if (entry.jointAngles != null && entry.jointAngles.Length > 0)
        {
            sb.Append("  Joint Angles: [");
            for (int i = 0; i < entry.jointAngles.Length; i++)
            {
                sb.Append($"{entry.jointAngles[i]:F2}°");
                if (i < entry.jointAngles.Length - 1) sb.Append(", ");
            }
            sb.AppendLine("]");
        }
        
        // Add RAPID context if available
        if (entry.rapidContext != null)
        {
            sb.AppendLine("  RAPID Context:");
            if (!string.IsNullOrEmpty(entry.rapidContext.programName))
                sb.AppendLine($"    Program: {entry.rapidContext.programName}");
            if (!string.IsNullOrEmpty(entry.rapidContext.currentInstruction))
                sb.AppendLine($"    Current Instruction: {entry.rapidContext.currentInstruction}");
            if (!string.IsNullOrEmpty(entry.rapidContext.currentProcedure))
                sb.AppendLine($"    Procedure: {entry.rapidContext.currentProcedure}");
            if (!string.IsNullOrEmpty(entry.rapidContext.executionState))
                sb.AppendLine($"    Execution State: {entry.rapidContext.executionState}");
            if (!string.IsNullOrEmpty(entry.rapidContext.operationMode))
                sb.AppendLine($"    Operation Mode: {entry.rapidContext.operationMode}");
            if (entry.rapidContext.robotTarget != Vector3.zero)
                sb.AppendLine($"    Robot Target: {entry.rapidContext.robotTarget}");
            if (!string.IsNullOrEmpty(entry.rapidContext.movementType))
                sb.AppendLine($"    Movement Type: {entry.rapidContext.movementType}");
        }
        
        sb.AppendLine("---");
        return sb.ToString();
    }
    
    private void OnDestroy()
    {
        if (logWriter != null)
        {
            LogInfo(LogCategory.System, "ABB Safety Logger shutting down", 
                   $"Total entries written: {logEntriesWritten}");
            
            logWriter.WriteLine($"=== Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            logWriter.Close();
            logWriter = null;
        }
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            LogInfo(LogCategory.System, "Application paused");
        }
        else
        {
            LogInfo(LogCategory.System, "Application resumed");
        }
    }
    
    // Context menu for testing
    [ContextMenu("Test Logging")]
    private void TestLogging()
    {
        LogInfo(LogCategory.System, "Test log entry", "This is a test from context menu");
        LogWarning(LogCategory.Collision, "Test collision", "Simulated collision for testing");
        LogError(LogCategory.RWS, "Test RWS error", "Simulated RWS error for testing");
    }
}
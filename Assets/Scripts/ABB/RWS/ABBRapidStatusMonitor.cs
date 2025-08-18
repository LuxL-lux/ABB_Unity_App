/****************************************************************************
ABB RAPID Status Monitor - Handles RAPID program status monitoring via RWS
****************************************************************************/

using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace ABB.RWS
{
    public class ABBRapidStatusMonitor
    {
        [Serializable]
        public class RapidStatus
        {
            public string programName = "Unknown";
            public string executionState = "Unknown";
            public string operationMode = "Unknown";
            public string controllerState = "Unknown";
            public string rapidTaskType = "Unknown";
            public bool rapidTaskActive = false;
            public string programPointer = "Unknown";
            public DateTime lastUpdate = DateTime.MinValue;
            
            public string GetFormattedContext()
            {
                return $"Program: {programName}, State: {executionState}, Mode: {operationMode}, Controller: {controllerState}, Active: {rapidTaskActive}";
            }
        }

        private readonly IRWSApiClient apiClient;
        private readonly string taskName;
        private readonly bool enableDebugLogging;
        private RapidStatus currentStatus = new RapidStatus();
        private bool isInitialized = false;

        // Events
        public event Action<RapidStatus> OnStatusChanged;
        public event Action<string> OnError;

        public ABBRapidStatusMonitor(IRWSApiClient apiClient, string taskName, bool enableDebugLogging = false)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.taskName = taskName;
            this.enableDebugLogging = enableDebugLogging;
        }

        public RapidStatus CurrentStatus => currentStatus;
        public bool IsInitialized => isInitialized;

        public async Task UpdateStatusAsync()
        {
            try
            {
                using (var client = apiClient.CreateHttpClient())
                {
                    // Get RAPID status endpoints using correct RWS API URLs
                    var tasks = new Task<(string data, string endpoint)>[]
                    {
                        GetRapidDataAsync(client, $"/rw/rapid/tasks/{taskName}"), // Specific task state
                        GetRapidDataAsync(client, "/rw/panel/ctrlstate"), // Controller state
                        GetRapidDataAsync(client, "/rw/panel/opmode"), // Operation mode  
                        GetRapidDataAsync(client, $"/rw/rapid/tasks/{taskName}/pcp"), // Program counter/pointer
                        GetRapidDataAsync(client, "/rw/rapid/execution") // Global execution state
                    };

                    var results = await Task.WhenAll(tasks);
                    
                    var newStatus = ParseRapidStatus(results);
                    
                    bool statusChanged = HasStatusChanged(currentStatus, newStatus);
                    currentStatus = newStatus;
                    currentStatus.lastUpdate = DateTime.Now;

                    if (!isInitialized)
                    {
                        isInitialized = true;
                        LogInfo("RAPID status monitoring initialized successfully");
                    }

                    if (statusChanged)
                    {
                        LogInfo($"RAPID Status Update: {currentStatus.GetFormattedContext()}");
                        OnStatusChanged?.Invoke(currentStatus);
                    }
                }
            }
            catch (Exception e)
            {
                string errorMsg = $"Failed to update RAPID status: {e.Message}";
                LogWarning(errorMsg);
                OnError?.Invoke(errorMsg);
            }
        }

        private async Task<(string data, string endpoint)> GetRapidDataAsync(HttpClient client, string endpoint)
        {
            try
            {
                var response = await client.GetAsync(apiClient.BuildUrl(endpoint));
                if (response.IsSuccessStatusCode)
                {
                    string data = await response.Content.ReadAsStringAsync();
                    return (data, endpoint);
                }
                else if (enableDebugLogging)
                {
                    LogWarning($"RWS API returned {response.StatusCode} for {endpoint}");
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

        private RapidStatus ParseRapidStatus((string data, string endpoint)[] results)
        {
            var status = new RapidStatus();

            foreach (var (data, endpoint) in results)
            {
                if (string.IsNullOrEmpty(data)) continue;

                try
                {
                    if (endpoint.Contains("/ctrlstate"))
                    {
                        // Parse controller state from panel endpoint
                        status.controllerState = ExtractValue(data, "ctrlstate") ?? 
                                                ExtractClassValue(data, "pnl-ctrlstate-ev") ?? "Unknown";
                    }
                    else if (endpoint.Contains("/opmode"))
                    {
                        // Parse operation mode from panel endpoint
                        status.operationMode = ExtractValue(data, "opmode") ?? 
                                             ExtractClassValue(data, "pnl-opmode-ev") ?? "Unknown";
                    }
                    else if (endpoint.Contains($"/tasks/{taskName}") && !endpoint.Contains("/pcp"))
                    {
                        // Parse task-specific information
                        status.executionState = ExtractValue(data, "excstate") ?? "Unknown";
                        status.programName = ExtractValue(data, "name") ?? taskName; // Fallback to task name
                        status.rapidTaskType = ExtractValue(data, "type") ?? "Unknown";
                        status.rapidTaskActive = ExtractBoolValue(data, "active");
                        
                        // Also try to get task state
                        string taskState = ExtractValue(data, "taskstate");
                        if (!string.IsNullOrEmpty(taskState) && taskState != "Unknown")
                        {
                            status.rapidTaskType = taskState; // Use taskstate as type if available
                        }
                    }
                    else if (endpoint.Contains("/pcp"))
                    {
                        string module = ExtractValue(data, "module");
                        string routine = ExtractValue(data, "routine");
                        if (!string.IsNullOrEmpty(module) && !string.IsNullOrEmpty(routine))
                        {
                            status.programPointer = $"{module}.{routine}";
                        }
                        else
                        {
                            status.programPointer = ExtractValue(data, "routine") ?? "Unknown";
                        }
                    }
                    else if (endpoint.Contains("/execution"))
                    {
                        string globalState = ExtractValue(data, "ctrlexecstate");
                        if (!string.IsNullOrEmpty(globalState) && status.executionState == "Unknown")
                        {
                            status.executionState = globalState;
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

            return status;
        }

        private bool HasStatusChanged(RapidStatus oldStatus, RapidStatus newStatus)
        {
            return oldStatus.programName != newStatus.programName ||
                   oldStatus.executionState != newStatus.executionState ||
                   oldStatus.operationMode != newStatus.operationMode ||
                   oldStatus.controllerState != newStatus.controllerState ||
                   oldStatus.rapidTaskType != newStatus.rapidTaskType ||
                   oldStatus.rapidTaskActive != newStatus.rapidTaskActive ||
                   oldStatus.programPointer != newStatus.programPointer;
        }

        private string ExtractValue(string response, string key)
        {
            try
            {
                // Enhanced JSON parsing - try multiple JSON patterns
                // Pattern 1: "key":"value"
                string jsonPattern1 = $"\"{key}\":\"";
                int startIndex = response.IndexOf(jsonPattern1);
                if (startIndex >= 0)
                {
                    startIndex += jsonPattern1.Length;
                    int endIndex = response.IndexOf('"', startIndex);
                    if (endIndex > startIndex)
                    {
                        return response.Substring(startIndex, endIndex - startIndex);
                    }
                }

                // Pattern 2: "key": "value" (with spaces)
                string jsonPattern2 = $"\"{key}\": \"";
                startIndex = response.IndexOf(jsonPattern2);
                if (startIndex >= 0)
                {
                    startIndex += jsonPattern2.Length;
                    int endIndex = response.IndexOf('"', startIndex);
                    if (endIndex > startIndex)
                    {
                        return response.Substring(startIndex, endIndex - startIndex);
                    }
                }

                // Pattern 3: key:"value" (without quotes on key - some RWS responses)
                string jsonPattern3 = $"{key}:\"";
                startIndex = response.IndexOf(jsonPattern3);
                if (startIndex >= 0)
                {
                    startIndex += jsonPattern3.Length;
                    int endIndex = response.IndexOf('"', startIndex);
                    if (endIndex > startIndex)
                    {
                        return response.Substring(startIndex, endIndex - startIndex);
                    }
                }

                // Fallback to XML parsing if JSON not found
                string startTag = $"<{key}>";
                string endTag = $"</{key}>";
                startIndex = response.IndexOf(startTag);
                if (startIndex >= 0)
                {
                    startIndex += startTag.Length;
                    int endIndex = response.IndexOf(endTag, startIndex);
                    if (endIndex > startIndex)
                    {
                        return response.Substring(startIndex, endIndex - startIndex).Trim();
                    }
                }

                // Fallback to attribute pattern
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
                    LogWarning($"Failed to extract {key}: {e.Message}");
                }
            }
            return null;
        }

        private bool ExtractBoolValue(string response, string key)
        {
            string value = ExtractValue(response, key);
            return !string.IsNullOrEmpty(value) && (value.ToLower() == "true" || value == "1" || value.ToLower() == "on");
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

        private void LogInfo(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[ABB RAPID Monitor] {message}");
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
                Debug.LogWarning($"[ABB RAPID Monitor] {message}");
        }
    }
}
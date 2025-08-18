/****************************************************************************
ABB I/O Signal Monitor - Handles I/O signal monitoring via RWS
****************************************************************************/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace ABB.RWS
{
    [Serializable]
    public class IOSignalDefinition
    {
        [SerializeField] public string signalName = "";
        [SerializeField] public string ioNetwork = "Local";
        [SerializeField] public string ioDevice = "DRV_1";
        [SerializeField] public IOSignalType signalType = IOSignalType.DigitalOutput;
        [SerializeField] public bool isGripperSignal = false;
        [SerializeField] public string description = "";
    }

    public enum IOSignalType
    {
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        AnalogOutput
    }

    [Serializable]
    public class IOSignalState
    {
        public string signalName;
        public bool currentState;
        public DateTime lastUpdate;
        public IOSignalDefinition definition;

        public IOSignalState(IOSignalDefinition def)
        {
            definition = def;
            signalName = def.signalName;
            currentState = false;
            lastUpdate = DateTime.MinValue;
        }
    }

    public class ABBIOSignalMonitor
    {
        private readonly IRWSApiClient apiClient;
        private readonly bool enableDebugLogging;
        private readonly List<IOSignalDefinition> signalsToMonitor;
        private readonly Dictionary<string, IOSignalState> signalStates;

        // Events
        public event Action<string, bool> OnSignalChanged;
        public event Action<string, bool> OnGripperSignalReceived;
        public event Action<string> OnError;

        public ABBIOSignalMonitor(IRWSApiClient apiClient, bool enableDebugLogging = false)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.enableDebugLogging = enableDebugLogging;
            this.signalsToMonitor = new List<IOSignalDefinition>();
            this.signalStates = new Dictionary<string, IOSignalState>();
        }

        public IReadOnlyList<IOSignalDefinition> SignalsToMonitor => signalsToMonitor.AsReadOnly();
        public IReadOnlyDictionary<string, IOSignalState> SignalStates => signalStates;

        public void AddSignal(IOSignalDefinition signalDefinition)
        {
            if (signalDefinition == null || string.IsNullOrEmpty(signalDefinition.signalName))
                return;

            signalsToMonitor.Add(signalDefinition);
            signalStates[signalDefinition.signalName] = new IOSignalState(signalDefinition);
            
            LogInfo($"Added I/O signal to monitor: {signalDefinition.signalName}");
        }

        public void AddGripperSignal()
        {            
            AddSignal(new IOSignalDefinition
            {
                signalName = "DO_GripperOpen",
                ioNetwork = "",
                ioDevice = "",
                signalType = IOSignalType.DigitalOutput,
                isGripperSignal = true,
                description = "Gripper open command"
            });

            LogInfo("Sample gripper signals added to monitor list");
        }

        public void ClearSignals()
        {
            signalsToMonitor.Clear();
            signalStates.Clear();
        }

        public async Task PollSignalsAsync()
        {
            if (signalsToMonitor.Count == 0) return;

            try
            {
                using (var client = apiClient.CreateHttpClient())
                {
                    foreach (var signalDef in signalsToMonitor)
                    {
                        if (string.IsNullOrEmpty(signalDef.signalName)) continue;

                        await PollSingleSignalAsync(client, signalDef);
                    }
                }
            }
            catch (Exception e)
            {
                string errorMsg = $"I/O polling failed: {e.Message}";
                LogError(errorMsg);
                OnError?.Invoke(errorMsg);
            }
        }

        private async Task PollSingleSignalAsync(HttpClient client, IOSignalDefinition signalDef)
        {
            try
            {
                string endpoint = $"/rw/iosystem/signals/{signalDef.ioNetwork}/{signalDef.ioDevice}/{signalDef.signalName}";
                var response = await client.GetAsync(apiClient.BuildUrl(endpoint));
                
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    bool currentState = ParseIOSignalState(content, signalDef.signalType);

                    var signalState = signalStates[signalDef.signalName];
                    bool stateChanged = signalState.currentState != currentState || 
                                       signalState.lastUpdate == DateTime.MinValue;

                    if (stateChanged)
                    {
                        signalState.currentState = currentState;
                        signalState.lastUpdate = DateTime.Now;

                        LogInfo($"I/O Signal changed: {signalDef.signalName} = {currentState}");

                        // Fire events
                        OnSignalChanged?.Invoke(signalDef.signalName, currentState);

                        if (signalDef.isGripperSignal)
                        {
                            OnGripperSignalReceived?.Invoke(signalDef.signalName, currentState);
                        }
                    }
                }
                else if (enableDebugLogging)
                {
                    LogWarning($"Failed to read I/O signal {signalDef.signalName}: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                if (enableDebugLogging)
                {
                    LogWarning($"Failed to poll signal {signalDef.signalName}: {e.Message}");
                }
            }
        }

        private bool ParseIOSignalState(string response, IOSignalType signalType)
        {
            try
            {
                if (signalType == IOSignalType.DigitalInput || signalType == IOSignalType.DigitalOutput)
                {
                    // Try JSON parsing first - look for "lvalue" field
                    string value = ExtractValueFromResponse(response, "lvalue");
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value == "1" || value.ToLower() == "true";
                    }

                    // Fallback to XML parsing
                    string searchPattern = "<lvalue>";
                    int startIndex = response.IndexOf(searchPattern);
                    if (startIndex >= 0)
                    {
                        startIndex += searchPattern.Length;
                        int endIndex = response.IndexOf("</lvalue>", startIndex);
                        if (endIndex > startIndex)
                        {
                            value = response.Substring(startIndex, endIndex - startIndex).Trim();
                            return value == "1" || value.ToLower() == "true";
                        }
                    }
                }
                // Handle analog signals if needed in the future
                else if (signalType == IOSignalType.AnalogInput || signalType == IOSignalType.AnalogOutput)
                {
                    string value = ExtractValueFromResponse(response, "lvalue");
                    if (!string.IsNullOrEmpty(value) && float.TryParse(value, out float analogValue))
                    {
                        return analogValue > 0.5f; // Threshold for analog to digital conversion
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

        private string ExtractValueFromResponse(string response, string key)
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

                // Pattern 3: "key":value (without quotes on value - for numbers/booleans)
                string jsonPattern3 = $"\"{key}\":";
                startIndex = response.IndexOf(jsonPattern3);
                if (startIndex >= 0)
                {
                    startIndex += jsonPattern3.Length;
                    // Skip whitespace
                    while (startIndex < response.Length && char.IsWhiteSpace(response[startIndex]))
                        startIndex++;
                    
                    int endIndex = startIndex;
                    // Find end of value (comma, }, or end of string)
                    while (endIndex < response.Length && 
                           response[endIndex] != ',' && 
                           response[endIndex] != '}' && 
                           response[endIndex] != ']' &&
                           !char.IsWhiteSpace(response[endIndex]))
                    {
                        endIndex++;
                    }
                    
                    if (endIndex > startIndex)
                    {
                        return response.Substring(startIndex, endIndex - startIndex);
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
            return null;
        }

        public IOSignalState GetSignalState(string signalName)
        {
            signalStates.TryGetValue(signalName, out var state);
            return state;
        }

        private void LogInfo(string message)
        {
            if (enableDebugLogging)
                Debug.Log($"[ABB I/O Monitor] {message}");
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
                Debug.LogWarning($"[ABB I/O Monitor] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ABB I/O Monitor] {message}");
        }
    }
}
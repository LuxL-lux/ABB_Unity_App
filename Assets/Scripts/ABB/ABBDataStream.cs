/****************************************************************************
ABB Robot Web Services Data Stream Implementation
MIT License
****************************************************************************/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Diagnostics;
using UnityEngine;
using Newtonsoft.Json;

internal class ABBDataStream
{
    private readonly IRWSController controller;
    private CancellationTokenSource cancellationTokenSource;
    private HttpClient httpClient;
    private ClientWebSocket webSocket;
    private readonly object dataLock = new object();
    
    // Subscription management
    private string jointSubscriptionId = "";
    private string cartesianSubscriptionId = "";
    
    // Session management for WebSocket
    private System.Net.CookieContainer cookieContainer;
    private string sessionCookieValue = "";
    private string abbcxCookieValue = "";
    
    // Performance tracking
    private readonly Stopwatch performanceStopwatch = new Stopwatch();
    private long totalRequestCount = 0;
    private long successfulRequestCount = 0;
    
    public ABBDataStream(IRWSController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        
        // Initialize cookie container
        cookieContainer = new System.Net.CookieContainer();
    }
    
    public async void Start()
    {
        cancellationTokenSource = new CancellationTokenSource();
        
        // Create HTTP client with our cookie container
        var handler = new HttpClientHandler 
        { 
            Credentials = new System.Net.NetworkCredential(controller.Username, controller.Password),
            Proxy = null,
            UseProxy = false,
            CookieContainer = cookieContainer
        };
        
        httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        try
        {
            // Start the main streaming task
            await Task.Run(async () => await StreamingLoop(cancellationTokenSource.Token));
        }
        catch (Exception e)
        {
            controller.OnConnectionLost($"Failed to start streaming: {e.Message}");
        }
    }
    
    private async Task StreamingLoop(CancellationToken cancellationToken)
    {
        try
        {
            bool websocketSuccess = false;
            
            if (controller.UseWebSocket)
            {
                websocketSuccess = await TryEstablishWebSocketConnection(cancellationToken);
            }
            
            if (websocketSuccess)
            {
                controller.OnConnectionEstablished(usingWebSocket: true);
                await HandleWebSocketCommunication(cancellationToken);
            }
            else
            {
                controller.OnConnectionEstablished(usingWebSocket: false);
                await HandleHttpPolling(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception e)
        {
            controller.OnConnectionLost($"Streaming error: {e.Message}");
        }
        finally
        {
            await Cleanup();
            controller.OnConnectionStopped();
        }
    }
    
    private async Task<bool> TryEstablishWebSocketConnection(CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Create subscription via HTTP to establish session and get cookies
            jointSubscriptionId = await CreateSubscription("jointtarget", cancellationToken);
            
            if (string.IsNullOrEmpty(jointSubscriptionId))
            {
                UnityEngine.Debug.LogWarning("[ABB RWS] Failed to create joint subscription, falling back to HTTP polling");
                return false;
            }
            
            // Step 2: Extract session cookies from cookie container
            var baseUri = new Uri($"http://{controller.IPAddress}:{controller.Port}/");
            var cookies = cookieContainer.GetCookies(baseUri);
            
            UnityEngine.Debug.Log($"[ABB RWS] Found {cookies.Count} cookies in container");
            
            foreach (System.Net.Cookie cookie in cookies)
            {
                UnityEngine.Debug.Log($"[ABB RWS] Available cookie: {cookie.Name} = {cookie.Value}");
                
                if (cookie.Name.ToLower().Contains("session"))
                {
                    sessionCookieValue = $"{cookie.Name}={cookie.Value}";
                    UnityEngine.Debug.Log($"[ABB RWS] Found session cookie: {sessionCookieValue}");
                }
                else if (cookie.Name == "ABBCX")
                {
                    abbcxCookieValue = $"{cookie.Name}={cookie.Value}";
                    UnityEngine.Debug.Log($"[ABB RWS] Found ABBCX cookie: {abbcxCookieValue}");
                }
            }
            
            // Step 3: Establish WebSocket connection with proper protocol and cookies
            webSocket = new ClientWebSocket();
            
            // Set the required WebSocket subprotocol
            webSocket.Options.AddSubProtocol("robapi2_subscription");
            
            // Add session cookies to WebSocket headers
            if (!string.IsNullOrEmpty(sessionCookieValue) && !string.IsNullOrEmpty(abbcxCookieValue))
            {
                string cookieHeader = $"{sessionCookieValue}; {abbcxCookieValue}";
                webSocket.Options.SetRequestHeader("Cookie", cookieHeader);
                UnityEngine.Debug.Log($"[ABB RWS] Added WebSocket cookies: {cookieHeader}");
            }
            else if (!string.IsNullOrEmpty(sessionCookieValue))
            {
                webSocket.Options.SetRequestHeader("Cookie", sessionCookieValue);
                UnityEngine.Debug.Log($"[ABB RWS] Added session cookie only: {sessionCookieValue}");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[ABB RWS] No session cookies found for WebSocket connection");
            }
            
            // Try different WebSocket endpoints commonly used by ABB RWS
            string[] endpoints = { "/poll", "/subscription", "/ws" };
            
            foreach (string endpoint in endpoints)
            {
                try
                {
                    string wsUrl = $"ws://{controller.IPAddress}:{controller.Port}{endpoint}";
                    UnityEngine.Debug.Log($"[ABB RWS] Attempting WebSocket connection to: {wsUrl}");
                    
                    await webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
                    
                    if (webSocket.State == WebSocketState.Open)
                    {
                        UnityEngine.Debug.Log($"[ABB RWS] WebSocket connected successfully to {endpoint} with protocol: {webSocket.SubProtocol}");
                        return true;
                    }
                }
                catch (Exception endpointEx)
                {
                    UnityEngine.Debug.Log($"[ABB RWS] Endpoint {endpoint} failed: {endpointEx.Message}");
                    
                    // Reset WebSocket for next attempt
                    webSocket?.Dispose();
                    webSocket = new ClientWebSocket();
                    webSocket.Options.AddSubProtocol("robapi2_subscription");
                    if (!string.IsNullOrEmpty(sessionCookieValue))
                    {
                        string cookieHeader = !string.IsNullOrEmpty(abbcxCookieValue) ? 
                            $"{sessionCookieValue}; {abbcxCookieValue}" : sessionCookieValue;
                        webSocket.Options.SetRequestHeader("Cookie", cookieHeader);
                    }
                }
            }
            
            return false;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ABB RWS] WebSocket setup failed, falling back to HTTP polling: {e.Message}");
            if (e.InnerException != null)
            {
                UnityEngine.Debug.LogWarning($"[ABB RWS] Inner exception: {e.InnerException.Message}");
            }
            return false;
        }
    }
    
    private async Task<string> CreateSubscription(string resource, CancellationToken cancellationToken)
    {
        // Define multiple resource URI patterns to try
        string[] resourceUriPatterns = null;
        
        if (resource == "jointtarget")
        {
            resourceUriPatterns = new string[]
            {
                $"/rw/motionsystem/mechunits/ROB_1/{resource}",  // Motion system direct
                "/rw/motionsystem?resource=change-count",          // Motion system change count
                $"/rw/rapid/tasks/{controller.TaskName}/motion?resource={resource}", // RAPID task resource
                "/rw/motionsystem/mechunits/ROB_1",               // Motion system unit only
                $"/rw/rapid/tasks/{controller.TaskName}/motion",   // RAPID task motion only
            };
        }
        else if (resource == "robtarget")
        {
            resourceUriPatterns = new string[]
            {
                $"/rw/motionsystem/mechunits/ROB_1/{resource}",
                $"/rw/rapid/tasks/{controller.TaskName}/motion?resource={resource}",
                "/rw/motionsystem/mechunits/ROB_1",
                $"/rw/rapid/tasks/{controller.TaskName}/motion",
            };
        }
        else
        {
            resourceUriPatterns = new string[]
            {
                $"/rw/rapid/tasks/{controller.TaskName}/motion?resource={resource}",
                $"/rw/rapid/tasks/{controller.TaskName}/motion"
            };
        }
        
        // Try each resource URI pattern until one succeeds
        foreach (string resourceUri in resourceUriPatterns)
        {
            string subscriptionId = await TryCreateSubscriptionWithUri(resource, resourceUri, cancellationToken);
            if (!string.IsNullOrEmpty(subscriptionId))
            {
                return subscriptionId;
            }
        }
        
        UnityEngine.Debug.LogError($"[ABB RWS] All resource URI patterns failed for {resource}");
        return "";
    }
    
    private async Task<string> TryCreateSubscriptionWithUri(string resource, string resourceUri, CancellationToken cancellationToken)
    {
        try
        {
            totalRequestCount++;
            string subscriptionUrl = $"http://{controller.IPAddress}:{controller.Port}/subscription";
            
            // ABB RWS requires specific numbered parameter format:
            // resources=1&1=/path/to/resource&1-p=priority_level
            var formParams = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("resources", "1"),
                new KeyValuePair<string, string>("1", resourceUri),
                new KeyValuePair<string, string>("1-p", "1") // Medium priority
            };
            
            var content = new FormUrlEncodedContent(formParams);
            
            UnityEngine.Debug.Log($"[ABB RWS] Attempting subscription for {resource} with URI: {resourceUri}");
            
            using (var response = await httpClient.PostAsync(subscriptionUrl, content, cancellationToken))
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                UnityEngine.Debug.Log($"[ABB RWS] Response ({response.StatusCode}): {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...");
                
                if (!response.IsSuccessStatusCode)
                {
                    UnityEngine.Debug.LogWarning($"[ABB RWS] URI {resourceUri} failed: {response.StatusCode} - {response.ReasonPhrase}");
                    return "";
                }
                
                // Parse response based on content type
                string subscriptionId = "";
                
                if (responseContent.TrimStart().StartsWith("<"))
                {
                    // XML response - extract subscription ID from XML
                    subscriptionId = ExtractSubscriptionIdFromXml(responseContent);
                }
                else
                {
                    // JSON response
                    try
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseContent);
                        subscriptionId = result?.subscription?.ToString() ?? "";
                    }
                    catch (Exception jsonEx)
                    {
                        UnityEngine.Debug.LogWarning($"[ABB RWS] Failed to parse JSON response: {jsonEx.Message}");
                        // Try to extract from plain text response
                        subscriptionId = ExtractSubscriptionIdFromText(responseContent);
                    }
                }
                
                if (!string.IsNullOrEmpty(subscriptionId))
                {
                    UnityEngine.Debug.Log($"[ABB RWS] Successfully created subscription with URI {resourceUri}, ID: {subscriptionId}");
                    successfulRequestCount++;
                    return subscriptionId;
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[ABB RWS] No subscription ID found in response for URI: {resourceUri}");
                    return "";
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[ABB RWS] Exception trying URI {resourceUri}: {e.Message}");
            return "";
        }
    }
    
    private string ExtractSubscriptionIdFromXml(string xmlResponse)
    {
        try
        {
            // Simple XML parsing to extract subscription ID
            // Look for patterns like <subscription>ID</subscription> or subscription="ID"
            var lines = xmlResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("subscription"))
                {
                    // Extract subscription ID from various XML patterns
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("<subscription>") && trimmed.EndsWith("</subscription>"))
                    {
                        return trimmed.Substring(14, trimmed.Length - 28); // Remove tags
                    }
                    
                    // Look for subscription="value" pattern
                    var subscriptionIndex = trimmed.IndexOf("subscription=\"");
                    if (subscriptionIndex >= 0)
                    {
                        var start = subscriptionIndex + 14; // Length of 'subscription="'
                        var end = trimmed.IndexOf('\"', start);
                        if (end > start)
                        {
                            return trimmed.Substring(start, end - start);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ABB RWS] Error extracting subscription ID from XML: {e.Message}");
        }
        return "";
    }
    
    private string ExtractSubscriptionIdFromText(string textResponse)
    {
        try
        {
            // Look for subscription ID in plain text response
            var lines = textResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Common patterns for subscription ID in text responses
                if (trimmed.Length > 0 && !trimmed.StartsWith("<") && !trimmed.Contains("Error"))
                {
                    // Assume first non-XML, non-error line might be subscription ID
                    return trimmed;
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ABB RWS] Error extracting subscription ID from text: {e.Message}");
        }
        return "";
    }
    
    private async Task HandleWebSocketCommunication(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();
        
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string fragment = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuffer.Append(fragment);
                    
                    if (result.EndOfMessage)
                    {
                        ProcessWebSocketMessage(messageBuffer.ToString());
                        messageBuffer.Clear();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ABB RWS] WebSocket message handling error: {e.Message}");
                break;
            }
        }
    }
    
    private void ProcessWebSocketMessage(string message)
    {
        try
        {
            dynamic data = JsonConvert.DeserializeObject(message);
            
            if (data?.subscription?.ToString() == jointSubscriptionId)
            {
                var state = data._embedded?._state?[0];
                if (state != null)
                {
                    var jointData = new double[6]
                    {
                        (double)(state.j1 ?? 0),
                        (double)(state.j2 ?? 0),
                        (double)(state.j3 ?? 0),
                        (double)(state.j4 ?? 0),
                        (double)(state.j5 ?? 0),
                        (double)(state.j6 ?? 0)
                    };
                    
                    controller.OnDataReceived(jointData);
                    successfulRequestCount++;
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ABB RWS] WebSocket message processing error: {e.Message}");
        }
    }
    
    private async Task HandleHttpPolling(CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();
            
            try
            {
                await FetchJointData(cancellationToken);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ABB RWS] HTTP polling error: {e.Message}");
                
                // Add small delay on error to prevent spam
                await Task.Delay(Math.Max(controller.PollingIntervalMs, 1000), cancellationToken);
            }
            
            stopwatch.Stop();
            
            // Calculate sleep time to maintain polling interval
            int sleepTime = Math.Max(0, controller.PollingIntervalMs - (int)stopwatch.ElapsedMilliseconds);
            if (sleepTime > 0)
            {
                await Task.Delay(sleepTime, cancellationToken);
            }
        }
    }
    
    private async Task FetchJointData(CancellationToken cancellationToken)
    {
        try
        {
            totalRequestCount++;
            string url = $"http://{controller.IPAddress}:{controller.Port}/rw/rapid/tasks/{controller.TaskName}/motion?resource=jointtarget&json=1";
            
            using (var response = await httpClient.GetAsync(url, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                string result = await response.Content.ReadAsStringAsync();
                
                dynamic obj = JsonConvert.DeserializeObject(result);
                var state = obj?._embedded?._state?[0];
                
                if (state != null)
                {
                    var jointData = new double[6]
                    {
                        (double)(state.j1 ?? 0),
                        (double)(state.j2 ?? 0),
                        (double)(state.j3 ?? 0),
                        (double)(state.j4 ?? 0),
                        (double)(state.j5 ?? 0),
                        (double)(state.j6 ?? 0)
                    };
                    
                    controller.OnDataReceived(jointData);
                    successfulRequestCount++;
                }
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ABB RWS] Joint data fetch error: {e.Message}");
        }
    }
    
    public void Stop()
    {
        cancellationTokenSource?.Cancel();
    }
    
    private async Task Cleanup()
    {
        try
        {
            // Delete subscriptions
            if (!string.IsNullOrEmpty(jointSubscriptionId))
            {
                try
                {
                    await httpClient.DeleteAsync($"http://{controller.IPAddress}:{controller.Port}/subscription/{jointSubscriptionId}");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[ABB RWS] Failed to delete joint subscription: {e.Message}");
                }
            }
            
            if (!string.IsNullOrEmpty(cartesianSubscriptionId))
            {
                try
                {
                    await httpClient.DeleteAsync($"http://{controller.IPAddress}:{controller.Port}/subscription/{cartesianSubscriptionId}");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[ABB RWS] Failed to delete cartesian subscription: {e.Message}");
                }
            }
            
            // Close WebSocket
            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[ABB RWS] Cleanup error: {e.Message}");
        }
        finally
        {
            webSocket?.Dispose();
            httpClient?.Dispose();
            cancellationTokenSource?.Dispose();
            
            if (controller.ShowPerformanceMetrics && totalRequestCount > 0)
            {
                float successRate = (float)successfulRequestCount / totalRequestCount * 100f;
                UnityEngine.Debug.Log($"[ABB RWS] Performance Summary: {successfulRequestCount}/{totalRequestCount} requests successful ({successRate:F1}%)");
            }
        }
    }
}
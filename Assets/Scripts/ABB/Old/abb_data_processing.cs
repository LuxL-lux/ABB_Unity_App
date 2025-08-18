/****************************************************************************
MIT License - ABB Robot Web Services with WebSocket Subscription
****************************************************************************/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class abb_data_processing  : MonoBehaviour
{
    public static class GlobalVariables_Main_Control
    {
        public static bool connect, disconnect;
    }

    public static class ABB_Stream_Data
    {
        // IP Address
        public static string ip_address = "127.0.0.1";
        // Communication Speed (ms) - for fallback polling
        public static int polling_time_step = 100;
        
        // Joint Space: Orientation {J1 .. J6} (°)
        public static double[] J_Orientation = new double[6];
        // Cartesian Space: Position {X, Y, Z} (mm)
        public static double[] C_Position = new double[3];
        // Cartesian Space: Orientation {Quaternion}
        public static double[] C_Orientation = new double[4];
        
        // Thread control
        public static bool is_alive = false;
        
        // Performance metrics
        public static long last_update_time = 0;
        public static double update_frequency = 0;
        public static bool using_websocket = false;
    }

    private ABB_RWS_Stream stream_manager;
    private int main_abb_state = 0;

    void Start()
    {
        stream_manager = new ABB_RWS_Stream();
    }

    private void FixedUpdate()
    {
        switch (main_abb_state)
        {
            case 0:
                {
                    if (GlobalVariables_Main_Control.connect == true)
                    {
                        stream_manager.Start();
                        main_abb_state = 1;
                    }
                }
                break;
            case 1:
                {
                    if (GlobalVariables_Main_Control.disconnect == true)
                    {
                        if (ABB_Stream_Data.is_alive == true)
                        {
                            stream_manager.Stop();
                        }

                        if (ABB_Stream_Data.is_alive == false)
                        {
                            main_abb_state = 0;
                        }
                    }
                }
                break;
        }
    }

    void OnApplicationQuit()
    {
        try
        {
            stream_manager?.Destroy();
            Destroy(this);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    class ABB_RWS_Stream
    {
        private CancellationTokenSource cancellationTokenSource;
        private HttpClient httpClient;
        private ClientWebSocket webSocket;
        private readonly object dataLock = new object();
        
        // Subscription IDs for WebSocket
        private string jointSubscriptionId = "";
        private string cartesianSubscriptionId = "";
        
        // Cookies for session management
        private CookieContainer cookieContainer;
        
        public ABB_RWS_Stream()
        {
            cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler 
            { 
                Credentials = new NetworkCredential("Default User", "robotics"),
                Proxy = null,
                UseProxy = false,
                CookieContainer = cookieContainer
            };
            
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Start()
        {
            cancellationTokenSource = new CancellationTokenSource();
            ABB_Stream_Data.is_alive = true;
            
            // Start the streaming task
            _ = Task.Run(async () => await StreamingLoop(cancellationTokenSource.Token));
        }

        private async Task StreamingLoop(CancellationToken cancellationToken)
        {
            try
            {
                // First, try to establish WebSocket subscription
                bool websocketSuccess = await TryWebSocketSubscription(cancellationToken);
                
                if (websocketSuccess)
                {
                    ABB_Stream_Data.using_websocket = true;
                    Debug.Log("Using WebSocket subscription for real-time data");
                    await HandleWebSocketMessages(cancellationToken);
                }
                else
                {
                    // Fall back to HTTP polling
                    ABB_Stream_Data.using_websocket = false;
                    Debug.Log("Falling back to HTTP polling");
                    await HttpPollingLoop(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception e)
            {
                Debug.LogError($"Streaming error: {e.Message}");
            }
            finally
            {
                await CleanupSubscriptions();
                ABB_Stream_Data.is_alive = false;
            }
        }

        private async Task<bool> TryWebSocketSubscription(CancellationToken cancellationToken)
        {
            try
            {
                // Step 1: Create subscriptions via HTTP POST
                jointSubscriptionId = await CreateSubscription("jointtarget", cancellationToken);
                cartesianSubscriptionId = await CreateSubscription("robtarget", cancellationToken);
                
                if (string.IsNullOrEmpty(jointSubscriptionId) || string.IsNullOrEmpty(cartesianSubscriptionId))
                {
                    Debug.LogWarning("Failed to create subscriptions");
                    return false;
                }
                
                // Step 2: Establish WebSocket connection
                webSocket = new ClientWebSocket();
                
                // Add authentication headers
                webSocket.Options.Credentials = new NetworkCredential("Default User", "robotics");
                
                // Connect to WebSocket endpoint
                string wsUrl = $"ws://{ABB_Stream_Data.ip_address}/subscription";
                await webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
                
                return webSocket.State == WebSocketState.Open;
            }
            catch (Exception e)
            {
                Debug.LogError($"WebSocket setup failed: {e.Message}");
                return false;
            }
        }

        private async Task<string> CreateSubscription(string resource, CancellationToken cancellationToken)
        {
            try
            {
                // Create subscription for the resource
                string subscriptionUrl = $"http://{ABB_Stream_Data.ip_address}/subscription";
                
                // Subscription request body
                var subscriptionData = new
                {
                    resources = new[] { $"/rw/rapid/tasks/T_ROB1/motion?resource={resource}" },
                    priority = 0
                };
                
                string jsonContent = Newtonsoft.Json.JsonConvert.SerializeObject(subscriptionData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                using (var response = await httpClient.PostAsync(subscriptionUrl, content, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string responseContent = await response.Content.ReadAsStringAsync();
                    
                    // Parse the subscription ID from response
                    dynamic result = Newtonsoft.Json.JsonConvert.DeserializeObject(responseContent);
                    return result?.subscription?.ToString() ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create subscription for {resource}: {e.Message}");
                return "";
            }
        }

        private async Task HandleWebSocketMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
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
                            string completeMessage = messageBuffer.ToString();
                            messageBuffer.Clear();
                            
                            ProcessWebSocketMessage(completeMessage);
                            UpdatePerformanceMetrics();
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"WebSocket message handling error: {e.Message}");
                    break;
                }
            }
        }

        private void ProcessWebSocketMessage(string message)
        {
            try
            {
                dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(message);
                
                if (data?.subscription != null)
                {
                    string subscriptionId = data.subscription.ToString();
                    
                    lock (dataLock)
                    {
                        if (subscriptionId == jointSubscriptionId)
                        {
                            // Process joint data
                            var state = data._embedded?._state?[0];
                            if (state != null)
                            {
                                ABB_Stream_Data.J_Orientation[0] = (double)(state.j1 ?? 0);
                                ABB_Stream_Data.J_Orientation[1] = (double)(state.j2 ?? 0);
                                ABB_Stream_Data.J_Orientation[2] = (double)(state.j3 ?? 0);
                                ABB_Stream_Data.J_Orientation[3] = (double)(state.j4 ?? 0);
                                ABB_Stream_Data.J_Orientation[4] = (double)(state.j5 ?? 0);
                                ABB_Stream_Data.J_Orientation[5] = (double)(state.j6 ?? 0);
                            }
                        }
                        else if (subscriptionId == cartesianSubscriptionId)
                        {
                            // Process cartesian data
                            var state = data._embedded?._state?[0];
                            if (state != null)
                            {
                                ABB_Stream_Data.C_Position[0] = (double)(state.x ?? 0);
                                ABB_Stream_Data.C_Position[1] = (double)(state.y ?? 0);
                                ABB_Stream_Data.C_Position[2] = (double)(state.z ?? 0);
                                
                                ABB_Stream_Data.C_Orientation[0] = (double)(state.q1 ?? 0);
                                ABB_Stream_Data.C_Orientation[1] = (double)(state.q2 ?? 0);
                                ABB_Stream_Data.C_Orientation[2] = (double)(state.q3 ?? 0);
                                ABB_Stream_Data.C_Orientation[3] = (double)(state.q4 ?? 0);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"WebSocket message processing error: {e.Message}");
            }
        }

        private async Task HttpPollingLoop(CancellationToken cancellationToken)
        {
            var stopwatch = new Stopwatch();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Start();
                
                try
                {
                    // Separate requests for joint and cartesian data
                    await Task.WhenAll(
                        FetchJointData(cancellationToken),
                        FetchCartesianData(cancellationToken)
                    );
                    
                    UpdatePerformanceMetrics();
                }
                catch (Exception e)
                {
                    Debug.LogError($"HTTP polling error: {e.Message}");
                }
                
                stopwatch.Stop();
                
                int sleepTime = Math.Max(0, ABB_Stream_Data.polling_time_step - (int)stopwatch.ElapsedMilliseconds);
                if (sleepTime > 0)
                {
                    await Task.Delay(sleepTime, cancellationToken);
                }
                
                stopwatch.Restart();
            }
        }

        private async Task FetchJointData(CancellationToken cancellationToken)
        {
            try
            {
                string url = $"http://{ABB_Stream_Data.ip_address}/rw/rapid/tasks/T_ROB1/motion?resource=jointtarget&json=1";
                using (var response = await httpClient.GetAsync(url, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string result = await response.Content.ReadAsStringAsync();
                    
                    dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(result);
                    var service = obj?._embedded?._state?[0];
                    
                    if (service != null)
                    {
                        lock (dataLock)
                        {
                            ABB_Stream_Data.J_Orientation[0] = (double)(service.j1 ?? 0);
                            ABB_Stream_Data.J_Orientation[1] = (double)(service.j2 ?? 0);
                            ABB_Stream_Data.J_Orientation[2] = (double)(service.j3 ?? 0);
                            ABB_Stream_Data.J_Orientation[3] = (double)(service.j4 ?? 0);
                            ABB_Stream_Data.J_Orientation[4] = (double)(service.j5 ?? 0);
                            ABB_Stream_Data.J_Orientation[5] = (double)(service.j6 ?? 0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Joint data fetch error: {e.Message}");
            }
        }

        private async Task FetchCartesianData(CancellationToken cancellationToken)
        {
            try
            {
                string url = $"http://{ABB_Stream_Data.ip_address}/rw/rapid/tasks/T_ROB1/motion?resource=robtarget&json=1";
                using (var response = await httpClient.GetAsync(url, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string result = await response.Content.ReadAsStringAsync();
                    
                    dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(result);
                    var service = obj?._embedded?._state?[0];
                    
                    if (service != null)
                    {
                        lock (dataLock)
                        {
                            ABB_Stream_Data.C_Position[0] = (double)(service.x ?? 0);
                            ABB_Stream_Data.C_Position[1] = (double)(service.y ?? 0);
                            ABB_Stream_Data.C_Position[2] = (double)(service.z ?? 0);
                            
                            ABB_Stream_Data.C_Orientation[0] = (double)(service.q1 ?? 0);
                            ABB_Stream_Data.C_Orientation[1] = (double)(service.q2 ?? 0);
                            ABB_Stream_Data.C_Orientation[2] = (double)(service.q3 ?? 0);
                            ABB_Stream_Data.C_Orientation[3] = (double)(service.q4 ?? 0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Cartesian data fetch error: {e.Message}");
            }
        }

        private void UpdatePerformanceMetrics()
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ABB_Stream_Data.last_update_time > 0)
            {
                long timeDiff = currentTime - ABB_Stream_Data.last_update_time;
                ABB_Stream_Data.update_frequency = timeDiff > 0 ? 1000.0 / timeDiff : 0;
            }
            ABB_Stream_Data.last_update_time = currentTime;
        }

        private async Task CleanupSubscriptions()
        {
            try
            {
                // Delete subscriptions
                if (!string.IsNullOrEmpty(jointSubscriptionId))
                {
                    await httpClient.DeleteAsync($"http://{ABB_Stream_Data.ip_address}/subscription/{jointSubscriptionId}");
                }
                
                if (!string.IsNullOrEmpty(cartesianSubscriptionId))
                {
                    await httpClient.DeleteAsync($"http://{ABB_Stream_Data.ip_address}/subscription/{cartesianSubscriptionId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Cleanup error: {e.Message}");
            }
        }

        public void Stop()
        {
            cancellationTokenSource?.Cancel();
            ABB_Stream_Data.is_alive = false;
        }

        public void Destroy()
        {
            Stop();
            
            // Close WebSocket connection
            if (webSocket?.State == WebSocketState.Open)
            {
                webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
            }
            
            webSocket?.Dispose();
            httpClient?.Dispose();
            cancellationTokenSource?.Dispose();
        }
    }
}
# Communication Protocols & Data Formats

## Protocol Overview

The RobotSystem framework implements a hybrid communication approach combining real-time WebSocket subscriptions with HTTP polling for comprehensive robot data acquisition.

### Communication Architecture

```
┌─────────────────┐    WebSocket (WSS)     ┌──────────────────┐
│                 │◄──────────────────────►│                  │
│   Unity Client  │                        │  ABB Controller  │
│                 │◄──────────────────────►│     (RWS)        │
└─────────────────┘    HTTP/HTTPS          └──────────────────┘
       ▲                                            
       │                                            
       ▼                                            
┌─────────────────┐                                 
│  Safety Monitors │                                 
│  & Visualization │                                 
└─────────────────┘                                 
```

## ABB Robot Web Services (RWS) Integration

### Authentication Protocol

#### Digest Authentication Flow

1. **Initial Request** (without credentials):
   ```http
   GET /rw/system/version HTTP/1.1
   Host: 192.168.1.100
   ```

2. **Authentication Challenge**:
   ```http
   HTTP/1.1 401 Unauthorized
   WWW-Authenticate: Digest realm="RobotController", 
                           qop="auth",
                           nonce="1234567890abcdef",
                           opaque="0000ffff"
   ```

3. **Authenticated Request**:
   ```http
   GET /rw/system/version HTTP/1.1
   Host: 192.168.1.100
   Authorization: Digest username="unity_client",
                         realm="RobotController",
                         nonce="1234567890abcdef",
                         uri="/rw/system/version",
                         qop=auth,
                         nc=00000001,
                         cnonce="abcdef1234567890",
                         response="6629fae49393a05397450978507c4ef1",
                         opaque="0000ffff"
   ```

#### Authentication Service Implementation

```csharp
public class ABBAuthenticationService
{
    private readonly string robotIP;
    private readonly string username;
    private readonly string password;
    private readonly HttpClient httpClient;
    
    private string realm;
    private string nonce;
    private string opaque;
    private string qop;
    private int nonceCount = 1;
    
    public async Task<bool> AuthenticateAsync()
    {
        // Step 1: Get authentication challenge
        var challengeResponse = await httpClient.GetAsync($"http://{robotIP}/rw/system/version");
        
        if (challengeResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            ParseAuthenticationChallenge(challengeResponse.Headers.WwwAuthenticate.First());
            
            // Step 2: Calculate digest response
            string digestResponse = CalculateDigestResponse("/rw/system/version", "GET");
            
            // Step 3: Send authenticated request
            var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, $"http://{robotIP}/rw/system/version");
            authenticatedRequest.Headers.Authorization = new AuthenticationHeaderValue("Digest", digestResponse);
            
            var authResult = await httpClient.SendAsync(authenticatedRequest);
            return authResult.IsSuccessStatusCode;
        }
        
        return false;
    }
    
    private string CalculateDigestResponse(string uri, string method)
    {
        // MD5 hash calculations for digest authentication
        string ha1 = CalculateMD5($"{username}:{realm}:{password}");
        string ha2 = CalculateMD5($"{method}:{uri}");
        string cnonce = GenerateClientNonce();
        
        string response = CalculateMD5($"{ha1}:{nonce}:{nonceCount:D8}:{cnonce}:{qop}:{ha2}");
        
        return $"username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", " +
               $"uri=\"{uri}\", qop={qop}, nc={nonceCount:D8}, cnonce=\"{cnonce}\", " +
               $"response=\"{response}\", opaque=\"{opaque}\"";
    }
}
```

### HTTP Communication

#### Motion Data Polling

**Endpoint**: `GET /rw/rapid/tasks/{taskName}/motion?resource=jointtarget&json=1`

**Request Headers**:
```http
GET /rw/rapid/tasks/T_ROB1/motion?resource=jointtarget&json=1 HTTP/1.1
Host: 192.168.1.100
Authorization: Digest [authentication parameters]
Accept: application/json
Cache-Control: no-cache
```

**Response Format**:
```json
{
  "state": [
    {
      "class": "motion",
      "instance": "T_ROB1",
      "resource": "jointtarget", 
      "title": "T_ROB1 jointtarget",
      "value": "[[10.5,-25.3,30.7,0.0,85.2,-15.1],[9E9,9E9,9E9,9E9,9E9,9E9]]"
    }
  ]
}
```

**Parsing Implementation**:
```csharp
public class ABBMotionDataService
{
    public async Task<float[]> GetJointAnglesAsync()
    {
        string endpoint = $"http://{robotIP}/rw/rapid/tasks/{taskName}/motion?resource=jointtarget&json=1";
        
        var response = await httpClient.GetAsync(endpoint);
        if (response.IsSuccessStatusCode)
        {
            var jsonContent = await response.Content.ReadAsStringAsync();
            var motionData = JsonConvert.DeserializeObject<MotionDataResponse>(jsonContent);
            
            string jointTargetValue = motionData.state[0].value;
            return ParseJointTarget(jointTargetValue);
        }
        
        return null;
    }
    
    private float[] ParseJointTarget(string jointTarget)
    {
        // Parse: "[[j1,j2,j3,j4,j5,j6],[9E9,9E9,9E9,9E9,9E9,9E9]]"
        var match = Regex.Match(jointTarget, @"\[\[([^\]]+)\]");
        if (match.Success)
        {
            string[] jointStrings = match.Groups[1].Value.Split(',');
            float[] jointAngles = new float[6];
            
            for (int i = 0; i < 6 && i < jointStrings.Length; i++)
            {
                if (float.TryParse(jointStrings[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
                {
                    jointAngles[i] = angle;
                }
            }
            
            return jointAngles;
        }
        
        return null;
    }
}
```

#### System Information Queries

**Controller Version**:
```http
GET /rw/system/version HTTP/1.1
```

**Response**:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>system version</title></head>
<body>
<div class="state">
  <a href="/rw/system/version/rwsystem" rel="self">
    <span class="name">rwsystem</span>
    <span class="value">6.11.01</span>
  </a>
  <a href="/rw/system/version/robotware" rel="self">
    <span class="name">robotware</span>
    <span class="value">6.11.1034</span>
  </a>
</div>
</body>
</html>
```

### WebSocket Communication

#### Connection Establishment

```csharp
public class ABBWebSocketService
{
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellationTokenSource;
    
    public async Task ConnectAsync(string subscriptionUrl)
    {
        webSocket = new ClientWebSocket();
        
        // Configure authentication headers
        webSocket.Options.SetRequestHeader("Authorization", authorizationHeader);
        
        // Connect to WebSocket endpoint
        await webSocket.ConnectAsync(new Uri(subscriptionUrl), cancellationTokenSource.Token);
        
        // Start listening for messages
        _ = Task.Run(ReceiveLoop);
    }
    
    private async Task ReceiveLoop()
    {
        var buffer = new byte[4096];
        
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), 
                                                    cancellationTokenSource.Token);
            
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessWebSocketMessage(message);
            }
        }
    }
}
```

#### Subscription Management

**Subscription Creation**:
```http
POST /subscription HTTP/1.1
Host: 192.168.1.100
Authorization: Digest [authentication parameters]
Content-Type: application/x-www-form-urlencoded

resource=1,2,3&priority=Medium&ackRequest=True
```

**Resources Configuration**:
```csharp
private readonly Dictionary<int, string> subscriptionResources = new Dictionary<int, string>
{
    { 1, "/rw/rapid/execution;ctrlexecstate" },              // Execution state
    { 2, "/rw/rapid/tasks/T_ROB1/pcp;programpointerchange" }, // Program pointer
    { 3, "/rw/iosystem/signals/Local/Unit/DO_GripperOpen;state" }, // I/O signals
    { 4, "/rw/panel/ctrl-state" },                           // Controller state
    { 5, "/rw/rapid/execution;rapidexeccycle" }              // Execution cycle
};
```

**WebSocket Message Types**:

1. **Execution State Change**:
   ```xml
   <div class="state">
     <span class="class">rapid-execution</span>
     <span class="instance">ctrlexecstate</span>
     <span class="name">ctrlexecstate</span>
     <span class="value">running</span>
   </div>
   ```

2. **Program Pointer Update**:
   ```xml
   <div class="state">
     <span class="class">rapid-task-pcp</span>
     <span class="instance">T_ROB1</span>
     <span class="name">programpointerchange</span>
     <span class="value">MainModule/main/15</span>
   </div>
   ```

3. **I/O Signal Change**:
   ```xml
   <div class="state">
     <span class="class">iosignal</span>
     <span class="instance">Local/Unit/DO_GripperOpen</span>
     <span class="name">state</span>
     <span class="value">1</span>
   </div>
   ```

#### Message Parsing

```csharp
public class ABBRWSDataParser : IRobotDataParser
{
    public bool CanParse(string rawData)
    {
        return rawData.Contains("<div class=\"state\">") && 
               (rawData.Contains("rapid-execution") || 
                rawData.Contains("rapid-task-pcp") ||
                rawData.Contains("iosignal"));
    }
    
    public void ParseData(string rawData, RobotState robotState)
    {
        var doc = new XmlDocument();
        doc.LoadXml($"<root>{rawData}</root>");
        
        var stateNode = doc.SelectSingleNode("//div[@class='state']");
        if (stateNode != null)
        {
            string className = GetSpanValue(stateNode, "class");
            string value = GetSpanValue(stateNode, "value");
            
            switch (className)
            {
                case "rapid-execution":
                    ParseExecutionState(value, robotState);
                    break;
                    
                case "rapid-task-pcp":
                    ParseProgramPointer(value, robotState);
                    break;
                    
                case "iosignal":
                    string instance = GetSpanValue(stateNode, "instance");
                    ParseIOSignal(instance, value, robotState);
                    break;
            }
        }
    }
    
    private void ParseExecutionState(string value, RobotState robotState)
    {
        robotState.UpdateMotorState(value.ToLower());
    }
    
    private void ParseProgramPointer(string value, RobotState robotState)
    {
        // Format: "MainModule/main/15"
        string[] parts = value.Split('/');
        if (parts.Length >= 3)
        {
            string module = parts[0];
            string routine = parts[1];
            int.TryParse(parts[2], out int line);
            
            robotState.UpdateProgramPointer(module, routine, line, 0);
        }
    }
    
    private void ParseIOSignal(string signalPath, string value, RobotState robotState)
    {
        // Extract signal name from path: "Local/Unit/DO_GripperOpen"
        string[] pathParts = signalPath.Split('/');
        string signalName = pathParts[pathParts.Length - 1];
        
        // Convert value based on signal type
        object signalValue = ConvertSignalValue(signalName, value);
        robotState.UpdateIOSignal(signalName, signalValue, "good", "0");
    }
}
```

## Data Structures

### RobotState Schema

```json
{
  "robotType": "ABB",
  "robotIP": "192.168.1.100",
  "lastUpdate": "2024-01-15T10:30:45.123Z",
  
  "executionState": {
    "isRunning": true,
    "motorState": "running",
    "executionCycle": "running",
    "controllerState": "AUTO"
  },
  
  "programPointer": {
    "currentModule": "MainModule",
    "currentRoutine": "PickAndPlace", 
    "currentLine": 42,
    "currentColumn": 15
  },
  
  "motionData": {
    "jointAngles": [15.2, -45.8, 30.1, 0.0, 90.0, -10.5],
    "jointVelocities": [2.1, -1.8, 0.5, 0.0, 3.2, -0.8],
    "hasValidJointData": true,
    "lastJointUpdate": "2024-01-15T10:30:45.120Z",
    "motionUpdateFrequencyHz": 10.5
  },
  
  "ioSignals": {
    "do_gripperopen": 1,
    "do_gripperopen_state": "good",
    "do_gripperopen_quality": "0"
  },
  
  "customData": {}
}
```

### SafetyEvent Schema

```json
{
  "monitorName": "Collision Detector",
  "eventType": "Warning",
  "timestamp": "2024-01-15T10:30:45.123Z",
  "description": "Collision detected between Link3 and ConveyorBelt",
  
  "robotStateSnapshot": {
    "captureTime": "2024-01-15T10:30:45.120Z",
    "isProgramRunning": true,
    "currentModule": "MainModule",
    "currentRoutine": "PickAndPlace",
    "currentLine": 42,
    "executionCycle": "running",
    "motorState": "active", 
    "controllerState": "AUTO",
    "jointAngles": [15.2, -45.8, 30.1, 0.0, 90.0, -10.5],
    "hasValidJointData": true,
    "motionUpdateFrequencyHz": 10.5,
    "gripperOpen": false,
    "robotType": "ABB",
    "robotIP": "192.168.1.100"
  },
  
  "eventDataJson": "{\"robotLink\":\"Link3\",\"collisionObject\":\"ConveyorBelt\",\"collisionPoint\":{\"x\":1.2,\"y\":0.8,\"z\":0.3},\"distance\":0.05}"
}
```

### Singularity Event Data

```json
{
  "singularityType": "Wrist",
  "jointAngles": [15.2, -45.8, 30.1, 0.0, 2.1, -10.5],
  "wristThreshold": 10.0,
  "shoulderThreshold": 0.1,
  "elbowThreshold": 0.01,
  "detectionTime": "2024-01-15T10:30:45.123Z",
  "dhAnalysis": "DH-parameter based detection using Preliy.Flange framework (Unity Y-up coordinate system)",
  "isEntering": true
}
```

### Collision Event Data

```json
{
  "robotLink": "Link3",
  "collisionObject": "ConveyorBelt",
  "collisionPoint": {
    "x": 1.2,
    "y": 0.8, 
    "z": 0.3
  },
  "distance": 0.05,
  "collisionNormal": {
    "x": 0.0,
    "y": 1.0,
    "z": 0.0
  },
  "relativeVelocity": {
    "x": 0.1,
    "y": -0.2,
    "z": 0.0
  },
  "isSelfCollision": false
}
```

### Joint Dynamics Event Data

```json
{
  "violationType": "VelocityLimit",
  "jointIndex": 5,
  "currentValue": 195.7,
  "limitValue": 190.0,
  "safetyFactor": 0.8,
  "effectiveLimitValue": 152.0,
  "violationPercent": 127.9,
  "jointAngles": [15.2, -45.8, 30.1, 0.0, 90.0, -10.5],
  "jointVelocities": [2.1, -1.8, 0.5, 0.0, 3.2, 195.7],
  "jointAccelerations": [0.1, 0.2, -0.1, 0.0, -0.5, 25.3],
  "partAttached": true,
  "partMass": 2.5
}
```

## Error Handling & Recovery

### HTTP Error Codes

```csharp
public enum RWSErrorCode
{
    // Success
    OK = 200,
    Created = 201,
    NoContent = 204,
    
    // Client Errors
    BadRequest = 400,           // Malformed request
    Unauthorized = 401,         // Authentication required
    Forbidden = 403,            // Access denied
    NotFound = 404,             // Resource not found
    MethodNotAllowed = 405,     // HTTP method not supported
    
    // Server Errors
    InternalServerError = 500,  // Controller internal error
    NotImplemented = 501,       // Feature not supported
    ServiceUnavailable = 503    // Controller busy/unavailable
}
```

### Error Response Format

```xml
<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>Error</title></head>
<body>
<div class="error">
  <span class="code">400</span>
  <span class="name">Bad Request</span>
  <span class="description">Invalid resource path</span>
</div>
</body>
</html>
```

### Connection Recovery

```csharp
public class ConnectionRecoveryService
{
    private readonly TimeSpan[] retryDelays = {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };
    
    public async Task<bool> AttemptRecoveryAsync()
    {
        for (int attempt = 0; attempt < retryDelays.Length; attempt++)
        {
            try
            {
                await Task.Delay(retryDelays[attempt]);
                
                // Re-authenticate
                if (await authService.AuthenticateAsync())
                {
                    // Reconnect WebSocket
                    await webSocketService.ConnectAsync(subscriptionUrl);
                    
                    // Restart motion polling
                    motionDataService.StartPolling();
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Recovery attempt {attempt + 1} failed: {ex.Message}");
            }
        }
        
        return false;
    }
}
```

## Performance Characteristics

### Measured Latencies

| Communication Type | Typical Latency | Maximum Latency | Update Rate |
|-------------------|----------------|-----------------|-------------|
| WebSocket Events  | 20-50ms        | 100ms          | Event-driven |
| HTTP Motion Data  | 80-120ms       | 200ms          | 50-1000ms   |
| Authentication    | 100-300ms      | 500ms          | On-demand   |
| Subscription Setup| 200-500ms      | 1000ms         | Once        |

### Bandwidth Utilization

| Data Stream        | Bytes/Update | Updates/Sec | Bandwidth    |
|-------------------|--------------|-------------|--------------|
| Joint Angles      | ~150 bytes   | 10 Hz       | 1.5 KB/s     |
| Execution Events  | ~200 bytes   | Variable    | <1 KB/s      |
| I/O Signal Events | ~120 bytes   | Variable    | <0.5 KB/s    |
| Safety Events     | ~2 KB        | Variable    | <1 KB/s      |
| **Total**         |              |             | **~4 KB/s**  |

### Threading Model

```
┌─────────────────┐    Unity Main Thread    ┌──────────────────┐
│  Unity GameLoop │◄──────────────────────►│   Safety System  │
└─────────────────┘                        └──────────────────┘
         ▲                                           ▲
         │                                           │
         ▼                                           ▼
┌─────────────────┐    Background Threads   ┌──────────────────┐
│ Network I/O     │◄──────────────────────►│  Event Processing │
│ - HTTP Polling  │                        │  - State Updates  │
│ - WebSocket Rx  │                        │  - Event Queue    │
│ - Authentication│                        │  - JSON Logging   │
└─────────────────┘                        └──────────────────┘
```

## Security Considerations

### Authentication Security

1. **Credential Storage**:
   - Never store credentials in plain text
   - Use Unity's secure credential storage
   - Consider certificate-based authentication for production

2. **Network Security**:
   - Use HTTPS where supported
   - Implement certificate validation
   - Consider VPN for remote access

3. **Access Control**:
   - Use principle of least privilege for robot user accounts
   - Regularly rotate credentials
   - Monitor access logs

### Data Privacy

1. **Sensitive Data Handling**:
   - Sanitize logs of sensitive information
   - Encrypt safety event logs if required
   - Implement data retention policies

2. **Network Traffic**:
   - Monitor for unauthorized access attempts
   - Log all authentication events
   - Implement rate limiting where possible

## Protocol Extensions

### Custom Data Parsers

```csharp
public class CustomRobotDataParser : IRobotDataParser
{
    public bool CanParse(string rawData)
    {
        // Implement custom format detection
        return rawData.StartsWith("CUSTOM:");
    }
    
    public void ParseData(string rawData, RobotState robotState)
    {
        // Implement custom parsing logic
        string customData = rawData.Substring(7); // Remove "CUSTOM:" prefix
        
        // Parse and update robot state
        var parsedData = JsonConvert.DeserializeObject<CustomDataFormat>(customData);
        robotState.SetCustomData("custom_field", parsedData.Value);
    }
}
```

### Additional Robot Types

The protocol architecture supports extending to other robot manufacturers:

```csharp
public class KUKAConnector : MonoBehaviour, IRobotConnector
{
    // Implement KUKA-specific communication protocol
    // Example: KRL XML interface or RSI real-time interface
}

public class UniversalRobotsConnector : MonoBehaviour, IRobotConnector  
{
    // Implement UR-specific communication protocol
    // Example: Real-Time Data Exchange (RTDE) or Dashboard Server
}
```

This modular approach allows the framework to support multiple robot manufacturers while maintaining consistent interfaces and safety monitoring capabilities.
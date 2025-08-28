# API Reference

## Core Interfaces

### IRobotConnector

**Location**: `Assets/Scripts/RobotSystem/Interfaces/IRobotConnector.cs`

Primary interface for robot communication implementations.

#### Properties
```csharp
bool IsConnected { get; }
RobotState CurrentState { get; }
```

#### Events
```csharp
event Action<RobotState> OnRobotStateUpdated;
event Action<bool> OnConnectionStateChanged;
```

#### Methods
```csharp
void Connect();
void Disconnect();
```

**Implementation Requirements**:
- Must handle authentication and session management
- Should implement thread-safe state updates
- Must trigger events on state changes
- Should provide graceful error handling

### IRobotSafetyMonitor

**Location**: `Assets/Scripts/RobotSystem/Interfaces/IRobotSafetyMonitor.cs`

Interface for implementing safety monitoring algorithms.

#### Properties
```csharp
string MonitorName { get; }    // Human-readable monitor identifier
bool IsActive { get; }         // Current monitor state
```

#### Events
```csharp
event Action<SafetyEvent> OnSafetyEventDetected;
```

#### Methods
```csharp
void Initialize();                    // Setup resources and configurations
void UpdateState(RobotState state);  // Process robot state for safety analysis
void SetActive(bool active);         // Enable/disable monitoring
void Shutdown();                     // Cleanup and resource disposal
```

**Implementation Guidelines**:
- Initialize() should be idempotent
- UpdateState() must be thread-safe
- Use cooldown periods to prevent event spam
- Include context data in SafetyEvent objects

### IRobotDataParser

**Location**: `Assets/Scripts/RobotSystem/Interfaces/IRobotDataParser.cs`

Strategy interface for parsing robot-specific data formats.

#### Methods
```csharp
bool CanParse(string rawData);                      // Format detection
void ParseData(string rawData, RobotState robotState); // Data extraction and state update
```

**Implementation Notes**:
- CanParse() should be fast and reliable
- ParseData() must handle malformed data gracefully
- Should update robotState atomically where possible

### IRobotVisualization

**Location**: `Assets/Scripts/RobotSystem/Interfaces/IRobotVisualization.cs`

Interface for robot visualization adapters.

#### Properties
```csharp
bool IsConnected { get; }
bool IsValid { get; }
string VisualizationType { get; }
```

#### Methods
```csharp
void Initialize();
void UpdateJointAngles(float[] jointAngles);
void Shutdown();
```

## Core Classes

### RobotState

**Location**: `Assets/Scripts/RobotSystem/Core/RobotState.cs`

Central state container for robot data.

#### Core Properties
```csharp
// Connection Information
string robotType;           // Robot manufacturer/model
string robotIP;            // Network address
DateTime lastUpdate;       // Last state modification timestamp

// Execution State
bool isRunning;            // Program execution status
string motorState;         // Motor system state
string executionCycle;     // Current execution cycle

// Program Pointer
string currentModule;      // Current RAPID module
string currentRoutine;     // Current routine name
int currentLine;          // Line number in routine
int currentColumn;        // Column position

// Motion Data
float[] jointAngles;       // Joint positions (degrees)
float[] jointVelocities;   // Joint velocities (deg/s)
bool hasValidJointData;    // Data validity flag
DateTime lastJointUpdate;  // Joint data timestamp
double motionUpdateFrequencyHz; // Update rate measurement

// Controller State
string controllerState;    // Controller mode (AUTO, MANUAL, etc.)

// I/O Signals
Dictionary<string, object> ioSignals; // Signal name -> value mapping
```

#### Key Methods
```csharp
void UpdateMotorState(string state);
void UpdateProgramPointer(string module, string routine, int line, int col);
void UpdateIOSignal(string signalName, object value, string state = "", string quality = "");
void UpdateJointAngles(float[] angles, double updateFrequency = 0.0);
void UpdateJointVelocities(float[] velocities);

// Data Access
float[] GetJointAngles();
T GetIOSignal<T>(string signalName, T defaultValue = default(T));
string GetIOSignalState(string signalName);
string GetIOSignalQuality(string signalName);

// Gripper Convenience Properties
bool GripperOpen => GetIOSignal<bool>("do_gripperopen", false);
bool GripperClosed => !GripperOpen;
```

#### Thread Safety
- All Update methods are thread-safe
- State access methods return copies/immutable data
- Dictionary operations are synchronized

### RobotManager

**Location**: `Assets/Scripts/RobotSystem/Core/RobotManager.cs`

Mediator class coordinating robot operations.

#### Properties
```csharp
bool IsConnected { get; }
RobotState CurrentRobotState { get; }
```

#### Events
```csharp
event Action<RobotState> OnRobotStateUpdated;
event Action<bool> OnConnectionStateChanged;
event Action<string, string> OnMotorStateChanged; // (oldState, newState)
```

#### Methods
```csharp
void ConnectToRobot();
void DisconnectFromRobot();
RobotState GetCurrentState();
bool HasValidMotionData();
float[] GetCurrentJointAngles();
double GetMotionUpdateFrequency();
```

### RobotSafetyManager

**Location**: `Assets/Scripts/RobotSystem/Core/RobotSafetyManager.cs`

Facade for coordinating safety monitoring subsystem.

#### Configuration Properties
```csharp
bool enableJsonLogging;              // Enable file-based logging
bool logOnlyWhenProgramRunning;      // Conditional logging based on program state
string logDirectory;                 // Log file directory
SafetyEventType minimumLogLevel;     // Minimum severity for logging
```

#### Methods
```csharp
void SetMonitorActive(string monitorName, bool active);
List<string> GetActiveMonitors();
void ClearEventHistory();
List<SafetyEvent> GetEventHistory(DateTime since);
```

#### Event Handling
- Aggregates events from all monitors
- Applies filtering based on severity levels
- Handles conditional logging logic
- Provides event replay capability

### SafetyEvent

**Location**: `Assets/Scripts/RobotSystem/Core/SafetyEvent.cs`

Immutable value object for safety incidents.

#### Properties
```csharp
string monitorName;                  // Source monitor identifier
SafetyEventType eventType;           // Severity level
string description;                  // Human-readable description
DateTime timestamp;                  // Event occurrence time
RobotStateSnapshot robotStateSnapshot; // Complete robot context
string eventDataJson;                // Serialized event-specific data
```

#### Event Types
```csharp
public enum SafetyEventType
{
    Info = 0,        // Informational events
    Warning = 1,     // Potential safety concerns
    Critical = 2,    // Immediate safety hazards
    Emergency = 3    // Emergency stop conditions
}
```

#### Methods
```csharp
void SetEventData<T>(T data);       // Attach typed event data
T GetEventData<T>();                // Retrieve typed event data
string ToJson();                    // Serialize complete event
static SafetyEvent FromJson(string json); // Deserialize event
```

### RobotStateSnapshot

**Location**: `Assets/Scripts/RobotSystem/Core/RobotStateSnapshot.cs`

Immutable snapshot of robot state at specific moment.

#### Properties
```csharp
DateTime captureTime;               // Snapshot timestamp
bool isProgramRunning;              // Execution state
string currentModule, currentRoutine;
int currentLine, currentColumn;
string executionCycle, motorState, controllerState;
float[] jointAngles;                // Joint configuration
bool hasValidJointData;
double motionUpdateFrequencyHz;
bool gripperOpen;
string robotType, robotIP;
```

#### Methods
```csharp
string GetProgramContext();         // Formatted program location
string GetMotionSummary();         // Joint configuration summary
Dictionary<string, object> ToDictionary(); // Serializable representation
```

## Safety Monitor Implementations

### SingularityDetectionMonitor

**Location**: `Assets/Scripts/RobotSystem/Monitors/SingularityDetectionMonitor.cs`

Detects kinematic singularities in 6-DOF spherical wrist robots.

#### Configuration
```csharp
float wristSingularityThreshold = 10f;      // Degrees
float shoulderSingularityThreshold = 0.1f;   // Meters
float elbowSingularityThreshold = 0.01f;     // Normalized threshold
bool checkWristSingularity = true;
bool checkShoulderSingularity = true;
bool checkElbowSingularity = true;
```

#### Mathematical Implementation

**Wrist Singularity Detection**:
```csharp
private bool IsWristSingularityDH(float[] jointAngles)
{
    float theta5_deg = Mathf.Abs(jointAngles[4]);
    return theta5_deg < wristSingularityThreshold || 
           Mathf.Abs(180f - theta5_deg) < wristSingularityThreshold;
}
```

**Shoulder Singularity Detection**:
```csharp
private bool IsShoulderSingularityDH(float[] jointAngles)
{
    Vector3 wristCenter = ComputeJointPosition(jointAngles, 5);
    Vector3 basePosition = robotFrames[0].transform.position;
    Vector3 wristToBase = wristCenter - basePosition;
    
    // Distance from Y₀ axis in Unity coordinates
    float distanceFromY0 = Mathf.Sqrt(wristToBase.x * wristToBase.x + wristToBase.z * wristToBase.z);
    return distanceFromY0 < shoulderSingularityThreshold;
}
```

**Elbow Singularity Detection**:
```csharp
private bool IsElbowSingularityDH(float[] jointAngles)
{
    Vector3 joint2Position = ComputeJointPosition(jointAngles, 2);
    Vector3 joint3Position = ComputeJointPosition(jointAngles, 3);
    Vector3 joint5Position = ComputeJointPosition(jointAngles, 5);
    
    return ArePointsCoplanar(joint2Position, joint3Position, joint5Position, elbowSingularityThreshold);
}
```

#### Event Data Structure
```csharp
public class SingularityInfo
{
    public string singularityType;      // "Wrist", "Shoulder", "Elbow"
    public float[] jointAngles;         // Robot configuration at detection
    public float wristThreshold;        // Detection parameters
    public float shoulderThreshold;
    public float elbowThreshold;
    public DateTime detectionTime;
    public string dhAnalysis;           // Analysis method description
    public bool isEntering;             // true = entering singularity, false = exiting
}
```

### CollisionDetectionMonitor

**Location**: `Assets/Scripts/RobotSystem/Monitors/CollisionDetectionMonitor.cs`

Unity physics-based collision detection for robot links.

#### Configuration
```csharp
LayerMask collisionLayers = -1;             // Collision detection layers
bool excludeProcessFlowLayer = true;         // Exclude station triggers
bool useExistingCollidersOnly = false;      // Use only pre-configured colliders
float cooldownTime = 1.0f;                  // Duplicate event suppression
List<string> criticalCollisionTags;         // Tags for critical collisions
```

#### Implementation Details
- Automatically discovers robot links via Frame and Tool components
- Generates mesh colliders for robot geometry
- Implements hierarchical collision detection
- Supports self-collision detection between non-adjacent links
- Uses spatial hashing for performance optimization

### JointDynamicsMonitor

**Location**: `Assets/Scripts/RobotSystem/Monitors/JointDynamicsMonitor.cs`

Monitors joint position, velocity, and acceleration limits.

#### Configuration
```csharp
bool useFlangeLimits = true;                 // Extract limits from Flange configuration
float limitSafetyFactor = 0.8f;              // Safety margin (80% of max limits)

// ABB IRB 6700-200/2.60 Specifications
float[] maxJointAngles = { 170f, 85f, 70f, 300f, 130f, 360f };     // Degrees
float[] minJointAngles = { -170f, -65f, -180f, -300f, -130f, -360f };
float[] maxJointVelocities = { 110f, 110f, 110f, 190f, 150f, 210f }; // Deg/s
float[] maxJointAccelerations = { 800f, 800f, 800f, 1500f, 1200f, 1800f }; // Deg/s²

// Monitoring Settings
float monitoringFrequency = 10f;             // Hz
int historyBufferSize = 10;                  // Samples for smoothing
bool enableSmoothing = true;
float smoothingAlpha = 0.3f;                 // EMA factor
int smoothingWindowSize = 5;                 // Moving average window
```

#### Signal Processing
- Finite difference velocity/acceleration calculation
- Exponential moving average smoothing
- Outlier rejection based on statistical thresholds
- Part attachment detection for selective monitoring

### ProcessFlowMonitor

**Location**: `Assets/Scripts/RobotSystem/Monitors/ProcessFlowMonitor.cs`

Validates part movement through manufacturing stations.

#### Configuration
```csharp
bool monitorAllParts = true;                 // Monitor all Part components
List<Part> specificParts;                   // Alternatively, monitor specific parts
bool autoDiscoverParts = true;              // Auto-find parts in scene
bool autoDiscoverStations = true;           // Auto-find stations in scene
bool treatSkippedStationsAsWarning = true;  // Severity level for skipped stations
bool treatWrongSequenceAsCritical = true;   // Severity level for sequence violations
float violationCooldownTime = 1.0f;         // Duplicate event suppression
```

#### Layer Configuration
- Layer 30: Parts layer (for objects being processed)
- Layer 31: ProcessFlow layer (for station triggers)
- Automatic layer collision matrix configuration

## ABB-Specific Components

### ABBRWSConnectionClient

**Location**: `Assets/Scripts/RobotSystem/ABB/RWS/ABBRWSConnectionClient.cs`

Main connector for ABB Robot Web Services.

#### Configuration
```csharp
string robotIP = "127.0.0.1";              // Robot controller IP
string username = "Default User";            // RWS authentication username  
string password = "robotics";               // RWS authentication password
bool enableMotionData = true;               // Enable joint data polling
int motionPollingIntervalMs = 100;          // Motion data update interval
string robName = "ROB_1";                  // Robot identifier in controller
```

#### Service Components
- **ABBAuthenticationService**: Digest authentication management
- **ABBSubscriptionService**: WebSocket subscription management  
- **ABBWebSocketService**: Real-time event handling
- **ABBMotionDataService**: HTTP-based motion data polling

#### WebSocket Subscriptions
Default subscriptions created on connection:
- `/rw/rapid/execution;ctrlexecstate` - Execution state changes
- `/rw/rapid/tasks/T_ROB1/pcp;programpointerchange` - Program pointer updates
- `/rw/iosystem/signals/Local/Unit/DO_GripperOpen;state` - Gripper I/O state
- `/rw/panel/ctrl-state` - Controller mode changes
- `/rw/rapid/execution;rapidexeccycle` - Execution cycle events

### ABBFlangeAdapter

**Location**: `Assets/Scripts/RobotSystem/ABB/ABBFlangeAdapter.cs`

Reflection-based adapter for Preliy Flange visualization framework.

#### Properties
```csharp
MonoBehaviour controllerComponent;          // Reference to Controller.cs script
bool IsConnected { get; }                   // Flange integration status
bool IsValid { get; }                       // Controller validity state
string VisualizationType { get; }           // "Preliy Flange"
```

#### Integration Details
- Uses C# reflection to avoid hard dependencies
- Thread-safe joint angle updates via ConcurrentQueue
- Automatic validity monitoring
- Graceful degradation when Flange unavailable

### RapidTargetGenerator

**Location**: `Assets/Scripts/RobotSystem/Core/RapidTargetGenerator.cs`

RAPID code generation for robot targets.

#### Output Formats

**JOINTTARGET**:
```
[[j1,j2,j3,j4,j5,j6],[9E9,9E9,9E9,9E9,9E9,9E9]]
```

**ROBTARGET**:
```
[[x,y,z],[qx,qy,qz,qw],[cf1,cf4,cf6,cfx],[9E9,9E9,9E9,9E9,9E9,9E9]]
```

#### Coordinate System Conversion
```csharp
// Unity → ABB transformation
float x = position.z * 1000f;  // Unity Z → ABB X (forward)
float y = position.x * 1000f;  // Unity X → ABB Y (left)  
float z = position.y * 1000f;  // Unity Y → ABB Z (up)
```

#### Methods
```csharp
void UpdateTargets();                       // Recalculate targets from current robot state
string GetJoinTarget();                     // Current JOINTTARGET string
string GetRobTarget();                      // Current ROBTARGET string  
void CopyJoinTargetToClipboard();          // Copy JOINTTARGET to system clipboard
void CopyRobTargetToClipboard();           // Copy ROBTARGET to system clipboard
void CopyBothTargetsToClipboard();         // Copy formatted RAPID code
```

## Process Flow Components

### Part

**Location**: `Assets/Scripts/RobotSystem/Core/Part.cs`

Represents a workpiece in the manufacturing process.

#### Configuration
```csharp
string partId;                              // Unique part identifier
string partName;                            // Human-readable name
string partType;                            // Part category/classification
Station[] requiredStationSequence;         // Required processing sequence
bool enforceSequence = true;                // Enable sequence validation
bool allowSkipStations = false;             // Allow non-sequential processing
```

#### State Tracking
```csharp
Station currentStation;                     // Current part location
DateTime lastStationChangeTime;             // Last movement timestamp
int currentSequenceIndex;                   // Position in required sequence
List<StationVisit> visitHistory;           // Complete movement history
```

#### Methods
```csharp
bool TryMoveToStation(Station newStation);      // Validated station transition
bool IsValidNextStation(Station station);        // Sequence validation
Station GetNextRequiredStation();                // Next station in sequence
bool IsSequenceComplete();                       // Sequence completion status
float GetCompletionPercentage();                 // Process progress (0-100%)
void ResetProcess();                             // Reset to initial state
```

#### Events
```csharp
event Action<Part, Station, Station> OnStationChanged;          // (part, from, to)
event Action<Part, Station, Station> OnInvalidStationTransition; // (part, from, attempted)
```

### Station

**Location**: `Assets/Scripts/RobotSystem/Core/Station.cs`

Represents a processing station in the manufacturing workflow.

#### Configuration
```csharp
string stationName;                         // Human-readable station name
int stationIndex;                          // Sequence position number
Color stationColor;                        // Visualization color
LayerMask partDetectionLayers;             // Layers for part detection
float detectionDelay = 0.1f;               // Debounce delay
bool autoConfigureForProcessFlow = true;   // Automatic layer setup
```

#### Layer Configuration
- Station GameObject automatically assigned to Layer 31 (ProcessFlow)
- Collider configured as trigger for part detection
- Collision matrix configured to avoid interference with physics

#### Methods
```csharp
bool IsValidNextStation(Station fromStation, Part part); // Sequence validation
void ManualConfigureForProcessFlow();                    // Manual layer setup
```

#### Events
```csharp
event Action<Part, Station> OnPartEntered;    // Part enters station
event Action<Part, Station> OnPartExited;     // Part leaves station
```
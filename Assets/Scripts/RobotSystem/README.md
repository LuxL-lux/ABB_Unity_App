# Robot Communication System

A modular, extensible robot communication system using the Strategy pattern for Unity.

## Architecture Overview

```
RobotSystem/
├── Core/                          # Shared components for all robot types
│   ├── RobotState.cs             # Generic robot state container
│   ├── RobotManager.cs           # Robot-agnostic manager
│   ├── RobotSafetyManager.cs     # Safety monitoring coordinator
│   ├── SafetyEvent.cs            # Safety event data structure
│   └── RobotStateSnapshot.cs     # Immutable state snapshot for logging
├── Interfaces/                    # Core interfaces
│   ├── IRobotConnector.cs        # Main robot connector interface
│   ├── IRobotDataParser.cs       # Data parser strategy interface
│   ├── IRobotVisualization.cs    # Robot visualization interface
│   └── IRobotSafetyMonitor.cs    # Safety monitoring interface
└── ABB/                          # ABB-specific implementations
    ├── RWS/                      # Robot Web Services implementation
    │   ├── ABBRWSSubscriptionClient.cs  # ABB RWS connector with motion data
    │   ├── ABBRWSDataParser.cs          # ABB XML data parser
    │   └── ABBMotionDataService.cs      # Joint angle fetching service
    ├── ABBFlangeAdapter.cs       # Flange framework integration
    └── ButtonAttribute.cs        # Unity inspector button attribute
├── Safety/                       # Robot safety monitoring implementations
│   ├── CollisionDetectionMonitor.cs    # Collision detection using Unity colliders
│   └── SingularityDetectionMonitor.cs  # Singularity detection for 6-DOF robots
```

## Key Components

### Core Interfaces

- **`IRobotConnector`**: Main interface for robot connections
  - Connect/Disconnect methods
  - State change events
  - Generic robot state access

- **`IRobotDataParser`**: Strategy pattern for parsing different data formats
  - `CanParse()`: Check if parser can handle data format
  - `ParseData()`: Parse and update robot state

### Core Classes

- **`RobotState`**: Generic robot state container
  - Works with any robot brand
  - **Motion data**: Joint angles, velocities, update frequencies
  - **Execution tracking**: Program pointer, line numbers, execution state
  - **I/O signals**: Digital/analog inputs and outputs
  - Extensible with custom data
  - Built-in convenience methods for common operations

- **`RobotManager`**: Robot-agnostic manager
  - Works with any `IRobotConnector` implementation
  - Provides unified API regardless of robot type
  - Event-driven architecture

### ABB Implementation

- **`ABBRWSSubscriptionClient`**: Complete ABB Robot Web Services connector
  - **WebSocket real-time subscriptions** for events (I/O, execution state)
  - **HTTP motion data polling** for joint angles (configurable frequency)
  - **Integrated authentication** (single session for all data types)
  - **Automatic Flange integration** (real-time robot visualization)
  - Implements `IRobotConnector` interface

- **`ABBRWSDataParser`**: Parses ABB RWS XML event data
  - Execution state tracking
  - Program pointer monitoring
  - I/O signal state changes
  - Controller state updates

- **`ABBMotionDataService`**: High-performance joint data fetching
  - **Non-blocking HTTP polling** with configurable intervals
  - **Performance tracking** (update frequency, error rates)
  - **Shared authentication** (reuses existing HTTP session)
  - Thread-safe joint angle updates

- **`ABBFlangeAdapter`**: Preliy Flange framework integration
  - **Reflection-based** integration (no direct dependencies)
  - **Automatic JointTarget updates** from robot data
  - **Safe connection handling** with state validation

## Usage Examples

### Basic Setup
```csharp
// Setup in Unity Inspector:
// 1. Create GameObject with ABBRWSSubscriptionClient
// 2. Create GameObject with ABBFlangeAdapter
// 3. Assign your Controller.cs script to ABBFlangeAdapter's "Controller Component"
// 4. Assign ABBFlangeAdapter to ABBRWSSubscriptionClient's "Flange Adapter Component"
// 5. Enable "Enable Motion Data" checkbox
// 6. Configure motion polling interval (default: 100ms)

// Use through RobotManager for robot-agnostic operations
var manager = gameObject.GetComponent<RobotManager>();
manager.ConnectToRobot();

// Access current state (includes motion data!)
var state = manager.GetCurrentState();
Debug.Log($"Robot running: {state.isRunning}");
Debug.Log($"Gripper open: {state.GripperOpen}");
Debug.Log($"Joint angles: [{string.Join(", ", state.jointAngles)}]");
Debug.Log($"Motion frequency: {state.motionUpdateFrequencyHz:F1} Hz");
```

### Event-Driven Monitoring
```csharp
public class MyRobotController : MonoBehaviour
{
    void Start()
    {
        var connector = FindObjectOfType<ABBRWSSubscriptionClient>();
        connector.OnRobotStateUpdated += OnRobotStateChanged;
        connector.OnConnectionStateChanged += OnConnectionChanged;
        connector.OnJointDataReceived += OnJointDataReceived;
    }
    
    private void OnRobotStateChanged(RobotState state)
    {
        if (state.isRunning)
        {
            Debug.Log($"Executing: {state.currentModule}.{state.currentRoutine}:{state.currentLine}");
        }
        
        if (state.GripperOpen)
        {
            // Handle gripper open
        }
        
        if (state.hasValidJointData)
        {
            Debug.Log($"Motion update: {state.motionUpdateFrequencyHz:F1} Hz");
        }
    }
    
    private void OnJointDataReceived(float[] jointAngles)
    {
        // Real-time joint angle updates for custom processing
        Debug.Log($"Joint 1: {jointAngles[0]:F2}°, Joint 2: {jointAngles[1]:F2}°");
        
        // Your custom logic here (collision detection, path planning, etc.)
    }
    
    private void OnConnectionChanged(bool connected)
    {
        Debug.Log($"Robot {(connected ? "connected" : "disconnected")}");
    }
}
```

### Motion Data Access
```csharp
public class MotionAnalyzer : MonoBehaviour
{
    void Update()
    {
        var manager = FindObjectOfType<RobotManager>();
        
        // Check if motion data is available
        if (manager.HasValidMotionData())
        {
            float[] joints = manager.GetCurrentJointAngles();
            double frequency = manager.GetMotionUpdateFrequency();
            
            // Analyze joint motion
            if (joints[0] > 90f) // Joint 1 over 90 degrees
            {
                Debug.LogWarning("Joint 1 approaching limit!");
            }
            
            // Performance monitoring
            if (frequency < 5.0) // Less than 5 Hz
            {
                Debug.LogWarning("Low motion update frequency!");
            }
        }
    }
}
```

## Extending the System

### Adding New Robot Types

1. **Create robot-specific folder**: `RobotSystem/KUKA/` or `RobotSystem/UniversalRobots/`

2. **Implement IRobotConnector**:
```csharp
public class KUKAConnector : MonoBehaviour, IRobotConnector
{
    public event Action<RobotState> OnRobotStateUpdated;
    public event Action<bool> OnConnectionStateChanged;
    // ... implement interface methods
}
```

3. **Create data parser** (if needed):
```csharp
public class KUKADataParser : IRobotDataParser
{
    public bool CanParse(string rawData) { /* check format */ }
    public void ParseData(string rawData, RobotState robotState) { /* parse and update */ }
}
```

4. **Use with RobotManager** - no other code changes needed!

### Adding Custom Robot State Data

```csharp
// Store custom data
robotState.SetCustomData("temperature", 45.2f);
robotState.SetCustomData("tool_id", "gripper_v2");

// Retrieve custom data
float temp = robotState.GetCustomData<float>("temperature", 0f);
string toolId = robotState.GetCustomData<string>("tool_id", "unknown");
```

## Safety Monitoring System

### Architecture

The safety monitoring system follows the **Observer** and **Strategy** design patterns to provide modular, extensible safety monitoring for robot operations:

```
RobotManager (OnStateUpdated) 
    ↓
RobotSafetyManager (Observer/Coordinator)
    ↓ (distributes state to all active monitors)
CollisionDetectionMonitor, SingularityDetectionMonitor, etc. (Strategy implementations)
    ↓ (safety events with complete state snapshots)
JSON Files (when program running) OR Console Logging (when idle)
```

### Core Safety Components

#### IRobotSafetyMonitor Interface
**Design Pattern**: Strategy Pattern
- Allows pluggable safety monitoring algorithms
- Each monitor is independent and swappable
- Common interface ensures consistency across all safety monitors

```csharp
public interface IRobotSafetyMonitor
{
    string MonitorName { get; }
    bool IsActive { get; }
    event Action<SafetyEvent> OnSafetyEventDetected;
    
    void Initialize();
    void UpdateState(RobotState state);
    void SetActive(bool active);
    void Shutdown();
}
```

#### RobotSafetyManager
**Design Patterns**: Observer Pattern, Facade Pattern
- **Observer**: Subscribes to RobotManager state updates and distributes to all monitors
- **Facade**: Provides simplified interface for managing complex safety subsystem
- **Single Responsibility**: Coordinates safety monitors and handles event logging

#### SafetyEvent & RobotStateSnapshot
**Design Patterns**: Value Object, Immutable Object, Command Pattern
- **Value Object**: SafetyEvent encapsulates all data about a safety incident
- **Immutable**: RobotStateSnapshot preserves exact robot state at time of incident
- **Command**: Can be serialized/deserialized for logging and replay

### Key Features

✅ **Conditional Logging**: JSON files only when robot program is running, console logging when idle  
✅ **Complete State Capture**: Full robot context (joint angles, program pointer, I/O states)  
✅ **Modular Architecture**: Easy to add new safety monitors without changing existing code  
✅ **RobotState-Only Dependencies**: Safety monitors depend only on generic RobotState, not RWS-specific components  
✅ **Event-Driven**: Real-time safety monitoring with immediate response  
✅ **Extensible Data**: Each monitor can attach custom event data  
✅ **Performance Optimized**: Configurable cooldown periods prevent event spam  

### Included Safety Monitors

#### CollisionDetectionMonitor
- Uses Unity's physics system and colliders
- Configurable collision layers and detection radius
- Self-collision detection (optional)
- Real-time collision point visualization in Scene view

#### SingularityDetectionMonitor  
- Detects wrist, shoulder, and elbow singularities for 6-DOF robots
- Configurable singularity thresholds
- Mathematical analysis of joint configurations

### Usage Example

```csharp
public class SafetySystemExample : MonoBehaviour
{
    void Start()
    {
        var safetyManager = FindObjectOfType<RobotSafetyManager>();
        
        // Subscribe to safety events
        safetyManager.OnSafetyEventDetected += OnSafetyEventDetected;
        
        // Enable/disable specific monitors
        safetyManager.SetMonitorActive("Collision Detector", true);
        safetyManager.SetMonitorActive("Singularity Detector", false);
    }
    
    private void OnSafetyEventDetected(SafetyEvent safetyEvent)
    {
        Debug.Log($"Safety Event: {safetyEvent.description}");
        Debug.Log($"Program Context: {safetyEvent.robotStateSnapshot.GetProgramContext()}");
        
        // Access event-specific data
        if (safetyEvent.monitorName == "Collision Detector")
        {
            var collisionData = safetyEvent.GetEventData<CollisionInfo>();
            Debug.Log($"Collision between {collisionData.robotLink} and {collisionData.collisionObject}");
        }
    }
}
```

### JSON Output Example

When a program is running, safety events are logged as detailed JSON files:

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
    "currentColumn": 15,
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

### Design Pattern Validation

The safety monitoring system adheres to several key software design principles:

#### SOLID Principles
- **Single Responsibility**: Each monitor has one safety concern, manager coordinates
- **Open/Closed**: Easy to add new monitors without modifying existing code
- **Liskov Substitution**: All monitors implement the same interface consistently  
- **Interface Segregation**: Clean, focused interfaces (IRobotSafetyMonitor)
- **Dependency Inversion**: Depends on abstractions (RobotState) not concretions (RWS)

#### Design Patterns Used
- **Strategy Pattern**: Pluggable safety monitoring algorithms
- **Observer Pattern**: Event-driven architecture for state updates and safety events
- **Facade Pattern**: RobotSafetyManager simplifies complex safety subsystem
- **Command Pattern**: SafetyEvent encapsulates safety incidents with full context
- **Template Method**: Base safety monitor workflow in interface

#### Additional Benefits
- **Testable**: Each monitor can be unit tested independently
- **Maintainable**: Clear separation of concerns and responsibilities
- **Extensible**: Plugin architecture for new safety monitors
- **Reliable**: Immutable state snapshots prevent data corruption
- **Debuggable**: Complete program context captured with each safety event

## Configuration

### Safety Monitoring Configuration

#### RobotSafetyManager Settings
- **`enableJsonLogging`**: Enable detailed JSON logging to files
- **`logOnlyWhenProgramRunning`**: Only create JSON logs during program execution
- **`logDirectory`**: Directory for safety log files (relative to Application.persistentDataPath)
- **`minimumLogLevel`**: Minimum severity to log (Info, Warning, Critical, Emergency)

#### CollisionDetectionMonitor Settings
- **`collisionLayers`**: Unity layer mask for collision detection
- **`collisionCheckRadius`**: Detection radius around robot links
- **`detectSelfCollisions`**: Enable collision detection between robot parts
- **`cooldownTime`**: Minimum time between duplicate collision reports

#### SingularityDetectionMonitor Settings  
- **`singularityThreshold`**: Angular threshold for singularity detection (degrees)
- **`checkWristSingularity`**: Monitor wrist singularities (J5 near 0°/180°)
- **`checkShoulderSingularity`**: Monitor shoulder singularities (arm fully extended)
- **`checkElbowSingularity`**: Monitor elbow singularities (J3 near 0°)

### ABB RWS Configuration

#### WebSocket Subscriptions (Real-time Events)
The ABB connector subscribes to these resources by default:
- **Execution State**: `/rw/rapid/execution;ctrlexecstate`
- **Program Pointer**: `/rw/rapid/tasks/T_ROB1/pcp;programpointerchange`
- **Gripper Signal**: `/rw/iosystem/signals/Local/Unit/DO_GripperOpen;state`
- **Controller State**: `/rw/panel/ctrl-state`
- **Execution Cycles**: `/rw/rapid/execution;rapidexeccycle`

#### HTTP Motion Data Polling
- **Joint Angles**: `/rw/rapid/tasks/{taskName}/motion?resource=jointtarget&json=1`
- **Configurable Interval**: 50-1000ms (default: 100ms)
- **Shared Authentication**: Reuses WebSocket session cookies

#### Configuration Parameters
- **`enableMotionData`**: Enable/disable joint data fetching
- **`motionPollingIntervalMs`**: Update frequency for joint data
- **`taskName`**: ABB RAPID task name (default: "T_ROB1")
- **`flangeAdapterComponent`**: Reference to Flange integration component

To modify subscriptions, edit the `CreateSubscription()` method in `ABBRWSSubscriptionClient.cs`.

## Benefits

### System Architecture
✅ **Modular**: Easy to add new robot types without changing existing code  
✅ **Extensible**: Plugin architecture for data parsers and safety monitors  
✅ **Generic**: Same API works with any robot brand  
✅ **Event-Driven**: React to state changes in real-time  
✅ **High Performance**: Optimized motion data polling with configurable rates  
✅ **Integrated**: Automatic Flange framework synchronization  
✅ **Efficient**: Shared authentication, no connection overhead  
✅ **Thread-Safe**: Concurrent access to robot data  
✅ **Clean Architecture**: Separation of concerns with clear interfaces  
✅ **Future-Proof**: Easy to extend with new features and robot types  

### Safety Monitoring
✅ **Comprehensive Logging**: Complete robot state captured with every safety event  
✅ **Smart Logging**: JSON files for program debugging, console for development  
✅ **Vendor Agnostic**: Safety monitors work with any robot brand  
✅ **Real-time Detection**: Immediate safety event detection and response  
✅ **Configurable**: Adjustable thresholds and detection parameters  
✅ **Extensible Safety**: Easy to add custom safety monitors  
✅ **Design Pattern Compliant**: Follows SOLID principles and proven design patterns  
✅ **Debug-Ready**: Full program context for safety incident analysis  

## Migration from Old System

The old files in `Assets/Scripts/ABB/` can be gradually migrated or removed:
- `RWSSubscriptionClient.cs` → `RobotSystem/ABB/RWS/ABBRWSSubscriptionClient.cs`
- `RWSDataParser.cs` → `RobotSystem/ABB/RWS/ABBRWSDataParser.cs`
- `RobotState.cs` → `RobotSystem/Core/RobotState.cs`
- `RobotManager.cs` → `RobotSystem/Core/RobotManager.cs`

Update your GameObject references to use the new components in the `RobotSystem` namespaces.
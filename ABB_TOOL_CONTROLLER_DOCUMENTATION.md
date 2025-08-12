# ABB Tool Controller Documentation

## Overview

The ABB Tool Controller system provides a comprehensive integration between Unity's Flange library tool/gripper components and real ABB robot controllers via Robot Web Services (RWS). This allows for synchronized control of both simulated Unity grippers and physical robot grippers.

## Architecture

### Integration Components

```
Unity Flange Library ←→ ABBToolController ←→ ABB Robot Web Services ←→ Physical Robot
      ↓                        ↓                      ↓                    ↓
  Tool/Gripper          Tool Management        I/O Signals          Hardware Control
  Physics Sim           State Sync            RAPID Calls          Physical Gripper
```

### Core Classes

1. **ABBToolController.cs** - Main controller component
2. **ABBToolControllerEditor.cs** - Custom Unity inspector
3. **ABBToolControllerExample.cs** - Usage examples and demo code

## ABBToolController.cs

### Purpose
Manages tool definitions, coordinates between Flange library components and ABB RWS API, and provides unified gripper control interface.

### Key Features

#### Multi-Method Control
- **Digital I/O Control**: Direct robot controller digital output signals
- **RAPID Procedure Calls**: Execute custom RAPID procedures
- **Hybrid Mode**: Combine both I/O and RAPID methods

#### Tool Definition System
```csharp
[Serializable]
public class ToolDefinition
{
    public string name;                      // Tool identifier
    public Tool flangeToolComponent;         // Flange library tool reference
    public Gripper gripperComponent;         // Flange library gripper reference
    public ToolControlType controlType;      // Control method selection
    public string customOpenSignal;          // Override default I/O signals
    public string customCloseSignal;
    public string customOpenProcedure;       // Override default RAPID procedures
    public string customCloseProcedure;
}
```

#### Control Types
```csharp
public enum ToolControlType
{
    DigitalIO,        // Use robot I/O signals only
    RapidProcedure,   // Use RAPID procedure calls only
    Both              // Use both methods simultaneously
}
```

### Configuration Parameters

#### Digital I/O Settings
- `ioNetwork` - ABB I/O network name (e.g., "Local")
- `ioDevice` - ABB I/O device name (e.g., "DRV_1")
- `gripperOpenSignal` - Default signal name for opening gripper
- `gripperCloseSignal` - Default signal name for closing gripper

#### RAPID Procedure Settings
- `rapidTaskName` - RAPID task name (e.g., "T_ROB1")
- `gripperOpenProcedure` - Default procedure for opening gripper
- `gripperCloseProcedure` - Default procedure for closing gripper

### Public Methods

#### Tool Management
```csharp
void SetActiveTool(int toolIndex)           // Switch active tool
void AddTool(ToolDefinition tool)           // Add new tool definition
void RemoveTool(int index)                  // Remove tool definition
```

#### Gripper Control
```csharp
async void OpenGripper()                    // Open gripper (async)
async void CloseGripper()                   // Close gripper (async)
async Task<bool> ExecuteToolCommand(bool open) // Execute with return status
```

#### Status Properties
```csharp
bool IsGripperOpen { get; }                 // Current gripper state
int ActiveToolIndex { get; }                // Active tool index
ToolDefinition ActiveTool { get; }          // Active tool reference
List<ToolDefinition> Tools { get; }         // All tool definitions
```

### Events
```csharp
event Action<bool> OnGripperStateChanged;           // Gripper state change
event Action<int> OnActiveToolChanged;              // Tool selection change
event Action<string> OnToolCommandExecuted;         // Command success
event Action<string> OnToolError;                   // Error notifications
```

## ABB Robot Web Services Integration

### Digital I/O Control

#### URL Format
```
POST http://{robot-ip}:{port}/rw/iosystem/signals/{network}/{device}/{signal}?action=set
```

#### Request Body
```
Content-Type: application/x-www-form-urlencoded
Body: lvalue=1  (high signal) / lvalue=0  (low signal)
```

#### Example Request
```
POST http://192.168.1.100:80/rw/iosystem/signals/Local/DRV_1/DO_GripperOpen?action=set
Body: lvalue=1
```

### RAPID Procedure Control

#### URL Format
```
POST http://{robot-ip}:{port}/rw/rapid/tasks/{task}/procedure?action=call
```

#### Request Body
```
Content-Type: application/x-www-form-urlencoded
Body: procedure={procedure_name}
```

#### Example Request
```
POST http://192.168.1.100:80/rw/rapid/tasks/T_ROB1/procedure?action=call
Body: procedure=GripperOpen
```

### Authentication
- **Type**: HTTP Digest Authentication
- **Default Credentials**: Username="Default User", Password="robotics"

## Flange Library Integration

### Tool Component
```csharp
public class Tool : MonoBehaviour
{
    public ToolMountType MountType;     // OnRobot or External
    public Transform Point;             // Tool center point reference
    public Matrix4x4 Offset;           // Tool transformation offset
}
```

### Gripper Component  
```csharp
public class Gripper : MonoBehaviour
{
    public bool Gripped;                // Current grip state
    public void Grip(bool grip);        // Control gripper
    public void Grip();                 // Close gripper
    public void Release();              // Open gripper
}
```

### Part Component
```csharp
public class Part : MonoBehaviour
{
    public PartPhysicsType Type;        // Physics or Kinematic
    
    public enum PartPhysicsType
    {
        Physics,    // Full physics simulation
        Kinematic   // Kinematic movement only
    }
}
```

## Setup Instructions

### 1. Component Setup
1. Add `ABBToolController` to robot GameObject (requires `ABBRobotWebServicesController`)
2. Configure connection settings in `ABBRobotWebServicesController`
3. Set up tools in `ABBToolController` inspector

### 2. Tool Configuration
1. Create tool definitions with Flange `Tool` and `Gripper` components
2. Set control type (DigitalIO, RapidProcedure, or Both)
3. Configure I/O signals or RAPID procedure names
4. Test connections and gripper operations

### 3. RAPID Program Requirements

Create these procedures in your RAPID program:

```rapid
PROC GripperOpen()
    ! Open gripper logic
    SetDO DO_GripperOpen, 1;
    WaitTime 0.1;
    SetDO DO_GripperOpen, 0;
    
    ! Wait for gripper to fully open
    WaitTime 0.5;
ENDPROC

PROC GripperClose()
    ! Close gripper logic
    SetDO DO_GripperClose, 1;
    WaitTime 0.1;
    SetDO DO_GripperClose, 0;
    
    ! Wait for gripper to fully close
    WaitTime 0.5;
ENDPROC
```

### 4. I/O Signal Configuration

Configure digital outputs in robot controller:
- `DO_GripperOpen` - Signal to open gripper
- `DO_GripperClose` - Signal to close gripper
- Connect to gripper control hardware

## Usage Examples

### Basic Gripper Control
```csharp
// Get tool controller
var toolController = GetComponent<ABBToolController>();

// Open gripper
await toolController.ExecuteToolCommand(true);

// Close gripper
await toolController.ExecuteToolCommand(false);

// Check gripper state
bool isOpen = toolController.IsGripperOpen;
```

### Event Handling
```csharp
void Start()
{
    var toolController = GetComponent<ABBToolController>();
    
    // Subscribe to events
    toolController.OnGripperStateChanged += OnGripperChanged;
    toolController.OnToolError += OnToolError;
}

void OnGripperChanged(bool isOpen)
{
    Debug.Log($"Gripper is now {(isOpen ? "open" : "closed")}");
}

void OnToolError(string error)
{
    Debug.LogError($"Tool error: {error}");
}
```

### Automated Pick and Place
```csharp
public async Task PerformPickAndPlace()
{
    // Move to pick position (your robot control code)
    await MoveToPosition(pickPosition);
    
    // Open gripper
    bool success = await toolController.ExecuteToolCommand(true);
    if (!success) return;
    
    // Move down to object
    await MoveToPosition(pickPosition + Vector3.down * 0.05f);
    
    // Close gripper
    success = await toolController.ExecuteToolCommand(false);
    if (!success) return;
    
    // Move to place position
    await MoveToPosition(placePosition);
    
    // Release object
    await toolController.ExecuteToolCommand(true);
}
```

## Inspector Interface

### Tool Configuration Section
- **Tools List**: Visual list of all configured tools
- **Active Tool**: Dropdown selection of current tool
- **Tool Info**: Real-time display of active tool status

### Digital I/O Settings
- **Network/Device**: ABB I/O system configuration
- **Signal Names**: Default open/close signal names
- **Per-Tool Overrides**: Custom signals per tool

### RAPID Settings
- **Task Name**: RAPID task containing procedures
- **Procedure Names**: Default open/close procedure names
- **Per-Tool Overrides**: Custom procedures per tool

### Real-Time Controls
- **Open/Close Buttons**: Direct gripper control
- **Tool Selection**: Quick tool switching
- **Status Display**: Command results and errors

## Troubleshooting

### Common Issues

#### Connection Problems
- Verify ABB RWS connection is established
- Check IP address and port settings
- Confirm robot controller web services are enabled

#### I/O Signal Failures
- Verify I/O network, device, and signal names
- Check robot controller I/O configuration
- Confirm signal permissions and mastership

#### RAPID Procedure Failures  
- Verify RAPID task name and procedure names
- Check RAPID program is loaded and available
- Confirm procedure execution permissions

#### Flange Integration Issues
- Ensure Tool and Gripper components are properly assigned
- Verify collider setup for gripper detection
- Check Part components on grippable objects

### Debug Features
- Enable debug logging in `ABBRobotWebServicesController`
- Monitor console for detailed error messages
- Use inspector status display for real-time feedback

## Performance Considerations

### Network Communication
- I/O signals: ~10-50ms response time
- RAPID procedures: ~100-500ms response time
- Use async/await for non-blocking operations

### Unity Integration
- Flange gripper updates are synchronized with RWS calls
- Physics simulation runs independently of robot timing
- Use events for loose coupling between components

## Extending the System

### Custom Tool Types
1. Inherit from `ToolDefinition` for specialized tools
2. Override control methods for custom behavior
3. Add new `ToolControlType` options as needed

### Additional Control Methods
1. Add new communication protocols (e.g., Modbus, EtherCAT)
2. Implement additional RAPID service calls
3. Support for analog outputs and complex sequences

### Integration with Other Systems
1. Add support for multiple robot controllers
2. Integrate with MES/ERP systems
3. Add data logging and analytics features

This documentation provides a complete reference for implementing and using the ABB Tool Controller system in Unity robotics applications.
# Com.preliy.flange Package Analysis

## Overview

This document provides a comprehensive analysis of the `com.preliy.flange` Unity package architecture, focusing on joint angle control and the internal data flow for 6-axis robotic systems.

## Package Structure

The package is organized into several key directories:
- `Runtime/Scripts/` - Core functionality
  - `Controller/` - Robot control APIs
  - `Solver/` - Kinematic algorithms (forward/inverse)
  - `Kinematic/` - Joint and frame management
  - `Common/` - Utility classes
- `Editor/` - Unity Editor integration
- `Samples/` - Demo scenes and prefabs

## Core Architecture

### 1. Controller System

The `Controller` class serves as the central orchestrator for robot control:

```csharp
public class Controller : MonoBehaviour
{
    public MechanicalGroup MechanicalGroup { get; }
    public Solver Solver { get; }
    public IProperty<Configuration> Configuration { get; }
    public IProperty<int> Tool { get; }
    public IProperty<int> Frame { get; }
    public PoseObserver PoseObserver { get; }
}
```

**Location**: `Runtime/Scripts/Controller/Controller.cs`

### 2. Joint Angle Data Flow

#### Primary Data Path
```
Input Sources → MechanicalGroup._jointState → TransformJoint.Position → Unity Transform
     ↓                    ↓                         ↓
Controller.cs      JointTarget (12 values)    Visual Movement
```

#### Joint Angle Sources
1. **TransformJoint.Position.Value** (`TransformJoint.cs:24`)
   - Reactive property storing individual joint angles
   - Automatically updates Unity transforms when changed

2. **JointTarget** (`JointTarget.cs:10`)
   - Container for 12 joint values (6 robot + 6 external)
   - Indices 0-5: Robot joints (`RobJoint`)
   - Indices 6-11: External joints (`ExtJoint`)

3. **MechanicalGroup._jointState** (`MechanicalGroup.cs:49`)
   - Central state storage for all joint positions
   - Updated through various control methods

#### Control Flow Points
1. **MechanicalGroup.SetJoint()** (`MechanicalGroup.cs:125`)
   - Primary entry point for individual joint changes
   - Handles both direct control and inverse kinematics

2. **MechanicalGroup.SetJoints()** (`MechanicalGroup.cs:167`)
   - Updates all joints simultaneously
   - Distributes values to robot and external joints

3. **TransformJoint.SetValue()** (`TransformJoint.cs:46`)
   - Final transformation step
   - Uses `HomogeneousMatrix.Create()` for kinematic calculations

## Controller API Reference

### Core Properties
- `IsValid` - Controller configuration validation
- `Configuration` - Robot configuration (limits, workspace)
- `Tool` / `Frame` - Active tool and reference frame indices
- `MechanicalGroup` - Joint and mechanism management
- `Solver` - Forward/inverse kinematics engine
- `PoseObserver` - Real-time pose monitoring

### Joint Control Methods

#### Direct Joint Manipulation
```csharp
// Individual joint control
controller.MechanicalGroup.SetJoint(jointIndex, angle, notify: true);

// All joints simultaneously
controller.MechanicalGroup.SetJoints(jointTarget, notify: true);

// Current joint state
var joints = controller.MechanicalGroup.JointState;
```

#### Cartesian Control
```csharp
// Compute inverse kinematics
var solution = controller.Solver.ComputeInverse(target, toolIndex, frameIndex);

// Apply solution
if (solution.IsValid)
    controller.Solver.TryApplySolution(solution);
```

### Coordinate System Transformations (ControllerExtension.cs)

#### Frame Conversions
```csharp
// Convert between coordinate systems
controller.FrameToWorld(pose, frameIndex);
controller.WorldToFrame(pose, frameIndex);
controller.ConvertFrame(pose, fromFrame, toFrame);

// Tool Center Point access
controller.GetTcpWorld();
controller.GetTcpRelativeToRefFrame();
```

#### Tool Management
```csharp
// Tool offset operations
controller.AddToolOffset(matrix, toolIndex);
controller.RemoveToolOffset(matrix, toolIndex);
controller.GetToolOffset(toolIndex);
```

## Kinematic System

### Robot6RSphericalWrist (`Robot6RSphericalWirst.cs`)

The main inverse kinematics solver for 6-axis spherical wrist robots:

#### Key Methods
- `ComputeForward()` - Calculate end-effector pose from joint angles
- `ComputeInverse()` - Calculate joint angles from target pose
- `GetConfigurationIndex()` - Determine robot configuration

#### Algorithm Features
- Supports 8 different solution configurations
- Handles singularity detection and avoidance
- Validates joint limits and workspace constraints
- Uses homogeneous transformations for calculations

### Solver Class (`Solver.cs`)

High-level interface for kinematic calculations:

```csharp
// Forward kinematics
var pose = solver.ComputeForward(jointTarget, toolIndex);

// Inverse kinematics - single solution
var solution = solver.ComputeInverse(cartesianTarget, toolIndex, frameIndex);

// All possible solutions
var solutions = solver.GetAllSolutions(target, toolIndex, frameIndex, includeTurns: true);
```

## Real-time Monitoring

### PoseObserver (`PoseObserver.cs`)

Provides real-time pose tracking and event notifications:

#### Tracked Poses
- `Flange.Value` - Raw flange pose (without tool offset)
- `ToolCenterPointBase.Value` - TCP in base coordinates
- `ToolCenterPointWorld.Value` - TCP in world coordinates  
- `ToolCenterPointFrame.Value` - TCP in reference frame coordinates

#### Event System
```csharp
controller.PoseObserver.OnPoseChanged += () => {
    // Handle real-time pose updates
    var currentTCP = controller.PoseObserver.ToolCenterPointWorld.Value;
};
```

## Data Structures

### JointTarget
12-element container for all joint positions:
- Elements 0-5: Robot joints (RobJoint)
- Elements 6-11: External axes (ExtJoint)

### CartesianTarget
Cartesian position/orientation with configuration:
- `Pose` - 4x4 transformation matrix
- `Configuration` - Robot configuration parameters
- `ExtJoint` - External axis positions

### IKSolution
Inverse kinematics solution result:
- `JointTarget` - Computed joint angles
- `Configuration` - Solution configuration
- `IsValid` - Solution validity flag
- `Exception` - Error information if invalid

## Integration Points

### Joint Angle Origins
Joint angles can be sourced from:

1. **Manual UI Control** - Editor sliders modifying `TransformJoint.Position.Value`
2. **Inverse Kinematics** - Computed by `Robot6RSphericalWrist.ComputeInverse()`
3. **External Scripts** - Direct API calls to Controller methods
4. **Animation Systems** - Unity animation controllers
5. **External Data Sources** - Like the ABB data processing system shown in selection

### External Data Integration
The selected ABB data processing code shows integration with external robot systems:
- WebSocket/HTTP communication for real-time data
- Joint angle streaming (`ABB_Stream_Data.J_Orientation`)
- Cartesian position streaming (`ABB_Stream_Data.C_Position/C_Orientation`)

This external data can be integrated with the Flange package through:
```csharp
// Update robot joints from external source
var jointTarget = new JointTarget(externalJointAngles);
controller.MechanicalGroup.SetJoints(jointTarget, notify: true);
```

## Key Design Patterns

1. **Reactive Properties** - Properties notify subscribers when values change
2. **Command Pattern** - Joint movements are encapsulated as discrete operations
3. **Strategy Pattern** - Different robot types implement common interfaces
4. **Observer Pattern** - PoseObserver monitors and broadcasts state changes
5. **Extension Methods** - ControllerExtension provides additional functionality

## Performance Considerations

- Joint updates trigger kinematic recalculations
- PoseObserver provides cached pose calculations
- Matrix operations are optimized using Unity's native math libraries
- Inverse kinematics provides multiple solution strategies for optimization

## Conclusion

The com.preliy.flange package provides a comprehensive framework for robotic control in Unity, with clear separation of concerns between joint control, kinematics, coordinate transformations, and real-time monitoring. The architecture supports both direct joint manipulation and high-level Cartesian control, making it suitable for various robotic applications.
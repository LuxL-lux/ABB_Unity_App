# Usage Guide

This guide provides detailed instructions on how to set up and configure the different components of the Unity3D ABB Robot Control System.

## Prerequisites

Before you begin, ensure you have met the following requirements:

- **Unity Editor**: 2023.2.20f1 or later (Unity 6000.0.32f1 recommended).
- **Preliy Flange Package**: The visualization of the robot is based on the Preliy Flange package. Make sure you have it installed in your project.
- **ABB Robot Controller**: An IRC5 or OmniCore controller with RobotWare 6.0 or later.
- **Robot Web Services (RWS)**: RWS 1.0+ must be installed and enabled on the controller.

For detailed installation instructions, please refer to the [Installation & Setup Guide](installation-setup.md).

## Core Components Setup

### RobotManager

The `RobotManager` component is the central coordinator of the robot system. It is responsible for connecting to the robot, managing the robot's state, and propagating state updates to other components.

1. Create an empty GameObject in your scene and name it "RobotManager".
2. Add the `RobotManager` component to the GameObject.
3. In the Inspector, you will see the following properties:
   - **Connector Component**: This is a reference to the component that implements the `IRobotConnector` interface. In this case, it should be the `ABBRWSConnectionClient` component.
   - **Visualization Components**: This is a list of components that implement the `IRobotVisualization` interface. You can add the `ABBFlangeAdapter` here.

### Robot Connection

The `ABBRWSConnectionClient` component is the main entry point for connecting to the robot. To set it up, follow these steps:

1. Create an empty GameObject in your scene and name it "RobotConnection".
2. Add the `ABBRWSConnectionClient` component to the GameObject.
3. Configure the connection parameters in the Inspector:
   - **Robot IP**: The IP address of your ABB robot controller.
   - **Username**: The username for RWS authentication.
   - **Password**: The password for RWS authentication.
   - **Enable Motion Data**: Check this to enable real-time joint data polling.
   - **Motion Polling Interval Ms**: The interval in milliseconds at which to poll for motion data (e.g., 100).

4. Drag the "RobotConnection" GameObject to the `Connector Component` field of the `RobotManager` component.

### Safety Monitoring

The `RobotSafetyManager` component is responsible for coordinating all safety monitors. To set it up:

1. Create an empty GameObject and name it "SafetyManager".
2. Add the `RobotSafetyManager` component.
3. Create child GameObjects for each safety monitor you want to use (e.g., `CollisionDetectionMonitor`, `SingularityDetectionMonitor`).
4. Add the respective monitor components to the child GameObjects.
5. In the `RobotSafetyManager` Inspector, link the monitor GameObjects to the `Safety Monitor Components` list.

## Monitor Configuration

### Collision Detection Monitor

The `CollisionDetectionMonitor` uses Unity's physics system to detect collisions.

- **Collision Layers**: Define which layers the monitor should check for collisions.
- **Cooldown Time**: The minimum time in seconds between two collision events to avoid spam.

### Singularity Detection Monitor

This monitor detects kinematic singularities in the robot's arm.

- **Wrist Singularity Threshold**: The angle in degrees at which to trigger a wrist singularity warning.
- **Shoulder Singularity Threshold**: The distance in meters from the Y0 axis at which to trigger a shoulder singularity warning.
- **Elbow Singularity Threshold**: The normalized cross product threshold for detecting an elbow singularity.

### Joint Dynamics Monitor

This monitor checks for joint limit violations, including position, velocity, and acceleration.

- **Use Flange Limits**: If checked, the monitor will use the joint limits defined in the Flange package.
- **Limit Safety Factor**: A safety margin for the joint limits (e.g., 0.8 for 80% of the maximum limit).
- **Manual Limits**: If `Use Flange Limits` is unchecked, you can manually define the joint limits.

### Process Flow Monitor

This monitor validates the sequence of stations that a part goes through.

- **Monitor All Parts**: If checked, the monitor will track all parts in the scene.
- **Specific Parts**: If `Monitor All Parts` is unchecked, you can specify which parts to monitor.
- **Treat Skipped Stations As Warning**: If checked, skipping a station in the sequence will trigger a warning. Otherwise, it will be an informational event.
- **Treat Wrong Sequence As Critical**: If checked, a wrong sequence of stations will trigger a critical event. Otherwise, it will be a warning.

## Process Flow Setup

To set up the process flow monitoring, you need to configure the `Part` and `Station` components.

### Part Component

The `Part` component represents a workpiece in the manufacturing process.

1. Create a GameObject for your part.
2. Add the `Part` component to the GameObject.
3. In the Inspector, you can configure the following properties:
   - **Part Id**: A unique identifier for the part.
   - **Part Name**: A human-readable name for the part.
   - **Required Station Sequence**: An array of `Station` components that defines the sequence of stations the part must go through.
   - **Enforce Sequence**: If checked, the monitor will validate the station sequence.
   - **Allow Skip Stations**: If checked, the part can skip stations in the sequence.

### Station Component

The `Station` component represents a processing station in the manufacturing workflow.

1. Create a GameObject for your station.
2. Add the `Station` component to the GameObject.
3. Add a `Collider` component to the GameObject and set it as a trigger.
4. In the Inspector, you can configure the following properties:
   - **Station Name**: A human-readable name for the station.
   - **Station Index**: The index of the station in the sequence.

## Gripper and Tool Setup

The `ABBToolController` component manages the robot's gripper and other tools. It allows you to control the gripper using digital I/O signals, RAPID procedures, or both.

1. Add the `ABBToolController` component to your robot's GameObject.
2. In the Inspector, you can configure the following properties:
   - **IO Network**: The name of the I/O network on the robot controller (e.g., "Local").
   - **IO Device**: The name of the I/O device on the robot controller (e.g., "DRV_1").
   - **Gripper Open Signal**: The name of the digital output signal to open the gripper.
   - **Gripper Close Signal**: The name of the digital output signal to close the gripper.
   - **Rapid Task Name**: The name of the RAPID task on the robot controller (e.g., "T_ROB1").
   - **Gripper Open Procedure**: The name of the RAPID procedure to open the gripper.
   - **Gripper Close Procedure**: The name of the RAPID procedure to close the gripper.

3. In the `Tools` list, you can define the tools that are attached to the robot. For each tool, you need to specify:
   - **Name**: A unique name for the tool.
   - **Flange Tool Component**: A reference to the `Tool` component from the Flange package.
   - **Gripper Component**: A reference to the `Gripper` component from the Flange package.
   - **Control Type**: The control method to use for the tool (`DigitalIO`, `RapidProcedure`, or `Both`).
   - **Custom Signals/Procedures**: You can override the default signals and procedures for each tool.

## Example Configuration

```csharp
// Example of a tool definition in the ABBToolController
ToolDefinition myGripper = new ToolDefinition();
myGripper.name = "MyGripper";
myGripper.flangeToolComponent = myTool; // Assign your Flange Tool component
myGripper.gripperComponent = myGripperComponent; // Assign your Flange Gripper component
myGripper.controlType = ToolControlType.DigitalIO;
myGripper.customOpenSignal = "DO_MyGripperOpen";
myGripper.customCloseSignal = "DO_MyGripperClose";
```

## Visualization

### ABB Flange Adapter

The `ABBFlangeAdapter` component is used to visualize the robot's movements using the Preliy Flange package.

1. Create an empty GameObject in your scene and name it "FlangeAdapter".
2. Add the `ABBFlangeAdapter` component to the GameObject.
3. In the Inspector, you need to assign the `Controller` component from the Flange package to the `Controller Component` field.
4. Add the "FlangeAdapter" GameObject to the `Visualization Components` list of the `RobotManager` component.

### RAPID Target Generator

The `RapidTargetGenerator` component is a utility that generates RAPID `robtarget` and `jointtarget` data from the robot's current position.

1. Add the `RapidTargetGenerator` component to your robot's GameObject.
2. The component will automatically find the `Robot6RSphericalWrist` component from the Flange package.
3. You can then copy the generated `robtarget` and `jointtarget` data from the Inspector to your RAPID program.

## Troubleshooting

- **Connection Failed**: Verify the robot's IP address, username, and password. Ensure the RWS service is running on the controller and that there are no firewall restrictions.
- **No Motion Data**: Check that `Enable Motion Data` is enabled in the `ABBRWSConnectionClient` and that the robot program is running.
- **Safety Monitor Not Working**: Ensure the monitor GameObjects are correctly linked to the `RobotSafetyManager` and that the monitor components are active.

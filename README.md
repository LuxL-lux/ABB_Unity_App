# Unity3D ABB Robot Control System

This Unity3D framework provides comprehensive real-time control and safety monitoring for ABB industrial robots. It enables direct integration with ABB's Robot Web Services (RWS) to create digital twins, control physical robots, and validate robot programs in a simulated environment.

## Quickstart

To install the abbcontrol package via the Unity package manager, open the package manager via Window > Package Manager, click the plus sign and click install via git URL:

```

https://github.com/LuxL-lux/ABB_Unity_App.git#upm

```

## Key Features & Capabilities

- **Robot Communication**: Hybrid protocol (WebSocket + HTTP), robust authentication, and automatic recovery.
- **Mathematical Safety Monitoring**: Singularity detection, collision detection, and joint dynamics monitoring.
- **Smart Safety Event Logging**: Context-aware logging to JSON files or the console.
- **Advanced Visualization Integration**: Reflection-based integration with the Preliy Flange framework.
- **Process Flow Management**: Station-based sequence validation and part tracking.

For more details, please refer to the full documentation in the `docs` folder.

## Documentation

- **[Overview](docs/index.md)**
- **[Usage Guide](docs/usage.md)**
- **[API Reference](docs/api-reference.md)**

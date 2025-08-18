# ABB RWS Modular Architecture

This folder contains the modular components for ABB Robot Web Services integration.

## Architecture Overview

### Core Components

- **`IRWSApiClient.cs`** - Interface for RWS API client
- **`ABBRWSApiClient.cs`** - HTTP client implementation with authentication

### Specialized Monitors

- **`ABBRapidStatusMonitor.cs`** - RAPID program status monitoring
- **`ABBIOSignalMonitor.cs`** - I/O signal polling and management
- **`ABBJointLimitMonitor.cs`** - Joint limit checking and warnings

### Main Controllers

- **`ABBRobotWebServicesController.cs`** - Original monolithic controller (legacy)
- **`ABBRobotWebServicesControllerModular.cs`** - New modular controller

## Usage

### Modular Controller (Recommended)
Use `ABBRobotWebServicesControllerModular` for new projects. It provides the same functionality as the original controller but with better architecture:

- Cleaner separation of concerns
- Individual modules can be reused
- Easier testing and maintenance
- Better extensibility

### Individual Modules
You can also use the individual monitoring modules in custom implementations:

```csharp
// Standalone RAPID monitoring
var apiClient = new ABBRWSApiClient(connectionSettings);
var rapidMonitor = new ABBRapidStatusMonitor(apiClient, "T_ROB1", true);
rapidMonitor.OnStatusChanged += HandleStatusChange;
await rapidMonitor.UpdateStatusAsync();

// Standalone I/O monitoring
var ioMonitor = new ABBIOSignalMonitor(apiClient);
ioMonitor.OnGripperSignalReceived += HandleGripperSignal;
ioMonitor.AddSampleGripperSignals();
await ioMonitor.PollSignalsAsync();
```

## Migration Path

1. **Existing projects** can continue using `ABBRobotWebServicesController`
2. **New projects** should use `ABBRobotWebServicesControllerModular`
3. **Custom implementations** can use individual modules as needed

## Benefits

- **Maintainability**: Smaller, focused files
- **Testability**: Individual modules can be unit tested
- **Reusability**: Modules can be used independently
- **Extensibility**: Easy to add new monitoring capabilities
- **Performance**: Same performance as original controller

## Response Format

The modular architecture is designed to prioritize **JSON** responses over XML for better performance and easier parsing:

### API Client Configuration
- Requests JSON format using `Accept: application/json` header
- Adds `json=1` query parameter to force JSON responses from RWS API
- Falls back to XML parsing if JSON is not available

### Parsing Strategy
All monitoring modules use enhanced parsing that:
1. **First**: Attempts JSON parsing with multiple patterns
2. **Fallback**: Uses XML parsing for compatibility
3. **Graceful**: Handles mixed response formats

### Testing JSON Format
Use the `Test JSON Response Format` context menu option in the modular controller to verify that your ABB controller is returning JSON responses.
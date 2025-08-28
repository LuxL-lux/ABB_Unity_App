# Installation & Setup Guide

## System Requirements

### Minimum Hardware Requirements
- **CPU**: Intel i5-8400 or AMD Ryzen 5 2600 (6 cores, 3.0+ GHz)
- **RAM**: 8 GB DDR4
- **GPU**: DirectX 11 compatible (GTX 1050 / RX 560 or better recommended)
- **Storage**: 10 GB available space (SSD recommended)
- **Network**: Gigabit Ethernet adapter for robot communication

### Recommended Hardware
- **CPU**: Intel i7-10700K or AMD Ryzen 7 3700X (8+ cores, 3.5+ GHz)
- **RAM**: 16 GB DDR4 or higher
- **GPU**: GTX 1660 Ti / RTX 3060 or better for complex visualizations
- **Storage**: NVMe SSD for improved loading times

### Software Requirements

#### Development Environment
- **Unity Hub**: 3.4.2 or later
- **Unity Editor**: 2023.2.20f1 or later (Unity 6000.0.32f1 recommended)
- **Visual Studio**: 2022 Community or Professional (with Unity Tools)
- **Git**: Latest version for version control
- **.NET Framework**: 4.8 or .NET 6.0+

#### Optional Tools
- **Blender**: 3.6+ for 3D model modifications
- **ABB RobotStudio**: 2023.4 or later for RAPID programming
- **Wireshark**: For network protocol analysis
- **Postman**: For API testing

#### ABB Robot Controller Requirements
- **Controller Type**: IRC5 or OmniCore
- **RobotWare Version**: 6.0 or later
- **Required Add-ins**:
  - Robot Web Services (RWS) 1.0+
  - WebSocket support
  - Digest authentication enabled
- **Network Configuration**: Static IP recommended
- **User Permissions**: User with sufficient privileges for RWS access

## Installation Procedure

### Step 1: Unity Setup

1. **Download Unity Hub**:
   ```
   https://unity.com/download
   ```

2. **Install Unity Editor**:
   - Open Unity Hub
   - Go to "Installs" tab
   - Click "Install Editor"
   - Select Unity 6000.0.32f1 or later
   - Include modules:
     - Visual Studio Community 2022
     - Android Build Support (if mobile deployment needed)
     - WebGL Build Support (for web-based control panels)

3. **Verify Installation**:
   - Create new 3D project
   - Confirm no compilation errors
   - Check Unity console for warnings

### Step 2: Project Setup

1. **Clone Repository**:
   ```bash
   git clone [repository-url]
   cd ABB_Unity_App
   ```

2. **Open Project in Unity**:
   - Launch Unity Hub
   - Click "Add project from disk"
   - Navigate to `ABB_Unity_App` folder
   - Select folder and click "Add Project"

3. **Import Dependencies**:
   - Unity should automatically import packages
   - If prompted, click "Import" for any package updates
   - Wait for compilation to complete

4. **Verify Package Dependencies**:
   Open Package Manager (Window → Package Manager):
   - **Newtonsoft.Json**: 3.2.1+
   - **Unity.Collections**: Latest
   - **Unity.Mathematics**: Latest
   - **TextMeshPro**: Latest

### Step 3: Preliy Flange Integration

1. **Obtain Flange Package**:
   - Contact Preliy for Flange package access
   - Download `com.preliy.flange` package
   - Verify package integrity

2. **Install Flange Package**:
   ```
   Window → Package Manager → + → Add package from tarball/disk
   ```
   - Select `com.preliy.flange` package file
   - Wait for import completion
   - Verify no compilation errors

3. **License Configuration**:
   - Place Flange license file in `Assets/StreamingAssets/`
   - Verify license validity in Unity console
   - Contact Preliy support for licensing issues

### Step 4: ABB Robot Controller Setup

#### Network Configuration

1. **Controller Network Setup**:
   ```
   Controller → Configuration → Communication → Network
   ```
   - Set static IP address (e.g., 192.168.1.100)
   - Configure subnet mask (e.g., 255.255.255.0)
   - Set gateway if required
   - Enable TCP/IP communication

2. **RWS Service Configuration**:
   ```
   RobotStudio → Controller → Service → Robot Web Services
   ```
   - Enable Robot Web Services
   - Configure authentication method (Digest recommended)
   - Set port (default: 80 for HTTP, 443 for HTTPS)
   - Create user account with sufficient privileges

3. **User Account Setup**:
   ```
   Controller → Access Control → User Accounts
   ```
   - Create new user (e.g., "unity_client")
   - Assign roles: "Service", "Operator", "Programmer"
   - Set strong password
   - Enable account

#### Firewall Configuration

1. **Windows Firewall (Development Machine)**:
   ```powershell
   # Run as Administrator
   New-NetFirewallRule -DisplayName "Unity ABB Robot Communication" -Direction Outbound -Protocol TCP -LocalPort 80,443,8080 -Action Allow
   New-NetFirewallRule -DisplayName "Unity ABB Robot Communication" -Direction Inbound -Protocol TCP -LocalPort 80,443,8080 -Action Allow
   ```

2. **Router/Network Configuration**:
   - Ensure ports 80, 443 are open between development machine and robot
   - Configure port forwarding if accessing robot across network segments
   - Consider VPN for remote access scenarios

#### RAPID Program Setup

1. **Basic RAPID Module** (`MainModule.mod`):
   ```rapid
   MODULE MainModule
       ! Tool and work object definitions
       CONST tooldata tGripper := [TRUE,[[0,0,100],[1,0,0,0]],[1,[0,0,1],[1,0,0,0],0,0,0]];
       CONST wobjdata wobjWorkstation := [FALSE,TRUE,"",[[0,0,0],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
       
       ! Gripper signals
       VAR bool bGripperOpen := FALSE;
       
       ! Main procedure
       PROC main()
           ! Initialize gripper
           SetDO DO_GripperOpen, 0;
           bGripperOpen := FALSE;
           
           ! Main operation loop
           WHILE TRUE DO
               ! Pick and place operations
               PickAndPlace;
               
               ! Wait for next cycle
               WaitTime 1.0;
           ENDWHILE
       ENDPROC
       
       ! Pick and place routine
       PROC PickAndPlace()
           ! Home position
           MoveJ [[0,0,0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]], v1000, fine, tGripper;
           
           ! Pick position
           MoveL [[500,0,200],[1,0,0,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]], v200, fine, tGripper\WObj:=wobjWorkstation;
           
           ! Close gripper
           SetDO DO_GripperOpen, 0;
           bGripperOpen := FALSE;
           WaitTime 0.5;
           
           ! Place position  
           MoveL [[500,300,200],[1,0,0,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]], v200, fine, tGripper\WObj:=wobjWorkstation;
           
           ! Open gripper
           SetDO DO_GripperOpen, 1;
           bGripperOpen := TRUE;
           WaitTime 0.5;
       ENDPROC
   ENDMODULE
   ```

2. **I/O Configuration**:
   ```
   Controller → Configuration → I/O → Unit Configuration
   ```
   - Define DO_GripperOpen signal
   - Configure signal properties (voltage, type)
   - Map to physical I/O if real gripper connected

### Step 5: Unity Project Configuration

#### Scene Setup

1. **Open Demo Scene**:
   ```
   Assets/Demo/1. Demo/1. Demo.unity
   ```

2. **Configure Robot Model**:
   - Locate ABB robot model in scene hierarchy
   - Verify `Robot6RSphericalWrist` component is attached
   - Check DH parameters are correctly configured
   - Ensure all joint transforms are properly linked

3. **Setup Connection Client**:
   - Create empty GameObject named "RobotConnection"
   - Add `ABBRWSConnectionClient` component
   - Configure connection settings:
     ```
     Robot IP: [Your robot IP address]
     Username: unity_client
     Password: [Your password]
     Enable Motion Data: true
     Motion Polling Interval: 100ms
     ```

4. **Configure Safety Manager**:
   - Create empty GameObject named "SafetyManager" 
   - Add `RobotSafetyManager` component
   - Add safety monitor GameObjects:
     - `CollisionDetectionMonitor`
     - `SingularityDetectionMonitor`
     - `JointDynamicsMonitor`
     - `ProcessFlowMonitor`
   - Link monitors to Safety Manager component

#### Flange Integration Setup

1. **Controller Component Setup**:
   - Locate Flange `Controller` component in scene
   - Verify robot model is properly linked
   - Check license validation in console

2. **Adapter Configuration**:
   - Create empty GameObject named "FlangeAdapter"
   - Add `ABBFlangeAdapter` component  
   - Link Controller Component reference
   - Link to RWS Connection Client

3. **Verification**:
   - Enter Play Mode
   - Check console for connection success messages
   - Verify joint angles update in real-time
   - Confirm safety monitors are active

### Step 6: Network Testing

#### Connectivity Verification

1. **Ping Test**:
   ```bash
   ping [robot-ip-address]
   ```
   - Should show consistent response times <10ms
   - Packet loss should be 0%

2. **Port Accessibility**:
   ```bash
   telnet [robot-ip-address] 80
   ```
   - Should connect successfully
   - Type `GET /` and press Enter twice
   - Should receive HTTP response

3. **RWS Service Test**:
   ```bash
   curl -u username:password http://[robot-ip]/rw/system/version
   ```
   - Should return XML with controller information
   - Verify authentication is working

#### Unity Connection Test

1. **Start Unity Project**:
   - Open demo scene
   - Enter Play Mode
   - Click "Start Connection" in ABBRWSConnectionClient inspector

2. **Verify Connection Status**:
   - Check Console for "Connected to robot" message
   - Monitor RobotState updates in inspector
   - Verify joint angles are updating (if robot is moving)

3. **Safety Monitor Verification**:
   - Check SafetyManager shows all monitors as "Active"
   - Move robot manually (if safe) to trigger safety events
   - Verify events appear in Console and log files

## Configuration Validation

### Automated Tests

1. **Connection Test** (Assets/Scripts/Editor/Tests/ConnectionTest.cs):
   ```csharp
   [Test]
   public void TestRobotConnection()
   {
       var connector = Object.FindObjectOfType<ABBRWSConnectionClient>();
       Assert.IsNotNull(connector);
       
       connector.Connect();
       yield return new WaitForSeconds(5f);
       
       Assert.IsTrue(connector.IsConnected);
   }
   ```

2. **Safety Monitor Test**:
   ```csharp
   [Test]
   public void TestSafetyMonitors()
   {
       var safetyManager = Object.FindObjectOfType<RobotSafetyManager>();
       Assert.IsNotNull(safetyManager);
       
       var monitors = safetyManager.GetActiveMonitors();
       Assert.IsTrue(monitors.Count > 0);
   }
   ```

### Manual Verification Checklist

- [ ] Unity project loads without errors
- [ ] All package dependencies resolved
- [ ] Flange license validated (if using Flange)
- [ ] Robot controller accessible via network
- [ ] RWS authentication successful
- [ ] WebSocket connection established
- [ ] Motion data polling active
- [ ] Safety monitors initialized
- [ ] Joint angles updating in real-time
- [ ] Safety events generated and logged
- [ ] Visualization synchronization working

## Performance Optimization

### Unity Settings

1. **Player Settings**:
   ```
   Edit → Project Settings → Player
   ```
   - **Scripting Backend**: IL2CPP
   - **Api Compatibility Level**: .NET Standard 2.1
   - **Managed Stripping Level**: Minimal
   - **Target Architecture**: x86_64

2. **Quality Settings**:
   ```
   Edit → Project Settings → Quality
   ```
   - Use "High" quality for development
   - Disable unnecessary features:
     - Anti-aliasing (if performance critical)
     - Realtime reflection probes
     - Soft particles (if not needed)

3. **Physics Settings**:
   ```
   Edit → Project Settings → Physics
   ```
   - **Fixed Timestep**: 0.02 (50Hz)
   - **Maximum Allowed Timestep**: 0.1
   - **Solver Iteration Count**: 6
   - **Auto Simulation**: Enabled

### Network Optimization

1. **Polling Intervals**:
   - Start with 100ms motion polling
   - Reduce to 50ms only if needed
   - Monitor CPU usage and network bandwidth

2. **Event Filtering**:
   - Enable safety event cooldown periods
   - Set appropriate minimum log levels
   - Use conditional logging for production

3. **Connection Pooling**:
   - Reuse HTTP connections where possible
   - Implement connection keep-alive
   - Monitor connection count

## Common Installation Issues

### Unity Issues

**Issue**: Package compilation errors
**Solution**: 
- Delete `Library` folder and restart Unity
- Verify .NET version compatibility
- Check Unity version compatibility

**Issue**: Missing Flange package references
**Solution**:
- Verify package installation in Package Manager
- Check license file location and validity
- Contact Preliy support for licensing issues

### Network Issues

**Issue**: Cannot connect to robot
**Solution**:
- Verify IP address and network connectivity
- Check firewall settings on both ends
- Confirm RWS service is running on robot

**Issue**: Authentication failures  
**Solution**:
- Verify username/password combination
- Check user account permissions in controller
- Ensure digest authentication is enabled

**Issue**: High network latency
**Solution**:
- Use wired ethernet connection instead of WiFi
- Check network infrastructure for bottlenecks
- Consider network QoS configuration

### Performance Issues

**Issue**: Low frame rate in Unity
**Solution**:
- Reduce motion polling frequency
- Disable unnecessary safety monitors
- Optimize Unity quality settings

**Issue**: Safety event spam
**Solution**:
- Increase cooldown periods
- Adjust safety thresholds
- Implement event aggregation

## Next Steps

After successful installation:

1. **Review Configuration Guide**: [configuration.md](configuration.md)
2. **Study API Documentation**: [api-reference.md](api-reference.md)  
3. **Run Test Scenarios**: [testing-validation.md](testing-validation.md)
4. **Explore Examples**: Assets/Demo folder
5. **Setup Safety Certification**: [safety-compliance.md](safety-compliance.md)

For troubleshooting assistance, see [troubleshooting.md](troubleshooting.md).
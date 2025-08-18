# ABB Robot Web Services (RWS) API Analysis

## Overview
Analysis of ABB RWS API documentation for optimal data retrieval methods focusing on:
1. Joint angles/positions for robot updates
2. RAPID program context/pointer/task execution status  
3. I/O signal changes
4. Event-driven updates (subscriptions/WebSocket) vs polling

## 🎯 Key Findings

### 1. **Subscription Service** - Event-Driven Approach ⭐
**Base Endpoint**: `/subscription`

#### Subscription Mechanism:
- **POST** `/subscription` - Create new subscription group
- **PUT** `/subscription/{group-id}` - Modify existing subscription  
- **DELETE** `/subscription/{group-id}` - Remove subscription group
- **DELETE** `/subscription/{group-id}/{resource-uri}` - Remove specific resource

#### Subscription Limits:
- Max **1000 resources** per group
- Max **10 groups** per client
- Max **64 high-priority** resources

#### Priority Levels:
- **0**: Low priority (all resources)
- **1**: Medium priority (all resources) 
- **2**: High priority (I/O signals and RAPID variables only)

#### WebSocket Integration:
After subscription, updates are received via **WebSocket** connection (e.g., `ws://localhost/poll/1`)

---

### 2. **RAPID Program Status** 📋

#### Key Endpoints:
- `/rw/rapid/execution` - Global execution state
- `/rw/rapid/tasks/{taskname}` - Task-specific information
- `/rw/rapid/tasks/{task}/modules` - Module information
- `/rw/rapid/tasks/{task}/modules/{module}/routine` - Routine details

#### Subscription Resources:
- `/rw/rapid/execution;ctrlexecstate` - Controller execution state
- `/rw/rapid/tasks/{taskname};excstate` - Task execution state  
- `/rw/rapid/execution;execstate` - Global execution state

#### Data Available:
- Execution state (running, stopped, etc.)
- Program pointer (current routine/module)
- Task status and type
- Hold-to-run status
- Execution cycle information

---

### 3. **I/O Signal Monitoring** 🔌

#### Key Endpoints:
- `/rw/iosystem/signals/{network}/{device}/{signal}` - Individual signal
- `/rw/iosystem/signals` - All signals
- `/rw/iosystem/devices/{network}/{device}` - Device status

#### Subscription Resources:
- `/rw/iosystem/signals/{network}/{device}/{signal};state` - Individual signal state
- `/rw/iosystem/devices/{network}/{device};state` - Device state

#### Signal Data:
- Logical/physical values
- Signal quality
- Timestamp information
- Access levels and safe levels
- Real-time state changes via WebSocket

---

### 4. **Joint Angles/Positions** 🦾

#### Current Endpoints (Limited):
- `/rw/motionsystem/position-joint` (POST) - Set joint positions
- `/rw/motionsystem/mechunits/{mechunit}/jointtarget` (GET) - Get joint target
- `/rw/motionsystem/jog` (POST) - Jog individual axes

#### ⚠️ **Challenge**: 
No explicit real-time joint position subscription found in documentation. May require:
- **Polling approach** for joint data
- **Alternative endpoints** not documented in public specs
- **Custom RWS extensions** or proprietary methods

---

## 🔧 Implementation Strategy

### Phase 1: Event-Driven Implementation
1. **Subscription Service Setup**
   - Create subscription groups for RAPID and I/O signals
   - Establish WebSocket connection for real-time updates
   - Implement JSON parsing for subscription responses

2. **RAPID Status Subscription**
   - Subscribe to: `/rw/rapid/execution;ctrlexecstate`
   - Subscribe to: `/rw/rapid/tasks/{taskname};excstate`
   - Real-time program pointer and execution state

3. **I/O Signal Subscription** 
   - Subscribe to: `/rw/iosystem/signals/{network}/{device}/{signal};state`
   - Real-time signal state changes
   - Gripper and tool control signals

### Phase 2: Joint Data Strategy
Since real-time joint subscriptions aren't documented:

**Option A**: Enhanced Polling
- Use existing joint position endpoints
- Optimize polling frequency
- Implement smart caching

**Option B**: Research Alternative Methods  
- Investigate undocumented endpoints
- Check for RAPID variable subscriptions for joint data
- Explore robot-specific extensions

**Option C**: Hybrid Approach
- Use subscriptions for RAPID/I/O (event-driven)
- Use optimized polling for joint data
- Synchronize data streams

### Phase 3: JSON Format Optimization
- Request: `application/hal+json;v=2.0`
- Fallback: `application/hal+json`
- Parameter: `json=1` (where supported)
- Enhanced JSON parsing for all responses

---

## 🚀 Next Steps

1. **Create WebSocket subscription client**
2. **Implement subscription management**  
3. **Test RAPID and I/O subscriptions**
4. **Research joint position alternatives**
5. **Build unified event-driven architecture**

---

## 📚 Reference URLs
- Main Documentation: https://developercenter.robotstudio.com/api/RWS
- Subscription Service: https://developercenter.robotstudio.com/api/RWS/Swagger_Doc/Subscription_Service.yaml
- RAPID Service: https://developercenter.robotstudio.com/api/RWS/Swagger_Doc/RAPID_Service.yaml
- I/O Service: https://developercenter.robotstudio.com/api/RWS/Swagger_Doc/IO_Service.yaml
- Motion System: https://developercenter.robotstudio.com/api/RWS/Swagger_Doc/Motion_System.yaml
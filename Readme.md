# Quin Autobot

Quin Autobot is an autonomous agent integrated directly into the Unity Editor, designed to assist with project development, scene manipulation, and codebase management through the Model Context Protocol (MCP).

<img width="1917" height="1108" alt="image" src="https://github.com/user-attachments/assets/52bc65bb-a810-4a7c-9ae7-ad535884e00a" />


## Core Architecture and Techniques

### Model Context Protocol (MCP) Integration
The core of Quin Autobot relies on a robust implementation of the MCP. It acts as a bridge between external language models and the Unity environment. The backend manages stateful agentic loops, executing tool calls natively within the editor context to perform dynamic tasks.

### Dynamic Payload Serialization
Communication with external AI providers involves handling complex, nested JSON structures. The system utilizes dynamic JObject payloads to construct, validate, and parse requests and responses (via LMRequestModels), ensuring schema compliance for functions like tool execution and context reporting.

### Telemetry and Context Awareness
The TelemetryWatcher operates as a low-level editor service, passively monitoring user actions, scene changes, compile errors, and console outputs. This subsystem feeds continuous context into the agent's prompt window, allowing the model to understand the exact state of the project dynamically.

### Editor UI Toolkit
The interface is constructed using Unity's UI Toolkit (AgentHubWindow.uxml), providing an optimized, non-blocking asynchronous UI within the editor. It manages API key configurations, chat history rendering, and real-time status indication of the agentic loop execution.

### Agentic Loop and Execution State
The agent operates on an autonomous evaluation loop. It ingests the current telemetry, determines the necessary tool calls, executes them via the MCP routing layer, and validates the output before proceeding or awaiting further instructions.

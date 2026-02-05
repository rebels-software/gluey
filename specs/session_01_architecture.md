# Session 01: GlueyHub Architecture Discussion

Date: 2025-10-30

## Topic: Runtime Engine Architecture for .gflow DSL

### Context

Discussion about the best programming language and architecture for implementing the GlueyHub workflow engine that executes `.gflow` files.

---

## Language Selection: Go vs .NET vs Rust vs Others

### Performance Tier Rankings

**Tier 1 (Optimal):**
- ✅ **Go** - 15-25MB containers, 10-50ms startup, 50-60k msg/sec
- ✅ **Rust** - 8-15MB containers, 5-20ms startup, 60-65k msg/sec

**Tier 2 (Excellent):**
- ✅ **.NET 8/9 Native AOT** - 28-35MB containers, 60-100ms startup, 50-55k msg/sec

**Tier 3 (Acceptable):**
- ⚠️ **Java 21 GraalVM** - 45-65MB containers, 100-200ms startup, 45-52k msg/sec

**Tier 4 (Avoid for Containers):**
- ❌ **.NET Traditional JIT** - 180-250MB, 800-1500ms startup
- ❌ **Java Traditional JVM** - 250-350MB, 2-10s startup
- ❌ **Node.js** - 150-200MB, 500-2000ms startup

### Performance Comparison Matrix

| Metric | .NET Native AOT | Go | Traditional .NET | Node.js |
|--------|----------------|-----|------------------|---------|
| **Container Size** | 28-35MB | 15-25MB | 180-250MB | 150-200MB |
| **Startup Time** | 60-100ms | 10-50ms | 800-1500ms | 500-2000ms |
| **Memory (idle)** | 40-80MB | 25-50MB | 120-200MB | 80-150MB |
| **Throughput** | 50-55k msg/s | 55-60k msg/s | 45-52k msg/s | 20-30k msg/s |

### Decision Criteria

**Choose Go if:**
- ✅ Greenfield project with no .NET investment
- ✅ Container size/startup time are critical (2x better than .NET)
- ✅ Team prefers simplicity over expressiveness
- ✅ Best balance of performance, development velocity, and ecosystem

**Choose .NET 8/9 Native AOT if:**
- ✅ Team has C# expertise (huge productivity boost)
- ✅ Complex data transformations (LINQ is powerful)
- ✅ SQL Server integration required (native first-class support)
- ✅ Excellent debugging/IDE support valued

**Choose Rust if:**
- ✅ Need absolute maximum performance (10-20% better)
- ✅ Resource-constrained edge deployments
- ✅ Team has Rust expertise

### Recommendation

**Primary: Go** - Best overall balance for containerized IoT message router
**Alternative: .NET 8/9 AOT** - Excellent if team has C# skills

---

## Architecture Overview: Plugin-Based Workflow Engine

### Key Concept

You write the core engine **once** in Go (or .NET), and it dynamically loads and executes workflows defined in `.gflow` files.

**What you write in Go (once):**
1. Core engine: Workflow executor, message routing, state management
2. Plugin system: Interface definitions and plugin loader
3. Built-in plugins: Common inputs (TCP, UDP, MQTT, HTTP, Kafka), transforms, outputs
4. DSL parser: Reads `.gflow` files and instantiates plugins

**What you DON'T write:**
- ❌ Custom TCP server code per customer
- ❌ Custom MQTT client per customer
- ❌ Custom transformation logic per workflow

**What customers/integrators do:**
- ✅ Write `.gflow` files describing their workflows
- ✅ Deploy the same runtime engine with different config files
- ✅ Optionally write custom plugins if built-ins don't suffice

---

## High-Level System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Gflow Runtime Engine                        │
│                      (Written in Go/C#)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────┐  ┌──────────────┐  ┌───────────────┐      │
│  │ .gflow Parser  │─▶│  Workflow    │─▶│   Workflow    │      │
│  │   (YAML/DSL)   │  │  Validator   │  │   Executor    │      │
│  └────────────────┘  └──────────────┘  └───────────────┘      │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              Plugin Registry & Manager                   │   │
│  │  - Discovers and loads plugins at runtime                │   │
│  │  - Manages plugin lifecycle                              │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │    Input     │  │  Transform   │  │   Output     │         │
│  │   Plugins    │  │   Plugins    │  │   Plugins    │         │
│  ├──────────────┤  ├──────────────┤  ├──────────────┤         │
│  │ • TCP        │  │ • JSON       │  │ • HTTP       │         │
│  │ • UDP        │  │ • Protobuf   │  │ • Kafka      │         │
│  │ • MQTT       │  │ • Mapper     │  │ • Database   │         │
│  │ • HTTP       │  │ • Filter     │  │ • MQTT       │         │
│  │ • Kafka      │  │ • Script     │  │ • File       │         │
│  └──────────────┘  └──────────────┘  └──────────────┘         │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │            Workflow Instance Manager                     │   │
│  │  - Manages multiple concurrent workflow instances        │   │
│  │  - Routes messages to correct workflow                   │   │
│  │  - Handles state, retry, audit                           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

         ▲                    ▲                    ▲
         │                    │                    │
    customer-a.gflow     customer-b.gflow     customer-c.gflow
```

---

## Plugin System Architecture

### Plugin Interface (Go)

```go
// Plugin base interface
type Plugin interface {
    Name() string
    Type() PluginType // Input, Transform, Output
    Initialize(config map[string]interface{}) error
    Start(ctx context.Context) error
    Stop() error
}

// Input plugin interface
type InputPlugin interface {
    Plugin
    Output() <-chan Message  // Channel to emit messages
}

// Transform plugin interface
type TransformPlugin interface {
    Plugin
    Process(msg Message) (Message, error)
}

// Output plugin interface
type OutputPlugin interface {
    Plugin
    Send(msg Message) error
}
```

### Example: TCP Input Plugin (Write Once, Use for All Customers)

```go
package plugins

type TCPInputPlugin struct {
    config     TCPConfig
    listener   net.Listener
    outputChan chan Message
}

type TCPConfig struct {
    Host string `yaml:"host"`
    Port int    `yaml:"port"`
}

func (p *TCPInputPlugin) Initialize(config map[string]interface{}) error {
    // Parse config from .gflow file
    p.config = parseConfig(config)
    p.outputChan = make(chan Message, 1000)
    return nil
}

func (p *TCPInputPlugin) Start(ctx context.Context) error {
    listener, err := net.Listen("tcp", fmt.Sprintf("%s:%d", p.config.Host, p.config.Port))
    if err != nil {
        return err
    }
    p.listener = listener

    go p.acceptConnections(ctx)
    return nil
}

func (p *TCPInputPlugin) acceptConnections(ctx context.Context) {
    for {
        conn, err := p.listener.Accept()
        if err != nil {
            continue
        }

        // Handle each connection in a goroutine
        go p.handleConnection(ctx, conn)
    }
}

func (p *TCPInputPlugin) handleConnection(ctx context.Context, conn net.Conn) {
    defer conn.Close()

    scanner := bufio.NewScanner(conn)
    for scanner.Scan() {
        // Read bytes and emit to output channel
        data := scanner.Bytes()

        select {
        case p.outputChan <- Message{Payload: data, Metadata: map[string]string{"source": "tcp"}}:
        case <-ctx.Done():
            return
        }
    }
}

func (p *TCPInputPlugin) Output() <-chan Message {
    return p.outputChan
}
```

### Example: MQTT Input Plugin (Write Once, Works for All)

```go
package plugins

import mqtt "github.com/eclipse/paho.mqtt.golang"

type MQTTInputPlugin struct {
    config     MQTTConfig
    client     mqtt.Client
    outputChan chan Message
}

type MQTTConfig struct {
    Broker   string   `yaml:"broker"`
    Topics   []string `yaml:"topics"`
    ClientID string   `yaml:"client_id"`
}

func (p *MQTTInputPlugin) Initialize(config map[string]interface{}) error {
    p.config = parseConfig(config)
    p.outputChan = make(chan Message, 1000)

    opts := mqtt.NewClientOptions()
    opts.AddBroker(p.config.Broker)
    opts.SetClientID(p.config.ClientID)

    p.client = mqtt.NewClient(opts)
    return nil
}

func (p *MQTTInputPlugin) Start(ctx context.Context) error {
    if token := p.client.Connect(); token.Wait() && token.Error() != nil {
        return token.Error()
    }

    // Subscribe to topics
    for _, topic := range p.config.Topics {
        p.client.Subscribe(topic, 0, p.messageHandler)
    }

    return nil
}

func (p *MQTTInputPlugin) messageHandler(client mqtt.Client, msg mqtt.Message) {
    p.outputChan <- Message{
        Payload: msg.Payload(),
        Metadata: map[string]string{
            "topic": msg.Topic(),
            "source": "mqtt",
        },
    }
}

func (p *MQTTInputPlugin) Output() <-chan Message {
    return p.outputChan
}
```

---

## Example Customer Workflows

### Customer A: TCP to Webhook

```yaml
workflow:
  id: customer-a-tcp-to-webhook
  name: "Customer A - TCP to Webhook"

input:
  type: tcp
  config:
    host: "0.0.0.0"
    port: 1234

transform:
  - type: byte-array-decoder
    config:
      encoding: "utf-8"

  - type: json-parser
    config:
      strict: false

output:
  type: http
  config:
    url: "https://customera-webhook.example.com/events"
    method: POST
    headers:
      Content-Type: application/json
    retry:
      max_attempts: 3
      backoff: exponential
```

### Customer B: TCP to Webhook (Different Port/Config)

```yaml
workflow:
  id: customer-b-tcp-to-webhook
  name: "Customer B - TCP to Webhook"

input:
  type: tcp
  config:
    host: "0.0.0.0"
    port: 5678

transform:
  - type: byte-array-decoder
    config:
      encoding: "utf-8"

  - type: data-mapper
    config:
      mapping:
        device_id: "$.deviceId"
        temperature: "$.temp"

output:
  type: http
  config:
    url: "https://customerb-webhook.example.com/data"
    method: POST
```

### Customer C: MQTT to Kafka

```yaml
workflow:
  id: customer-c-mqtt-to-kafka
  name: "Customer C - MQTT to Kafka"

input:
  type: mqtt
  config:
    broker: "tcp://mqtt.customerc.com:1883"
    topics:
      - "sensors/+/temperature"
      - "sensors/+/humidity"
    client_id: "glueyhub-customer-c"

transform:
  - type: json-parser

  - type: enrichment
    config:
      lookup:
        type: redis
        key_pattern: "device:{$.device_id}"

output:
  type: kafka
  config:
    brokers:
      - "kafka1.customerc.com:9092"
      - "kafka2.customerc.com:9092"
    topic: "iot-events"
    compression: lz4
```

---

## Core Engine Architecture (Go)

```go
package main

type WorkflowEngine struct {
    plugins   map[string]Plugin
    workflows map[string]*WorkflowInstance
    registry  *PluginRegistry
}

type WorkflowInstance struct {
    ID        string
    Config    WorkflowConfig
    Input     InputPlugin
    Transform []TransformPlugin
    Output    OutputPlugin
    Status    WorkflowStatus
}

func (e *WorkflowEngine) LoadWorkflow(filepath string) error {
    // 1. Parse .gflow file
    config, err := parseGflowFile(filepath)
    if err != nil {
        return err
    }

    // 2. Instantiate plugins based on config
    inputPlugin := e.registry.CreateInputPlugin(config.Input.Type)
    inputPlugin.Initialize(config.Input.Config)

    transformPlugins := []TransformPlugin{}
    for _, t := range config.Transform {
        plugin := e.registry.CreateTransformPlugin(t.Type)
        plugin.Initialize(t.Config)
        transformPlugins = append(transformPlugins, plugin)
    }

    outputPlugin := e.registry.CreateOutputPlugin(config.Output.Type)
    outputPlugin.Initialize(config.Output.Config)

    // 3. Create workflow instance
    workflow := &WorkflowInstance{
        ID:        config.Workflow.ID,
        Config:    config,
        Input:     inputPlugin,
        Transform: transformPlugins,
        Output:    outputPlugin,
    }

    e.workflows[workflow.ID] = workflow
    return nil
}

func (e *WorkflowEngine) StartWorkflow(id string) error {
    workflow := e.workflows[id]
    ctx := context.Background()

    // Start input plugin
    if err := workflow.Input.Start(ctx); err != nil {
        return err
    }

    // Start message processing pipeline
    go e.processMessages(ctx, workflow)

    return nil
}

func (e *WorkflowEngine) processMessages(ctx context.Context, workflow *WorkflowInstance) {
    // Get messages from input channel
    for msg := range workflow.Input.Output() {
        // Apply transformations in sequence
        transformed := msg
        for _, transform := range workflow.Transform {
            var err error
            transformed, err = transform.Process(transformed)
            if err != nil {
                // Handle error (retry, dead letter queue, etc.)
                continue
            }
        }

        // Send to output
        if err := workflow.Output.Send(transformed); err != nil {
            // Handle error (retry, audit, etc.)
        }
    }
}
```

### Plugin Registration System

```go
package plugins

type PluginRegistry struct {
    inputPlugins     map[string]func() InputPlugin
    transformPlugins map[string]func() TransformPlugin
    outputPlugins    map[string]func() OutputPlugin
}

func NewPluginRegistry() *PluginRegistry {
    r := &PluginRegistry{
        inputPlugins:     make(map[string]func() InputPlugin),
        transformPlugins: make(map[string]func() TransformPlugin),
        outputPlugins:    make(map[string]func() OutputPlugin),
    }

    // Register built-in plugins
    r.RegisterInput("tcp", func() InputPlugin { return &TCPInputPlugin{} })
    r.RegisterInput("udp", func() InputPlugin { return &UDPInputPlugin{} })
    r.RegisterInput("mqtt", func() InputPlugin { return &MQTTInputPlugin{} })
    r.RegisterInput("http", func() InputPlugin { return &HTTPInputPlugin{} })
    r.RegisterInput("kafka", func() InputPlugin { return &KafkaInputPlugin{} })

    r.RegisterTransform("json-parser", func() TransformPlugin { return &JSONParserPlugin{} })
    r.RegisterTransform("data-mapper", func() TransformPlugin { return &DataMapperPlugin{} })

    r.RegisterOutput("http", func() OutputPlugin { return &HTTPOutputPlugin{} })
    r.RegisterOutput("kafka", func() OutputPlugin { return &KafkaOutputPlugin{} })

    return r
}
```

---

## Deployment Strategies

### Single-Tenant Deployment (One workflow per container)

```
┌────────────────────────────────────────┐
│  customera.gluey.com:1234              │
│  ┌──────────────────────────────┐     │
│  │  Gflow Runtime Engine        │     │
│  │  - Loads customer-a.gflow    │     │
│  │  - TCP Plugin on port 1234   │     │
│  └──────────────────────────────┘     │
└────────────────────────────────────────┘

┌────────────────────────────────────────┐
│  customerb.gluey.com:5678              │
│  ┌──────────────────────────────┐     │
│  │  Gflow Runtime Engine        │     │
│  │  - Loads customer-b.gflow    │     │
│  │  - TCP Plugin on port 5678   │     │
│  └──────────────────────────────┘     │
└────────────────────────────────────────┘
```

**Kubernetes Deployment:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: customer-a-workflow
spec:
  replicas: 2
  template:
    spec:
      containers:
      - name: gflow-engine
        image: glueyhub/engine:latest  # Same image for ALL customers
        args:
          - --workflow=/config/customer-a.gflow
        volumeMounts:
          - name: workflow-config
            mountPath: /config
        ports:
          - containerPort: 1234
      volumes:
        - name: workflow-config
          configMap:
            name: customer-a-workflow
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: customer-a-workflow
data:
  customer-a.gflow: |
    # Workflow definition here
```

### Multi-Tenant Deployment (Multiple workflows in one engine)

```
┌─────────────────────────────────────────────────────┐
│  Gflow Runtime Engine (Multi-Tenant)                │
│                                                      │
│  ┌──────────────────┐  ┌──────────────────┐        │
│  │  Workflow A      │  │  Workflow B      │        │
│  │  TCP:1234        │  │  TCP:5678        │        │
│  └──────────────────┘  └──────────────────┘        │
│                                                      │
│  ┌──────────────────┐  ┌──────────────────┐        │
│  │  Workflow C      │  │  Workflow D      │        │
│  │  MQTT broker     │  │  HTTP:8080       │        │
│  └──────────────────┘  └──────────────────┘        │
└─────────────────────────────────────────────────────┘
```

**Command:**
```bash
# Load multiple workflows
./gflow-engine \
  --workflow=/config/customer-a.gflow \
  --workflow=/config/customer-b.gflow \
  --workflow=/config/customer-c.gflow
```

---

## Key Benefits of This Architecture

1. **Write Once, Use Everywhere**: TCP plugin works for Customer A, B, C, etc.
2. **No Code Per Customer**: Just write `.gflow` config files
3. **Easy MQTT Support**: Include MQTT plugin, customers use it via config
4. **Plugin Extensibility**: Customers can write custom plugins if needed
5. **Multi-Tenancy**: Run multiple workflows in single engine instance
6. **Isolated Deployment**: Or run one workflow per container for isolation

---

---

## .gflow File Format: Custom DSL vs YAML

### Decision: Use Custom DSL (from gluey_dsl_docs.md)

After analysis, the **custom pipeline-style DSL is superior to plain YAML** for this use case.

### Custom DSL Syntax (Recommended)

```gflow
flow mqtt-input v1.0 {
  from mqtt("mqtt://broker.local:1883") {
    topics: ["sensors/+/temperature", "devices/+/status"]
    qos: 1
    client_id: "gluey-worker-001"
  }

  | json.parse(payload)
  | kafka("mqtt-data")
}
```

### YAML Equivalent (Not Recommended)

```yaml
workflow:
  id: mqtt-input
  version: v1.0

input:
  type: mqtt
  config:
    broker: "mqtt://broker.local:1883"
    topics:
      - "sensors/+/temperature"
      - "devices/+/status"
    qos: 1
    client_id: "gluey-worker-001"

transform:
  - type: json-parser
    config:
      field: payload

output:
  type: kafka
  config:
    topic: "mqtt-data"
```

### Why Custom DSL is Better

1. **Pipeline Visual Flow**: The `|` operator makes data flow obvious and natural
2. **More Concise**: 40-50% less boilerplate than YAML
3. **Better for IoT Use Case**: Reads like a data pipeline (which it is!)
4. **Professional**: Similar to Apache Beam, dbt, n8n syntax patterns
5. **Easier Routing**: Elegant routing and branching syntax:
   ```gflow
   | route {
       high_temp: temperature > 35
       normal_temp: temperature <= 35
   }

   high_temp -> kafka("alerts")
   normal_temp -> influxdb("metrics")
   ```
6. **Product Differentiation**: Unique, purpose-built syntax sets GlueyHub apart

### Why NOT Plain YAML

1. **Too Verbose**: Nested structures become hard to read
2. **No Pipeline Clarity**: Harder to visualize message flow at a glance
3. **Less Expressive**: Can't elegantly express routing/branching
4. **Generic**: Every config tool uses YAML - doesn't differentiate the product
5. **Poor Developer Experience**: More typing, more indentation, less intuitive

### Implementation Strategy

**Hybrid Approach (Recommended):**

Use the custom DSL syntax as the primary interface, but internally parse to a structured format:

1. **Phase 1 - MVP**:
   - Parse basic `from → transform → output` pipelines
   - Support core input types (TCP, MQTT, HTTP, Kafka)
   - Basic transformations (JSON parse, filter, map)

2. **Phase 2 - Advanced Features**:
   - Add routing and conditionals
   - Error handling and retry logic
   - Schema definitions

3. **Phase 3 - Extensions**:
   - Complex binary decoding
   - State management
   - Custom plugin support

**Optional**: Support both `.gflow` (custom DSL) and `.yaml` (YAML format) for users who prefer YAML, but promote the DSL as the primary interface.

### Parser Architecture

```go
package parser

// Parsed .gflow file structure
type GflowFile struct {
    Flows   []Flow
    Schemas []Schema
}

type Flow struct {
    Name       string
    Version    string
    Input      InputNode
    Pipeline   []PipelineNode
    Routes     []RouteNode
}

type InputNode struct {
    Type   string // "mqtt", "tcp", "http", "kafka"
    URL    string // Connection string
    Config map[string]interface{}
}

type PipelineNode struct {
    Type   string // "json.parse", "transform", "filter"
    Config map[string]interface{}
}

type RouteNode struct {
    Name      string
    Condition string // Expression to evaluate
    Output    OutputNode
}

type OutputNode struct {
    Type   string
    Config map[string]interface{}
}
```

### Parser Implementation Options

**Option 1: Participle (Declarative Parser Generator)**
- Library: `github.com/alecthomas/participle/v2`
- Pros: Clean, declarative, maintainable
- Cons: Learning curve for advanced features

**Option 2: Hand-Rolled Lexer/Parser**
- Using Go's `text/scanner` package
- Pros: Full control, best error messages
- Cons: More code to maintain

**Option 3: DSL-to-YAML Transpiler**
- Users write `.gflow`, engine processes YAML internally
- Pros: Flexible, supports both formats
- Cons: Additional layer of complexity

### Example DSL Features from gluey_dsl_docs.md

**Basic Pipeline:**
```gflow
flow example v1.0 {
  from tcp("0.0.0.0:9000")
  | decode(length_prefix: u16be)
  | json.parse(payload)
  | http.post("https://webhook.example.com")
}
```

**Routing and Branching:**
```gflow
flow temperature-routing v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/+/temp"]
  }

  | json.parse(payload)
  | route {
      critical: temp > 50
      warning: temp > 35
      normal: temp <= 35
  }

  critical -> kafka("alerts") {
    priority: "high"
  }

  warning -> kafka("alerts") {
    priority: "medium"
  }

  normal -> influxdb("metrics")
}
```

**Schema Definition and Validation:**
```gflow
schema SensorData v1.0 {
  device_id: string(required: true)
  temperature: float(min: -40, max: 125)
  timestamp: timestamp(format: "iso8601")
}

flow validated-input v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/+/data"]
  }

  | json.parse(payload)
  | validate(schema: SensorData)
  | kafka("validated-data")
}
```

### Recommendation Summary

**Use the custom `.gflow` DSL syntax** for these reasons:

1. ✅ **Better Developer Experience**: Pipeline syntax is intuitive for IoT workflows
2. ✅ **Product Differentiation**: Unique, professional syntax sets GlueyHub apart
3. ✅ **Expressiveness**: Routing, conditionals, and transformations are elegant
4. ✅ **Readability**: 40-50% less boilerplate than YAML
5. ✅ **Maintainability**: Clear data flow makes debugging easier
6. ✅ **Scalability**: Can add advanced features (schemas, binary decoding) cleanly

The parser can be implemented incrementally, starting with basic pipelines and adding complexity over time.

---

## Next Steps

1. ✅ **Decided**: Use custom `.gflow` DSL (not plain YAML)
2. Define core plugin interfaces in Go
3. Implement basic DSL parser (MVP: from → transform → output)
4. Build first set of built-in plugins (TCP, HTTP, MQTT)
5. Create simple workflow executor
6. Add state management and retry logic
7. Implement routing and conditionals
8. Add schema validation support

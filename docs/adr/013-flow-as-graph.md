# ADR-013: Flow as Executable Graph

## Status

Accepted

## Context

Gluey flows describe how data moves through a system — from an input (MQTT subscription, HTTP endpoint), through transformations and enrichment, to an output. The challenge is expressing this in a way that is:

- **Declarative** — describes the graph, not the execution steps
- **Diagrammable** — the DSL is the text form of a visual diagram, like BPMN 2.0
- **Hierarchical** — complex steps can be zoomed into as their own sub-graphs
- **Trackable** — every node, every connection, every dependency is versioned
- **Language-agnostic** — precise enough to generate optimal code in C#, Rust, Go, or any target
- **LLM-readable** — an LLM can read, generate, and reason about the graph

The flow DSL is not a programming language. It is an intermediate layer between a graphic interface (where you draw lines between properties) and the generated imperative code. It must be precise enough to generate optimal code and abstract enough to draw diagrams.

BPMN 2.0 solved this problem for business processes. Gluey adapts the same concepts for data pipelines.

## Decision

### 1. A flow is a directed graph

A flow is a set of nodes connected by edges. Each node is a typed step. Each edge is a versioned connection carrying typed data. The DSL describes this graph in text form. Nodes are connected with the `|` pipe operator.

```gluey
flow telemetry-ingestion 1.0.0 {
  from mqtt-subscription telemetry-sub 1.0.0

  | enrich {
      device: http-endpoint get-device 1.0.0 { id: input.device_id }
    }

  | transform {
      output.device_id   <- input.device_id
      output.device_name <- device.name
      output.temperature <- input.temperature
      output.region      <- device.region
      output.recorded_at <- now()
    }

  | to http-endpoint store-reading 1.0.0
}
```

Every `<-` is a line on the diagram. Every `|` is an edge between nodes. The data flows top to bottom through the graph.

### 2. Node types

| Node | BPMN equivalent | Purpose |
|------|----------------|---------|
| `from` | Start Event (Message) | Entry point — receives data from an input |
| `to` | End Event (Message) | Exit point — sends data to an output |
| `transform` | Task | Connect input properties to output properties |
| `enrich` | Task (Service) | Fetch additional data from external sources in parallel |
| `split` | Task | Expand an array into individual messages |
| `batch` | Task | Collect individual messages into an array |
| `gateway` | Gateway | Route messages based on conditions |
| `flow-function` | Subprocess | Reusable sub-graph that can be zoomed into |

### 3. Transform — drawing lines between properties

The `transform` node connects input properties to output properties. Each `<-` is a line on the diagram. The left side is the output property, the right side is the source.

```gluey
| transform {
    output.device_id   <- input.device_id
    output.device_name <- device.name
    output.temperature <- input.temperature
    output.recorded_at <- now()
  }
```

When a property requires a transformation (e.g., unit conversion, string formatting), a named versioned transform function sits on the line:

```gluey
| transform {
    output.temperature_c <- input.temperature_f * 9 / 5 + 32
  }
```

The transform function is a versioned contract with declared input and output types. The DSL doesn't define the math — the function does.

### 4. Enrich — parallel data fetching

The `enrich` node fetches additional data from external sources. All enrichments within a single `enrich` block execute in parallel (like `Task.WhenAll` in C#).

```gluey
| enrich {
    device: http-endpoint get-device 1.0.0 { id: input.device_id }
    location: http-endpoint get-location 1.0.0 { id: input.location_id }
    config: http-endpoint get-config 1.0.0 { key: input.device_type }
  }
```

After the enrich step, subsequent nodes have access to `input`, `device`, `location`, and `config` — all as named, typed contexts.

### 5. Split and batch

`split` takes an array property and emits one message per element. Each downstream node processes individual items.

```gluey
| split input.readings
```

After split, the current context has a single `reading` instead of the array.

`batch` collects individual messages back into an array, with a configurable count or time window.

```gluey
| batch {
    size: 100
    timeout: "5s"
  }
```

### 6. Gateways — conditional routing

Gateways route messages based on conditions. They are nodes in the graph, not language constructs.

**Exclusive gateway** — pick one path (like BPMN exclusive gateway, XOR):

```gluey
| gateway exclusive {
    input.temperature > 100 -> flow-function handle-critical 1.0.0
    input.temperature > 50  -> flow-function handle-warning 1.0.0
    default                 -> flow-function handle-normal 1.0.0
  }
```

**Parallel gateway** — all paths execute (like BPMN parallel gateway, AND):

```gluey
| gateway parallel {
    -> to http-endpoint store-reading 1.0.0
    -> to mqtt-publication alert-pub 1.0.0
    -> to http-endpoint notify-dashboard 1.0.0
  }
```

**Inclusive gateway** — one or more paths based on conditions (like BPMN inclusive gateway, OR):

```gluey
| gateway inclusive {
    input.temperature > 100 -> to mqtt-publication critical-alert 1.0.0
    input.humidity > 90     -> to mqtt-publication humidity-alert 1.0.0
    default                 -> to http-endpoint store-reading 1.0.0
  }
```

### 7. Flow functions — hierarchical sub-graphs

A flow-function is a reusable sub-graph. It has declared inputs and outputs. It can contain any nodes — transform, enrich, split, gateways, and even other flow-functions.

```gluey
flow-function enrich-device-context 1.0.0 {
  in: json device-telemetry 1.0.0
  out: json enriched-telemetry 1.0.0

  | enrich {
      device: http-endpoint get-device 1.0.0 { id: input.device_id }
      location: http-endpoint get-location 1.0.0 { id: device.location_id }
    }

  | transform {
      output.device_id   <- input.device_id
      output.device_name <- device.name
      output.region      <- location.region
      output.temperature <- input.temperature
      output.recorded_at <- now()
    }
}
```

A flow references it as a step:

```gluey
flow telemetry-pipeline 1.0.0 {
  from mqtt-subscription telemetry-sub 1.0.0
  | flow-function enrich-device-context 1.0.0
  | to http-endpoint store-reading 1.0.0
}
```

On a diagram, the flow-function appears as a collapsed subprocess. Zooming in reveals its internal graph.

Flow-functions can be nested. A flow-function can call another flow-function. Each level is its own graph with its own versioned dependencies.

### 8. Results and failure handling

Every node in the graph produces a typed result. The next node (or the flow's failure handler) must handle it. There are no silent failures.

#### Every node returns a result

Each node produces either a value (passed to the next node) or an error. This is analogous to Rust's `Result<T, E>` — every function declares that it can fail, and the caller must decide what to do.

#### Three failure strategies

| Strategy | Description | Gluey syntax |
|----------|-------------|-------------|
| Handle | Inspect the result and act per outcome | `\| match` after the node |
| Propagate | Pass the error to the parent's failure handler | `on-failure: ?` |
| Route | Send the error to a specific failure store | `on-failure: failure-store X 1.0.0` |

#### Flow-level failure declaration

A flow declares what happens when it fails. This is part of the flow's contract with its caller — the orchestrator, the runtime, or a parent flow-function.

```gluey
flow telemetry-pipeline 1.0.0 {
  on-failure: failure-store telemetry-failures 1.0.0

  from mqtt-subscription telemetry-sub 1.0.0
  | flow-function enrich-device-context 1.0.0
  | to http-endpoint store-reading 1.0.0
}
```

Any unhandled error in the graph — enrich returns an error, transform encounters missing data, gateway condition throws — is routed to the declared failure store with full context: which node failed, what the input was, what the error was.

#### Flow-function failure propagation

A flow-function can handle failures internally or propagate them to the parent flow:

```gluey
// Handles failures internally — routes to its own failure store
flow-function enrich-device-context 1.0.0 {
  in: json device-telemetry 1.0.0
  out: json enriched-telemetry 1.0.0
  on-failure: failure-store enrichment-failures 1.0.0

  | enrich {
      device: http-endpoint get-device 1.0.0 { id: input.device_id }
    }
  | transform { ... }
}

// Propagates failures to parent — parent's on-failure handles it
flow-function validate-payload 1.0.0 {
  in: json raw-message 1.0.0
  out: json validated-message 1.0.0
  on-failure: ?

  | transform { ... }
}
```

`on-failure: ?` means "I don't handle failures — my caller does." This is equivalent to Rust's `?` operator that propagates errors up the call chain.

#### Node-level match

A flow can inspect the result of a specific node and handle each outcome explicitly:

```gluey
flow provision-server 1.0.0 {
  on-failure: failure-store provision-failures 1.0.0

  | to http-endpoint create-server 1.0.0

  | match response.status_code {
      201 -> log("created: ${response.body.server.id}")
      429 -> retry(after: response.headers["Retry-After"])
      _   -> alert("${response.status_code}: ${response.reason_phrase}")
    }
}
```

The `match` handles specific outcomes. Anything not matched falls through to `on-failure`.

#### Failure store

The failure store is itself a versioned contract. It receives the failed message, the error context, and the node that failed. It is the dead letter queue of the flow — every message that can't be processed ends up here for inspection, replay, or alerting.

### 9. Everything is trackable

Every node in the graph is a versioned reference:
- `from` and `to` reference plugin contracts (mqtt-subscription, http-endpoint)
- `enrich` references endpoints
- `transform` references properties from declared contracts
- `gateway` conditions reference properties from declared contracts
- `flow-function` references a versioned sub-graph
- `split` and `batch` reference array properties from declared contracts
- Transform functions on transform lines are versioned

Gluey can walk the entire graph and identify every dependency. Changing any referenced contract triggers impact analysis across all flows that use it.

### 10. The DSL is the diagram

The text DSL and the visual diagram are interchangeable representations of the same graph. An LLM reads the DSL. A human draws the diagram. Both produce the same graph. The code generator takes the graph and emits optimal imperative code for the target runtime.

```
┌─────────────┐     ┌─────────────────┐     ┌──────────┐     ┌─────────────┐
│ mqtt-sub    │────▶│ enrich-device   │────▶│ gateway  │────▶│ http-store  │
│ telemetry   │     │ (subprocess)    │     │ exclusive│     │ readings    │
└─────────────┘     └─────────────────┘     └────┬─────┘     └─────────────┘
                                                 │
                                                 ▼
                                          ┌─────────────┐
                                          │ mqtt-pub    │
                                          │ alert       │
                                          └─────────────┘
```

This diagram IS the DSL. The DSL IS this diagram. They are the same thing in different formats.

## Consequences

- Flows are graphs, not programs — every step is a typed, versioned node
- The DSL is an intermediate representation between visual diagrams and generated code
- Conditional logic uses gateways (graph nodes), not language constructs (if/else)
- Complex logic is composed from flow-functions (sub-graphs), not inline code
- No escape hatch to imperative code — the DSL must be expressive enough to describe any data pipeline as a graph
- Every connection in the graph is a versioned, trackable dependency
- The same graph can be rendered as text (DSL), visual diagram, or generated code
- Failure handling is declarative — declare where failures go, the runtime routes them

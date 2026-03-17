# Gluey Contract Model

Everything in Gluey is a **contract** — a versioned, declarative description of a system component with clear ownership boundaries. All contracts use a single file extension (`.gluey`) and follow semver (`major.minor.patch`, no `v` prefix). The contract type is identified by the keyword on the first line.

## Contract Hierarchy

```
contract (base)
  │
  ├── primitive             logical type (text, int32, bool, etc.)
  ├── struct                in-memory shape after protocol parsing
  ├── json                  JSON schema contract
  ├── app                   what a service is
  ├── plugin                protocol adapter (HTTP, MQTT, gRPC)
  ├── flow                  declarative data pipeline
  ├── auth                  authentication and authorization
  │
  └── plugin-defined        contracts registered by plugins
        ├── http-endpoint   HTTP API endpoint (defined by http plugin)
        ├── http-headers    HTTP header requirements (defined by http plugin)
        ├── mqtt-subscription  MQTT topic subscription (defined by mqtt plugin)
        └── mqtt-publication   MQTT topic publication (defined by mqtt plugin)
```

## Dependency Map

```
App
  │
  └── Plugin ──── Flow
        │
        ┌────────┴────────┐
        │  Plugin defines  │
        │  structs and     │
        │  contracts       │
        └────────┬────────┘
                 │
   ┌─────────────┼─────────────┐
   │             │             │
http-endpoint  mqtt-sub     mqtt-pub
   │             │             │
┌──┴────┐     payload       payload
│       │    (json/etc)    (json/etc)
headers  responses
(validate) (per status code)
```

---

## Types

### Primitive

Logical type definition. Runtime maps it to native representation (see ADR-001).

```gluey
primitive text 1.0.0 {
  displayName: { "en-US": "Text" }
  description: { "en-US": "Unicode text, runtime determines memory encoding" }
}

primitive int32 1.0.0 {
  signed: true
  bits: 32
  displayName: { "en-US": "Int32" }
  description: { "en-US": "32-bit signed integer" }
}
```

Primitives support enums and flags (see ADR-001).

### Struct

In-memory shape produced by a plugin after parsing the wire format. Not JSON, not binary — the parsed protocol frame.

```gluey
struct http-response 2.0.0 {
  properties: {
    status_code: int32 1.0.0 { required: true }
    reason_phrase: text 1.0.0 { required: true }
    headers: map 1.0.0 {
      required: true
      key: text 1.0.0 { comparison: "ordinal-ignore-case" }
      value: text 1.0.0
    }
    body: bytes 1.0.0 { required: false }
  }
}
```

### JSON

Format-specific data contract (see ADR-002, ADR-004). Every property references a named, versioned type with JSON Schema validation attributes.

```gluey
json device-telemetry 1.0.0 {
  properties: {
    device_id: text 1.0.0 {
      required: true
      minLength: 1
      displayName: { "en-US": "Device ID" }
      description: { "en-US": "Unique identifier of the device" }
    }
    timestamp: timestamp 1.0.0 {
      required: true
      displayName: { "en-US": "Timestamp" }
      description: { "en-US": "UTC timestamp when telemetry was recorded" }
    }
    payload: bytes 1.0.0 {
      required: true
      displayName: { "en-US": "Payload" }
      description: { "en-US": "Raw telemetry payload from the device" }
    }
  }
}
```

### Property attributes

Every property across all contract types supports:

| Attribute | Default | Description |
|-----------|---------|-------------|
| `required` | `false` | Whether the property must be present |
| `obfuscate` | `false` | Whether the value is redacted in logs and payload revision (see ADR-003) |
| `displayName` | — | Localized display name, keyed by IETF language tag |
| `description` | — | Localized description, keyed by IETF language tag |
| `represents` | — | Structural embedding — declares that `text` or `bytes` contains a parseable document conforming to a specific contract (see ADR-006) |

---

## Plugin Side

### Plugin

Protocol adapter — the SDK between flows and protocols. Each protocol version is a separate plugin. The plugin declares its structs and registers contract keywords with Gluey (see ADR-008, ADR-010, ADR-011).

```gluey
plugin http 2.0.0 {
  structs: [http-request 2.0.0, http-response 2.0.0]
  contracts: [http-endpoint, http-headers]
}

plugin mqtt 5.0.0 {
  structs: [mqtt-message 5.0.0]
  contracts: [mqtt-subscription, mqtt-publication]
}
```

### HTTP endpoint (defined by http plugin)

Describes a specific API call — method, path, what goes in, what comes out. The plugin owns the request/response structs; the endpoint defines what's inside them (see ADR-009, ADR-010).

```gluey
http-endpoint create-server 1.0.0 {
  app: hetzner 1.0.0
  plugin: http 2.0.0
  method: POST
  path: "/v1/servers"
  headers: hetzner-write-headers 1.0.0
  payload: json create-server-request 1.0.0
  responses: {
    201: json create-server-response 1.0.0
    429: json hetzner-rate-limit 1.0.0
    default: json hetzner-error 1.0.0
  }
}
```

### HTTP headers (defined by http plugin)

Header requirements with validation. Flat, explicit, per-provider.

```gluey
http-headers hetzner-write-headers 1.0.0 {
  properties: {
    Authorization: text 1.0.0 {
      required: true
      obfuscate: true
      pattern: "^Bearer .+$"
      displayName: { "en-US": "Authorization" }
      description: { "en-US": "Bearer token for API authentication" }
    }
    Content-Type: text 1.0.0 {
      required: true
      enum: ["application/json"]
      displayName: { "en-US": "Content Type" }
      description: { "en-US": "Request body media type" }
    }
  }
}
```

### MQTT subscription (defined by mqtt plugin)

Declares what topics to listen to, topic segment structure, and payload format (see ADR-011).

```gluey
mqtt-subscription telemetry-sub 1.0.0 {
  app: emqx 1.0.0
  plugin: mqtt 5.0.0
  topic: "riot/+/telemetry" {
    0: text 1.0.0 { const: "riot" }
    1: text 1.0.0 {
      name: "device_id"
      required: true
      displayName: { "en-US": "Device ID" }
      description: { "en-US": "Unique identifier of the publishing device" }
    }
    2: text 1.0.0 { const: "telemetry" }
  }
  qos: 1
  payload: json device-telemetry 1.0.0
}
```

### MQTT publication (defined by mqtt plugin)

Declares where to publish and payload format.

```gluey
mqtt-publication command-pub 1.0.0 {
  app: emqx 1.0.0
  plugin: mqtt 5.0.0
  topic: "riot/${device_id}/command" {
    0: text 1.0.0 { const: "riot" }
    1: text 1.0.0 {
      name: "device_id"
      required: true
      displayName: { "en-US": "Device ID" }
      description: { "en-US": "Target device identifier" }
    }
    2: text 1.0.0 { const: "command" }
  }
  qos: 1
  retain: false
  payload: json device-command 1.0.0
}
```

---

## Flow Side

### Flow

Declarative data pipeline — reads from an input, transforms, writes to an output. References use the full contract type.

```gluey
flow telemetry-ingestion 1.0.0 {
  from mqtt-subscription telemetry-sub 1.0.0

  | transform {
      output.device_id   <- topic.device_id
      output.temperature <- payload.temperature
      output.received_at <- now()
    }

  | to http-endpoint tsdb-insert 1.0.0
}
```

---

## Versioning

All contracts follow semver. Every reference is an exact version lock — no ranges. Changes propagate up the dependency tree. See ADR-005 for full versioning rules.

---

## ADR Index

| ADR | Title |
|-----|-------|
| [001](adr/001-logical-types-and-runtime-mapping.md) | Logical Types and Runtime Mapping |
| [002](adr/002-format-specific-contract-keywords.md) | Format-Specific Contract Keywords |
| [003](adr/003-property-obfuscation.md) | Property Obfuscation Flag |
| [004](adr/004-json-contract-dsl.md) | JSON Contract DSL |
| [005](adr/005-universal-versioning.md) | Universal Versioning and Change Tracking |
| [006](adr/006-structural-represents.md) | Structural Representation for Embedded Contracts |
| [007](adr/007-map-type.md) | Map Type with Typed Keys and Values |
| [008](adr/008-plugin-contract.md) | Plugin Contract |
| [009](adr/009-http-plugin-response-and-endpoint-responses.md) | HTTP Plugin Response and Endpoint Responses |
| [010](adr/010-http-plugin.md) | HTTP Plugin |
| [011](adr/011-mqtt-plugin.md) | MQTT Plugin |
| [012](adr/012-authentication-contract.md) | Authentication Contract |
| [013](adr/013-flow-as-graph.md) | Flow as Executable Graph |

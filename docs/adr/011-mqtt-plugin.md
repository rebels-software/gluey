# ADR-011: MQTT Plugin

## Status

Accepted

## Context

MQTT is the primary protocol for IoT device communication. Gluey flows need to subscribe to device telemetry, publish commands, and react to device events. The MQTT plugin is the adapter between flows and the MQTT protocol.

MQTT has evolved through several versions with significant differences:

- **MQTT 3.1** — basic pub/sub, limited session management
- **MQTT 3.1.1** — clarifications, cleaner session handling, the most widely deployed version
- **MQTT 5.0** — shared subscriptions, message expiry, user properties, reason codes, topic aliases, flow control

Each version is a different protocol with different wire behavior and capabilities.

## Decision

### 1. One plugin per protocol version

```gluey
plugin mqtt 3.1.0 {
  structs: [mqtt-message 3.1.0]
  contracts: [mqtt-subscription, mqtt-publication]
}

plugin mqtt 3.1.1 {
  structs: [mqtt-message 3.1.1]
  contracts: [mqtt-subscription, mqtt-publication]
}

plugin mqtt 5.0.0 {
  structs: [mqtt-message 5.0.0]
  contracts: [mqtt-subscription, mqtt-publication]
}
```

- `structs` — the message shape the plugin exposes to flows. Connection management (connack, suback, disconnect) is internal to the plugin — the plugin maintains the connection, handles reconnections, and tracks disconnections. Flows only see messages.
- `contracts` — `mqtt-subscription` (input) and `mqtt-publication` (output), shared across versions.

### 2. Protocol struct (MQTT 5.0.0)

```gluey
struct mqtt-message 5.0.0 {
  properties: {
    topic: text 1.0.0 {
      required: true
      displayName: { "en-US": "Topic" }
      description: { "en-US": "MQTT topic the message was published to" }
    }
    payload: bytes 1.0.0 {
      required: true
      displayName: { "en-US": "Payload" }
      description: { "en-US": "Raw message payload" }
    }
    qos: int32 1.0.0 {
      required: true
      minimum: 0
      maximum: 2
      displayName: { "en-US": "QoS" }
      description: { "en-US": "Quality of Service level: 0 = at most once, 1 = at least once, 2 = exactly once" }
    }
    retain: bool 1.0.0 {
      required: true
      displayName: { "en-US": "Retain" }
      description: { "en-US": "Whether the message is retained by the broker" }
    }
    message_expiry_interval: int32 1.0.0 {
      required: false
      displayName: { "en-US": "Message Expiry Interval" }
      description: { "en-US": "Lifetime of the message in seconds, MQTT 5.0 only" }
    }
    response_topic: text 1.0.0 {
      required: false
      displayName: { "en-US": "Response Topic" }
      description: { "en-US": "Topic for request/response pattern, MQTT 5.0 only" }
    }
    correlation_data: bytes 1.0.0 {
      required: false
      displayName: { "en-US": "Correlation Data" }
      description: { "en-US": "Correlation data for request/response pattern, MQTT 5.0 only" }
    }
    content_type: text 1.0.0 {
      required: false
      displayName: { "en-US": "Content Type" }
      description: { "en-US": "MIME type of the payload, MQTT 5.0 only" }
    }
    user_properties: map 1.0.0 {
      required: false
      key: text 1.0.0
      value: text 1.0.0
      displayName: { "en-US": "User Properties" }
      description: { "en-US": "User-defined key-value metadata, MQTT 5.0 only" }
    }
  }
}
```

### 3. Topic structure contract

MQTT topics are slash-delimited paths where each segment carries meaning. The subscription declares the topic structure — each segment is a named, typed, versioned position.

```gluey
mqtt-subscription telemetry-sub 1.0.0 {
  app: emqx 1.0.0
  plugin: mqtt 5.0.0
  topic: "riot/+/telemetry" {
    0: text 1.0.0 {
      const: "riot"
      displayName: { "en-US": "Namespace" }
      description: { "en-US": "Application namespace" }
    }
    1: text 1.0.0 {
      name: "device_id"
      required: true
      displayName: { "en-US": "Device ID" }
      description: { "en-US": "Unique identifier of the publishing device" }
    }
    2: text 1.0.0 {
      const: "telemetry"
      displayName: { "en-US": "Message Type" }
      description: { "en-US": "Type of message" }
    }
  }
  qos: 1
  payload: json device-telemetry 1.0.0
}
```

- `const` segments are fixed — Gluey validates that the topic pattern matches
- Wildcard segments (`+`) are variable — the `name:` property declares the identifier the flow uses to access the segment value at runtime
- The segment contract is versioned — if the topic structure changes (segment added, moved, renamed), the subscription version bumps and Gluey flags every flow that references those segments

The flow accesses segments by name, not by index:

```gluey
flow telemetry-ingestion 1.0.0 {
  from mqtt-subscription telemetry-sub 1.0.0

  | transform {
      output.device_id   <- topic.device_id
      output.received_at <- now()
    }
}
```

If the topic changes from `riot/+/telemetry` to `riot/+/region/telemetry`, `device_id` is still at position 1 but `telemetry` moved to position 3. Gluey detects this as a breaking change because segment 2 changed from `const: "telemetry"` to a new segment.

Publications declare topic structure the same way, with variable interpolation from the flow context:

```gluey
mqtt-publication command-pub 1.0.0 {
  app: emqx 1.0.0
  plugin: mqtt 5.0.0
  topic: "riot/${device_id}/command" {
    0: text 1.0.0 {
      const: "riot"
      displayName: { "en-US": "Namespace" }
      description: { "en-US": "Application namespace" }
    }
    1: text 1.0.0 {
      name: "device_id"
      required: true
      displayName: { "en-US": "Device ID" }
      description: { "en-US": "Target device identifier" }
    }
    2: text 1.0.0 {
      const: "command"
      displayName: { "en-US": "Message Type" }
      description: { "en-US": "Type of message" }
    }
  }
  qos: 1
  retain: false
  payload: json device-command 1.0.0
}
```

### 4. MQTT subscription contract (input)

A subscription declares what topics to listen to, the topic structure, and what the payload contains. Flows use `from` to read from a subscription.

- `app` — the broker this subscription connects to
- `plugin` — which MQTT version, determining the message struct
- `topic` — topic filter pattern with segment definitions
- `qos` — requested Quality of Service level
- `payload` — format and contract for the message payload

The flow receives `struct mqtt-message 5.0.0` with the `payload` bytes deserialized into the declared contract and topic segments accessible by name.

### 5. MQTT publication contract (output)

A publication declares where to send messages, the topic structure, and what the payload contains. Flows use `to` to write to a publication.

- `topic` — the target topic with segment definitions, supports variable interpolation
- `retain` — whether the broker should retain the message
- `payload` — format and contract for the message payload

The runtime validates the payload against the declared contract before publishing.

### 6. Input and output

- **Subscription (input)** — the flow reads messages via `from`. The plugin subscribes to the broker and delivers `mqtt-message` structs.
- **Publication (output)** — the flow writes messages via `to`. The plugin publishes to the broker.

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

flow device-command 1.0.0 {
  from http-endpoint command-api 1.0.0

  | transform {
      output.device_id <- payload.device_id
      output.command   <- payload.command
    }

  | to mqtt-publication command-pub 1.0.0
}
```

The `from` and `to` references include the full contract type — `mqtt-subscription`, `mqtt-publication`, `http-endpoint`. This makes the dependency explicit: the flow declares not just which contract it uses, but which plugin's contract type.

### 7. The plugin does not interpret messages

The plugin delivers the raw `mqtt-message` struct — topic, payload bytes, QoS, retain flag, user properties. It does not parse the payload. The subscription contract declares what format the payload is in, and the runtime deserializes accordingly.

The flow decides what to do with the message — filter by topic segments, transform the payload, route to different outputs based on content.

## Consequences

- Each MQTT version is a separate plugin with its own message struct — MQTT 5.0 features (user properties, message expiry, content type) are only available through `plugin mqtt 5.0.0`
- Connection management (connect, subscribe, disconnect) is internal to the plugin — flows only see messages
- Topic structure is a versioned contract — each segment is named, typed, and trackable. Changes to topic layout are breaking changes that Gluey detects
- Flows access topic segments by name, not by index — refactoring-safe and self-documenting
- Subscriptions and publications are separate contract types, reflecting the asymmetry of pub/sub
- Payload format is declared on the subscription/publication, not the plugin — the same broker can carry JSON on one topic and protobuf on another
- The dependency graph connects flows to brokers: `flow → mqtt-subscription → app (broker)`
- Flow `from`/`to` references include the full contract type, making plugin ownership explicit in the dependency graph

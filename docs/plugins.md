# Gluey Plugins Reference

This document covers all 12 built-in plugins available in Gluey CLI.

## Table of Contents

- [Input Plugins](#input-plugins)
  - [http](#http-input)
  - [mqtt](#mqtt-input)
- [Transform Plugins](#transform-plugins)
  - [json.parse](#jsonparse)
  - [filter](#filter)
  - [transform](#transform)
  - [decode.binary](#decodebinary)
  - [decode.base64](#decodebase64)
  - [decode.hex](#decodehex)
  - [route](#route)
- [Output Plugins](#output-plugins)
  - [console](#console)
  - [http](#http-output)
  - [mqtt](#mqtt-output)
  - [sql](#sql)

---

## Input Plugins

Input plugins receive data and feed it into the workflow pipeline.

### http (Input)

HTTP webhook input that listens for POST requests. Useful for receiving webhooks from external services or testing workflows.

**Config Options**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `port` | number | 8080 | Port to listen on |
| `path` | string | /webhook | URL path to accept requests |

**Supported Content Types**

- `application/json` - Parsed as JSON and used as message payload
- `application/octet-stream` - Raw bytes stored as base64 in `raw_bytes` metadata
- Other - Text content wrapped as `{"data": "..."}`

**Example**

```gflow
flow webhook-receiver v1.0 {
  from http("/api/events") {
    port: 3000
  }

  | json.parse(payload)
  | console()
}
```

**Testing with curl**

```bash
curl -X POST http://localhost:8080/webhook \
  -H "Content-Type: application/json" \
  -d '{"temperature": 25.5, "device": "sensor-01"}'
```

---

### mqtt (Input)

MQTT subscriber that connects to an MQTT broker and subscribes to topics. Supports TLS and authentication.

**Config Options**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `url` | string | *required* | Broker URL (mqtt:// or mqtts://) |
| `topics` | array | *required* | List of topic patterns to subscribe |
| `topic` | string | - | Single topic (alternative to topics) |
| `qos` | number | 1 | Quality of Service (0, 1, or 2) |
| `client_id` | string | auto-generated | Client identifier |
| `username` | string | - | Authentication username |
| `password` | string | - | Authentication password |
| `tls` | boolean | false | Force TLS (auto-detected from mqtts://) |

**Topic Wildcards**

- `+` matches a single level: `sensors/+/temperature`
- `#` matches multiple levels: `devices/#`

**Message Metadata**

Each message includes metadata:
- `source`: "mqtt"
- `topic`: The topic the message was received on
- `qos`: The QoS level
- `retain`: Whether the message was retained

**Example**

```gflow
flow mqtt-subscriber v1.0 {
  from mqtt("mqtt://broker.local:1883") {
    topics: ["sensors/+/temperature", "sensors/+/humidity"]
    qos: 1
    client_id: "gluey-worker-01"
  }

  | json.parse(payload)
  | console()
}
```

**With TLS and Authentication**

```gflow
flow secure-mqtt v1.0 {
  from mqtt("mqtts://secure-broker.example.com:8883") {
    topics: ["devices/#"]
    username: "iot-client"
    password: "secret"
  }

  | json.parse(payload)
  | console()
}
```

---

## Transform Plugins

Transform plugins process messages in the pipeline. They can modify, filter, decode, or route messages.

### json.parse

Parses JSON payloads from raw bytes or base64-encoded data. Often used after receiving binary HTTP payloads or MQTT messages.

**Config Options**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `on_error` | string/object | skip | Error handling action |

**Error Handling**

- `on_error: skip` - Drop messages that fail to parse (default)
- `on_error: { action: "route_to", target: "error_queue" }` - Set `_route_error` metadata

**Data Sources**

The plugin looks for JSON data in this order:
1. `raw_bytes` metadata (base64 from HTTP binary input)
2. `data` field in payload (wrapped text)
3. Existing JSON object/array (pass-through)
4. Root-level string containing JSON

**Example**

```gflow
flow parse-json v1.0 {
  from http("/webhook")

  | json.parse(payload)
  | console()
}
```

**With Error Routing**

```gflow
flow parse-with-errors v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["data/#"]
  }

  | json.parse(payload) {
      on_error: { action: "route_to", target: "parse_errors" }
    }
  | console()
}
```

---

### filter

Filters messages based on a condition expression. Messages that don't match the condition are dropped from the pipeline.

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `condition` | string | Expression to evaluate (set via `filter(expr)` syntax) |

**Expression Syntax**

Supports:
- **Comparison operators**: `>`, `<`, `>=`, `<=`, `==`, `!=`
- **Logical operators**: `&&`, `||`
- **Parentheses**: `(expr)`
- **Field access**: `temperature`, `device.location.type`
- **Literals**: numbers, quoted strings, `true`, `false`, `null`

**Example**

```gflow
flow temperature-filter v1.0 {
  from http("/webhook")
  | json.parse(payload)

  | filter(temperature > 20)

  | console()
}
```

**Complex Conditions**

```gflow
flow complex-filter v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/#"]
  }
  | json.parse(payload)

  | filter(temperature > 30 && device.type == "outdoor")
  | filter(battery_level >= 20 || is_powered == true)

  | console()
}
```

---

### transform

Creates a new payload from field mappings. Supports field copying, nested access, arithmetic, ternary expressions, and built-in functions.

**Config Options**

Configuration is a map of output field names to expressions:

```gflow
| transform {
    output_field: expression
  }
```

**Expression Features**

- **Direct field copy**: `device_id: device_id`
- **Nested field access**: `lat: device.location.lat`
- **Arithmetic**: `temp_f: temperature * 9 / 5 + 32`
- **Ternary conditionals**: `level: temperature > 30 ? "high" : "normal"`
- **String concatenation**: `full_name: first_name + " " + last_name`

**Built-in Functions**

| Function | Returns | Description |
|----------|---------|-------------|
| `now()` | string | ISO 8601 timestamp (UTC) |
| `uuid()` | string | Random UUID |
| `timestamp()` | number | Unix timestamp in milliseconds |

**Example**

```gflow
flow sensor-transform v1.0 {
  from http("/webhook")
  | json.parse(payload)

  | transform {
      device_id: device_id
      temperature_celsius: temperature
      temperature_fahrenheit: temperature * 9 / 5 + 32
      humidity: humidity
      alert_level: temperature > 30 ? "high" : "normal"
      processed_at: now()
      message_id: uuid()
    }

  | console()
}
```

---

### decode.binary

Decodes binary payloads using a specified format. Extracts fields from raw bytes with explicit type and endianness.

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `field` | string | Optional source field containing base64 data |
| `format` | array | Array of field definitions |

**Format Definition**

Object format (recommended):
```gflow
{ name: "field_name", offset: 0, length: 4, type: "string" }
```

String format:
```gflow
"field_name: bytes(0, 4, \"string\")"
```

**Supported Types**

| Type | Description |
|------|-------------|
| `ascii`, `string` | ASCII/UTF-8 string (null-terminated) |
| `uint8` | Unsigned 8-bit integer |
| `int16_be`, `int16_le` | Signed 16-bit integer (big/little endian) |
| `uint16_be`, `uint16_le` | Unsigned 16-bit integer (big/little endian) |
| `int32_be`, `int32_le` | Signed 32-bit integer (big/little endian) |
| `uint32_be`, `uint32_le` | Unsigned 32-bit integer (big/little endian) |
| `float32_be`, `float32_le` | IEEE 754 32-bit float (big/little endian) |

**Data Sources**

The plugin looks for binary data in this order:
1. `raw_bytes` metadata (base64 from HTTP binary input)
2. Specified `field` in payload (base64)
3. `data` field in payload (base64 or UTF-8)

**Example**

```gflow
flow binary-sensor v1.0 {
  from http("/webhook")

  // Binary format: 4-byte string + 2-byte temp + 2-byte humidity + 1-byte battery
  | decode.binary(payload) {
      format: [
        { name: "device_id", offset: 0, length: 4, type: "string" },
        { name: "temperature_raw", offset: 4, length: 2, type: "int16_be" },
        { name: "humidity_raw", offset: 6, length: 2, type: "uint16_le" },
        { name: "battery", offset: 8, length: 1, type: "uint8" }
      ]
    }

  | transform {
      device_id: device_id
      temperature: temperature_raw / 100
      humidity: humidity_raw / 100
      battery_percent: battery
    }

  | console()
}
```

---

### decode.base64

Decodes base64 strings to raw bytes. Can optionally apply binary format decoding in one step.

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `field` | string | Optional source field containing base64 string |
| `format` | array | Optional binary format (same as decode.binary) |

**Common Field Names**

If no `field` is specified, the plugin looks in:
- `frm_payload` (LoRaWAN)
- `payload`
- `data`
- `base64`

**Example**

```gflow
flow lorawan-decoder v1.0 {
  from http("/webhook")
  | json.parse(payload)

  // Decode LoRaWAN payload (frm_payload contains base64)
  | decode.base64(frm_payload) {
      format: [
        { name: "temperature", offset: 0, length: 2, type: "int16_be" },
        { name: "humidity", offset: 2, length: 1, type: "uint8" }
      ]
    }

  | transform {
      temperature_celsius: temperature / 10
      humidity_percent: humidity / 2
    }

  | console()
}
```

---

### decode.hex

Decodes hexadecimal strings to raw bytes. Supports 0x prefix and space-separated bytes.

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `field` | string | Optional source field containing hex string |
| `format` | array | Optional binary format (same as decode.binary) |

**Supported Formats**

- `"41424344"` - Plain hex
- `"0x41424344"` - With 0x prefix
- `"41 42 43 44"` - Space-separated

**Common Field Names**

If no `field` is specified, the plugin looks in:
- `hex_payload`
- `hex`
- `hex_data`
- `payload`
- `data`

**Example**

```gflow
flow hex-decoder v1.0 {
  from http("/webhook")
  | json.parse(payload)

  // Input: {"hex_payload": "41424344001E00C8"}
  | decode.hex(hex_payload) {
      format: [
        { name: "device_type", offset: 0, length: 4, type: "string" },
        { name: "device_id", offset: 4, length: 2, type: "uint16_be" },
        { name: "temperature", offset: 6, length: 2, type: "int16_be" }
      ]
    }

  | console()
}
```

---

### route

Routes messages to different outputs based on conditions. The first matching condition wins. Sets `_route` metadata for the WorkflowRunner.

**Config Options**

Configuration is a map of route names to condition expressions:

```gflow
| route {
    route_name: condition_expression
  }
```

**Special Conditions**

- `*` - Catch-all route that always matches
- Empty condition - Also matches all (catch-all behavior)

**Example**

```gflow
flow alert-router v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/#"]
  }
  | json.parse(payload)

  | transform {
      device_id: device_id
      temperature: temperature
      alert_level: temperature > 80 ? "critical" : (temperature > 60 ? "warning" : "normal")
    }

  | route {
      critical_alerts: alert_level == "critical"
      warnings: alert_level == "warning"
      normal_data: *
    }

  critical_alerts -> mqtt("mqtt://broker:1883") {
    topic: "alerts/critical"
  }

  warnings -> mqtt("mqtt://broker:1883") {
    topic: "alerts/warning"
  }

  normal_data -> console()
}
```

**Fan-out (Multiple Outputs)**

A route destination can send to multiple outputs in parallel using array syntax:

```gflow
flow broadcast v1.0 {
  from http("/webhook")
  | json.parse(payload)
  | transform {
      device_id: device_id
      temperature: temperature
      processed_at: now()
    }
  | route {
      all: *
    }

  all -> [console(), http("https://api.example.com/events")]
}
```

Fan-out behavior:
- All outputs execute in parallel
- An error in one output does not block others
- Each output receives the same message

---

## Output Plugins

Output plugins send processed messages to external systems.

### console

Prints messages to stdout for debugging. Formats the payload as indented JSON with a timestamp prefix.

**Config Options**

No configuration required.

**Output Format**

```
[2026-01-29T10:30:45.123+00:00] {
  "device_id": "sensor-01",
  "temperature": 25.5
}
```

**Example**

```gflow
flow debug-output v1.0 {
  from http("/webhook")
  | json.parse(payload)
  | console()
}
```

---

### http (Output)

POSTs messages as JSON to a configured URL. Fails fast on HTTP errors (4xx/5xx).

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `url` | string | *required* - Target URL (http:// or https://) |

**Headers**

Automatically sets `Content-Type: application/json`.

**Example**

```gflow
flow webhook-forward v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["events/#"]
  }
  | json.parse(payload)

  | http("https://api.example.com/events")
}
```

---

### mqtt (Output)

Publishes messages to an MQTT broker. Supports TLS, authentication, and topic interpolation.

**Config Options**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `url` | string | *required* | Broker URL (mqtt:// or mqtts://) |
| `topic` | string | *required* | Topic to publish to |
| `qos` | number | 1 | Quality of Service (0, 1, or 2) |
| `retain` | boolean | false | Retain flag for messages |
| `client_id` | string | auto-generated | Client identifier |
| `username` | string | - | Authentication username |
| `password` | string | - | Authentication password |
| `tls` | boolean | false | Force TLS (auto-detected from mqtts://) |

**Topic Interpolation**

Use `{{field}}` syntax to include message field values in the topic:
- `devices/{{device_id}}/data`
- `sensors/{{device.location}}/temperature`

**Example**

```gflow
flow mqtt-publisher v1.0 {
  from http("/webhook")
  | json.parse(payload)

  | mqtt("mqtt://broker.local:1883") {
      topic: "devices/{{device_id}}/processed"
      qos: 1
      retain: false
    }
}
```

---

### sql

Inserts messages into PostgreSQL or SQL Server databases. Auto-detects the dialect from the connection string.

**Config Options**

| Option | Type | Description |
|--------|------|-------------|
| `connection_string` | string | *required* - Database connection string |
| `url` | string | Alternative to connection_string |
| `table` | string | *required* - Table name to insert into |
| `columns` | object/array | *required* - Column to field mappings |

**Column Mapping Formats**

Object format:
```gflow
columns: {
  column_name: "field_path"
}
```

Array format:
```gflow
columns: [
  { column: "column_name", field: "field_path" }
]
```

**Supported Databases**

- **PostgreSQL** - Connection strings starting with `Host=`, `postgres://`, `postgresql://`
- **SQL Server** - Connection strings with `Server=`, `Data Source=`, `Initial Catalog=`, `sqlserver://`, `mssql://`

**Error Handling**

Insert failures are logged to stderr but don't crash the workflow. This allows processing to continue for other messages.

**Type Handling**

- Strings, numbers, booleans map directly to SQL types
- ISO 8601 timestamps (from `now()`) convert to SQL TIMESTAMP
- Objects and arrays are stored as JSON strings
- Null values are passed as DBNull

**Example - PostgreSQL**

```gflow
flow postgres-writer v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/#"]
  }
  | json.parse(payload)

  | transform {
      device_id: device_id
      temperature: temperature
      humidity: humidity
      recorded_at: now()
    }

  | sql("Host=localhost;Database=iot;Username=app;Password=secret") {
      table: "sensor_readings"
      columns: {
        device_id: "device_id"
        temperature: "temperature"
        humidity: "humidity"
        recorded_at: "recorded_at"
      }
    }
}
```

**Example - SQL Server**

```gflow
flow sqlserver-writer v1.0 {
  from http("/webhook")
  | json.parse(payload)

  | sql("Server=localhost;Database=IoT;User Id=sa;Password=YourPassword;TrustServerCertificate=true") {
      table: "DeviceEvents"
      columns: {
        DeviceId: "device_id"
        EventType: "event_type"
        Payload: "data"
        CreatedAt: "timestamp"
      }
    }
}
```

---

## Related Documentation

- [Getting Started](getting-started.md) - Quick start guide
- [DSL Reference](dsl-reference.md) - Complete DSL syntax documentation

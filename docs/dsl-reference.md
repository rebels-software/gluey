# Gluey DSL Reference

Complete syntax documentation for the `.gflow` domain-specific language.

## Table of Contents

1. [Flow Structure](#flow-structure)
2. [Input Block](#input-block)
3. [Transform Syntax](#transform-syntax)
4. [Output Block](#output-block)
5. [Route Block](#route-block)
6. [Error Handling](#error-handling)
7. [Expression Syntax](#expression-syntax)

---

## Flow Structure

Every `.gflow` file defines a workflow using the `flow` keyword.

### Basic Syntax

```gflow
flow <name> v<major>.<minor> {
  from <input>

  | <transform>
  | <transform>
  ...
  | <output>
}
```

### Components

| Part | Description |
|------|-------------|
| `flow` | Required keyword to start a workflow definition |
| `<name>` | Workflow identifier (alphanumeric, hyphens allowed) |
| `v<major>.<minor>` | Version string (e.g., `v1.0`, `v2.3`) |
| `from` | Input source definition |
| `\|` | Pipeline operator connecting transforms |

### Naming Rules

- Must start with a letter
- Can contain letters, numbers, and hyphens
- Cannot contain spaces or special characters
- Examples: `hello-world`, `sensor-pipeline`, `mqtt2sql`

### Comments

```gflow
// Single-line comment

/* Multi-line
   comment */

flow my-workflow v1.0 {
  // This is a comment
  from http("/webhook") { port: 8080 }
  | json.parse(payload)  // Inline comment
  | console()
}
```

### Minimal Example

```gflow
flow hello-world v1.0 {
  from http("/webhook") { port: 8080 }
  | json.parse(payload)
  | console()
}
```

---

## Input Block

The `from` keyword defines where messages originate.

### Syntax

```gflow
from <type>("<url_or_path>") {
  <config_key>: <value>
  ...
}
```

### HTTP Webhook Input

Receives HTTP POST requests.

```gflow
from http("/webhook") {
  port: 8080
}
```

**Config Options:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `port` | number | 8080 | HTTP listener port |

**Supported Content Types:**
- `application/json` - Parsed as JSON payload
- `application/octet-stream` - Raw bytes stored in metadata

### MQTT Subscriber Input

Subscribes to MQTT topics.

```gflow
from mqtt("mqtt://broker.local:1883") {
  topics: ["sensors/+/data", "devices/#"]
  qos: 1
  client_id: "gluey-client-001"
}
```

**URL Schemes:**
- `mqtt://` - Plain MQTT connection
- `mqtts://` - MQTT over TLS

**Config Options:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `topics` | array | required | Topic patterns to subscribe |
| `qos` | number | 0 | Quality of Service (0, 1, or 2) |
| `client_id` | string | auto | MQTT client identifier |
| `username` | string | - | Authentication username |
| `password` | string | - | Authentication password |
| `tls` | boolean | false | Enable TLS (also enabled by `mqtts://`) |

**Topic Wildcards:**
- `+` matches a single level: `sensors/+/temp` matches `sensors/1/temp`
- `#` matches all remaining levels: `devices/#` matches `devices/a/b/c`

---

## Transform Syntax

Transforms process messages in the pipeline. Each transform is prefixed with `|`.

### General Forms

```gflow
// Function-style with argument
| transform_name(argument)

// Block-style with config
| transform_name {
    key: value
  }

// Combined
| transform_name(argument) {
    key: value
  }
```

### json.parse

Parses JSON from the message payload or raw bytes.

```gflow
| json.parse(payload)
```

**Behavior:**
- Parses `raw_bytes` from metadata (binary HTTP input)
- Parses `data` field if payload is wrapped
- Passes through valid JSON objects/arrays
- Drops malformed messages (returns null)

**Error Handling:**

```gflow
| json.parse(payload) {
    on_error: skip
  }
```

See [Error Handling](#error-handling) for details.

### filter

Filters messages based on a condition expression.

```gflow
| filter(temperature > 25)
| filter(device_type == "outdoor" && battery_level > 10)
```

**Behavior:**
- Returns message if condition is `true`
- Drops message (returns null) if condition is `false`
- Drops message on evaluation error

See [Expression Syntax](#expression-syntax) for condition operators.

### transform

Creates a new payload from field mappings.

```gflow
| transform {
    device_id: device_id
    temp_fahrenheit: temperature * 9 / 5 + 32
    alert_level: temperature > 30 ? "high" : "normal"
    processed_at: now()
  }
```

**Mapping Types:**

| Type | Example | Description |
|------|---------|-------------|
| Field copy | `device_id: device_id` | Copy field value |
| Nested access | `lat: device.location.lat` | Access nested field |
| Arithmetic | `temp_f: temp * 1.8 + 32` | Mathematical expression |
| Ternary | `level: x > 10 ? "high" : "low"` | Conditional value |
| Function | `ts: now()` | Built-in function call |

**Built-in Functions:**

| Function | Returns | Description |
|----------|---------|-------------|
| `now()` | string | ISO 8601 timestamp |
| `uuid()` | string | Random UUID |
| `timestamp()` | number | Unix timestamp (milliseconds) |

### decode.binary

Decodes binary payloads using field definitions.

```gflow
| decode.binary {
    format: [
      { name: "device_id", offset: 0, length: 4, type: "string" },
      { name: "temperature", offset: 4, length: 2, type: "int16_be" },
      { name: "humidity", offset: 6, length: 2, type: "uint16_le" },
      { name: "battery", offset: 8, length: 1, type: "uint8" }
    ]
  }
```

**Field Definition:**

| Property | Type | Description |
|----------|------|-------------|
| `name` | string | Output field name |
| `offset` | number | Byte offset (0-indexed) |
| `length` | number | Number of bytes |
| `type` | string | Data type (see below) |

**Supported Types:**

| Type | Size | Description |
|------|------|-------------|
| `string`, `ascii` | variable | ASCII/UTF-8 string |
| `uint8` | 1 | Unsigned 8-bit integer |
| `int16_be` | 2 | Signed 16-bit, big-endian |
| `int16_le` | 2 | Signed 16-bit, little-endian |
| `uint16_be` | 2 | Unsigned 16-bit, big-endian |
| `uint16_le` | 2 | Unsigned 16-bit, little-endian |
| `int32_be` | 4 | Signed 32-bit, big-endian |
| `int32_le` | 4 | Signed 32-bit, little-endian |
| `uint32_be` | 4 | Unsigned 32-bit, big-endian |
| `uint32_le` | 4 | Unsigned 32-bit, little-endian |
| `float32_be` | 4 | IEEE 754 float, big-endian |
| `float32_le` | 4 | IEEE 754 float, little-endian |

### decode.base64

Decodes base64 strings to raw bytes.

```gflow
| decode.base64 {
    field: "frm_payload"
  }
```

**Config Options:**

| Option | Type | Description |
|--------|------|-------------|
| `field` | string | Source field (optional, auto-detects common fields) |
| `format` | array | Binary format to apply after decoding |

**Auto-detected Fields:** `frm_payload`, `payload`, `data`, `base64`

**With Binary Decoding:**

```gflow
| decode.base64 {
    field: "frm_payload"
    format: [
      { name: "temp", offset: 0, length: 2, type: "int16_be" },
      { name: "humidity", offset: 2, length: 1, type: "uint8" }
    ]
  }
```

### decode.hex

Decodes hexadecimal strings to raw bytes.

```gflow
| decode.hex {
    field: "hex_payload"
  }
```

**Config Options:**

| Option | Type | Description |
|--------|------|-------------|
| `field` | string | Source field (optional, auto-detects) |
| `format` | array | Binary format to apply after decoding |

**Auto-detected Fields:** `hex_payload`, `hex`, `hex_data`, `payload`, `data`

**Supported Formats:**
- `"41424344"` - Standard hex
- `"0x41424344"` - With 0x prefix
- `"41 42 43 44"` - Space-separated

### route

Routes messages to different outputs based on conditions.

```gflow
| route {
    critical_alert: temperature > 80 || pressure > 140
    warning: temperature > 60
    normal: *
  }
```

See [Route Block](#route-block) for complete documentation.

---

## Output Block

Outputs send processed messages to destinations.

### Syntax

```gflow
// As final pipeline step
| <output_type>("<url>") {
    <config>
  }

// As route destination
route_name -> <output_type>("<url>") {
    <config>
  }
```

### console

Prints messages to stdout with timestamps.

```gflow
| console()
```

**Output Format:**
```
[2026-01-29T10:30:45.123+00:00] {
  "device_id": "sensor-001",
  "temperature": 25.5
}
```

### http

Sends messages via HTTP POST.

```gflow
| http("https://api.example.com/webhook")
```

**Behavior:**
- POSTs payload as JSON
- Sets `Content-Type: application/json`
- Fails on HTTP 4xx/5xx responses

### mqtt

Publishes messages to MQTT topics.

```gflow
| mqtt("mqtt://broker.local:1883") {
    topic: "processed/{{device_id}}/data"
    qos: 1
    retain: false
  }
```

**Config Options:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `topic` | string | required | Publish topic (supports interpolation) |
| `qos` | number | 0 | Quality of Service |
| `retain` | boolean | false | Retain message flag |
| `client_id` | string | auto | MQTT client identifier |
| `username` | string | - | Authentication username |
| `password` | string | - | Authentication password |
| `tls` | boolean | false | Enable TLS |

**Topic Interpolation:**

Use `{{field}}` to insert payload values into the topic:
- `"devices/{{device_id}}/telemetry"` - Simple field
- `"{{device.location.zone}}/data"` - Nested field

### sql

Inserts messages into SQL databases.

```gflow
| sql("Host=localhost;Port=5432;Database=iot;Username=app;Password=secret") {
    table: "sensor_readings"
    columns: {
      device_id: "device_id",
      temperature: "temperature",
      recorded_at: "processed_at"
    }
  }
```

**Supported Databases:**
- PostgreSQL (connection strings with `Host=`)
- SQL Server (connection strings with `Server=` or `Data Source=`)

**Config Options:**

| Option | Type | Description |
|--------|------|-------------|
| `table` | string | Target table name |
| `columns` | object | Column-to-field mapping |

**Column Mapping:**

```gflow
columns: {
  <sql_column>: "<payload_field>"
}
```

The column name is the SQL column; the value is the JSON field path.

---

## Route Block

Routing enables conditional message delivery to different outputs.

### Syntax

```gflow
flow my-workflow v1.0 {
  from <input>

  | <transforms>
  | route {
      <route_name>: <condition>
      <route_name>: <condition>
      <fallback_name>: *
    }

  <route_name> -> <output>
  <route_name> -> <output>
}
```

### Route Conditions

Conditions are expressions evaluated against the message payload.

```gflow
| route {
    critical: temperature > 80 || pressure > 140
    warning: temperature > 60 && temperature <= 80
    normal: *
  }
```

**Evaluation Order:**
1. Conditions are evaluated in order
2. First matching condition wins
3. Use `*` as catch-all (matches everything)

### Route Destinations

After the route block, define where each route sends messages.

```gflow
// Single output
critical -> mqtt("mqtt://broker:1883") { topic: "alerts/critical" }

// With config block
normal -> sql("Host=localhost;Database=iot") {
    table: "readings"
    columns: { device_id: "device_id", temp: "temperature" }
  }
```

### Complete Example

```gflow
flow industrial-pipeline v1.0 {
  from mqtt("mqtt://localhost:1883") {
    topics: ["sensors/+/telemetry"]
  }

  | json.parse(payload)
  | transform {
      device_id: device_id
      temperature: temperature
      alert_level: temperature > 80 ? "critical" : "normal"
    }
  | route {
      critical_alert: alert_level == "critical"
      normal_data: *
    }

  critical_alert -> mqtt("mqtt://localhost:1883") {
    topic: "alerts/critical"
    qos: 1
  }

  normal_data -> sql("Host=localhost;Database=iot") {
    table: "sensor_readings"
    columns: {
      device_id: "device_id",
      temperature: "temperature"
    }
  }
}
```

---

## Error Handling

Transforms can specify behavior when processing fails.

### Syntax

```gflow
| transform_name(args) {
    on_error: <action>
  }
```

### Actions

**skip** - Drop the message (default)

```gflow
| json.parse(payload) {
    on_error: skip
  }
```

**route_to** - Route to error handler

```gflow
| json.parse(payload) {
    on_error: { action: "route_to", target: "parse_errors" }
  }
```

Then define an error route destination:

```gflow
parse_errors -> console()
```

### Default Behavior

Most transforms drop messages on error by default:
- `json.parse` - Drops on invalid JSON
- `filter` - Drops on evaluation error
- `transform` - Drops on expression error
- `decode.*` - Drops on decode error

---

## Expression Syntax

Expressions are used in `filter`, `transform`, and `route` conditions.

### Field Access

```gflow
// Simple field
temperature

// Nested field
device.location.latitude

// Example in filter
| filter(device.status == "active")
```

### Metadata Access

Access message metadata (like MQTT topic) using the `$meta` prefix:

```gflow
// Access MQTT topic
$meta.topic

// Access any metadata field
$meta.source
$meta.qos
```

**Available Metadata Fields (from MQTT input):**

| Field | Description |
|-------|-------------|
| `topic` | The MQTT topic the message was received on |
| `source` | Always "mqtt" for MQTT messages |
| `qos` | Quality of Service level (0, 1, or 2) |
| `retain` | Whether the message was retained |

**Example: Extract device ID from MQTT topic**

```gflow
flow topic-extract v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["devices/+/telemetry"]
  }

  | json.parse(payload)
  | transform {
      // Topic is "devices/sensor-001/telemetry"
      // Extract "sensor-001" using split
      device_id: $meta.topic.split('/')[1]
      temperature: temperature
    }
  | console()
}
```

### String Functions

String methods can be called on string values (payload fields or metadata):

```gflow
// Split a string and get element by index
$meta.topic.split('/')[1]
device_name.split('-')[0]

// Get substring
serial_number.substring(0, 4)

// Find index of substring
topic.indexOf('/')
```

**Available String Functions:**

| Function | Description | Example |
|----------|-------------|---------|
| `split(delimiter)` | Split string into array | `$meta.topic.split('/')` |
| `substring(start)` | Get substring from index | `name.substring(5)` |
| `substring(start, length)` | Get substring with length | `serial.substring(0, 4)` |
| `indexOf(search)` | Find index of substring (-1 if not found) | `path.indexOf('/')` |
| `toLower()` | Convert to lowercase | `name.toLower()` |
| `toUpper()` | Convert to uppercase | `code.toUpper()` |
| `trim()` | Remove leading/trailing whitespace | `input.trim()` |

**Array Indexing:**

After `split()`, use `[index]` to access array elements:

```gflow
// Topic: "devices/sensor-001/telemetry"
$meta.topic.split('/')[0]  // "devices"
$meta.topic.split('/')[1]  // "sensor-001"
$meta.topic.split('/')[2]  // "telemetry"
```

### Literals

| Type | Examples |
|------|----------|
| Number | `42`, `3.14`, `-10`, `0xFF` |
| String | `"hello"`, `'world'` |
| Boolean | `true`, `false` |
| Null | `null` |

### Comparison Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `==` | Equal | `status == "active"` |
| `!=` | Not equal | `type != "test"` |
| `>` | Greater than | `temperature > 25` |
| `<` | Less than | `battery < 20` |
| `>=` | Greater or equal | `count >= 10` |
| `<=` | Less or equal | `pressure <= 100` |

### Logical Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `&&` | Logical AND | `temp > 20 && humidity < 80` |
| `\|\|` | Logical OR | `status == "error" \|\| battery < 5` |
| `!` | Logical NOT | `!is_active` |

### Arithmetic Operators

| Operator | Description | Example |
|----------|-------------|---------|
| `+` | Addition | `base + offset` |
| `-` | Subtraction | `total - discount` |
| `*` | Multiplication | `price * quantity` |
| `/` | Division | `celsius * 9 / 5 + 32` |

### Ternary Operator

```gflow
condition ? value_if_true : value_if_false
```

Examples:
```gflow
// In transform
alert_level: temperature > 30 ? "high" : "normal"

// Nested ternary
priority: temp > 80 ? "critical" : temp > 60 ? "warning" : "normal"
```

### Parentheses

Use parentheses to control evaluation order:

```gflow
| filter((temperature > 30 || humidity > 80) && device_type == "outdoor")
```

### Operator Precedence

From highest to lowest:
1. `()` - Parentheses
2. `!` - Logical NOT
3. `*`, `/` - Multiplication, division
4. `+`, `-` - Addition, subtraction
5. `>`, `<`, `>=`, `<=` - Comparisons
6. `==`, `!=` - Equality
7. `&&` - Logical AND
8. `||` - Logical OR
9. `? :` - Ternary

---

## Next Steps

- [Getting Started](getting-started.md) - Run your first workflow
- [Plugins Reference](plugins.md) - Detailed plugin documentation
- [Sample Workflows](../samples/) - Learn by example

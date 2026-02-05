# GlueyHub DSL Documentation
Rebels Software sp. z o.o.

## Table of Contents
1. [Introduction](#introduction)
2. [Basic Workflow Structure](#basic-workflow-structure)
3. [Input Sources](#input-sources)
4. [Transform Operations](#transform-operations)
5. [Routing & Conditions](#routing--conditions)
6. [Output Destinations](#output-destinations)
7. [Schema Definitions](#schema-definitions)
8. [Error Handling](#error-handling)
9. [Complete Examples](#complete-examples)
10. [CLI Usage](#cli-usage)

---

## Introduction

GlueyFlow (`.gflow`) is a domain-specific language designed for IoT message routing and transformation. It provides a clean, pipeline-style syntax for connecting various protocols, transforming data, and routing messages to multiple destinations.

**Key Features:**
- Pipeline-style data flow
- Schema-aware message processing
- Multi-protocol input/output support
- Built-in IoT protocol handling
- Simple routing logic

---

## Basic Workflow Structure

Every `.gflow` file starts with a workflow definition:

```gflow
flow workflow-name v1.0 {
  // Workflow content goes here
}
```

### Minimal Example

```gflow
flow hello-world v1.0 {
  from http("/webhook")
  | json.parse(payload)
  | kafka("output-topic")
}
```

This simple flow:
1. Receives HTTP POST requests at `/webhook`
2. Parses JSON from the request payload
3. Sends the parsed data to a Kafka topic

---

## Input Sources

Input sources define where data comes from. Use the `from` keyword to specify sources.

### HTTP Webhook

```gflow
flow http-example v1.0 {
  from http("/api/sensors") {
    methods: [POST, PUT]
    port: 8080
  }
  
  | json.parse(payload)
  | kafka("sensor-data")
}
```

### MQTT Subscriber

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

### Kafka Consumer

```gflow
flow kafka-input v1.0 {
  from kafka("input-topic") {
    brokers: ["kafka1:9092", "kafka2:9092"]
    group_id: "gluey-processors"
    offset: "earliest"
  }
  
  | json.parse(payload)
  | influxdb("metrics")
}
```

### Multiple Input Sources

```gflow
flow multi-input v1.0 {
  from [
    mqtt("mqtt://edge1.local:1883").topics(["sensors/+"]),
    http("/webhook").methods([POST]),
    kafka("raw-data").group("processors")
  ]
  
  | json.parse(payload)
  | kafka("unified-stream")
}
```

---

## Transform Operations

Transform operations modify, filter, or enrich data as it flows through the pipeline.

### JSON Parsing

```gflow
flow json-parsing v1.0 {
  from http("/data")
  
  // Parse JSON from payload field
  | json.parse(payload)
  
  // Parse JSON from custom field
  | json.parse(message_body)
  
  | kafka("parsed-data")
}
```

### Data Filtering

```gflow
flow filtering v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/temp"])
  | json.parse(payload)
  
  // Simple condition
  | filter(temperature > 25)
  
  // Multiple conditions
  | filter(temperature > 25 && device_type == "outdoor")
  
  | kafka("filtered-data")
}
```

### Field Transformation

```gflow
flow transform-fields v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  | json.parse(payload)
  
  | transform {
      // Extract device ID from MQTT topic
      device_id: topic.split("/")[1]
      
      // Convert temperature units
      temp_fahrenheit: temperature * 9/5 + 32
      
      // Add timestamp
      processed_at: now()
      
      // Conditional field
      alert_level: temperature > 35 ? "high" : "normal"
      
      // Keep original fields
      temperature: temperature
      humidity: humidity
    }
  
  | kafka("transformed-data")
}
```

### Binary and Hex Payload Decoding

```gflow
flow binary-decoding v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/raw"])
  
  // Decode binary payload using custom format
  | decode.binary(payload) {
      format: [
        device_id: bytes(0, 4, "string"),        // First 4 bytes as string
        temperature: bytes(4, 2, "int16_be"),    // Next 2 bytes as big-endian int16
        humidity: bytes(6, 2, "uint16_le"),      // Next 2 bytes as little-endian uint16  
        battery: bytes(8, 1, "uint8"),           // 1 byte as uint8
        status: bytes(9, 1, "uint8"),            // 1 byte status flags
        checksum: bytes(10, 2, "uint16_be")      // 2 byte checksum
      ]
    }
  
  | transform {
      // Convert raw values to meaningful data
      temperature_celsius: temperature / 100.0   // Divide by 100 for decimal
      humidity_percent: humidity / 100.0
      battery_percent: battery
      is_online: (status & 0x01) != 0           // Check bit 0
      is_low_battery: (status & 0x02) != 0      // Check bit 1
      timestamp: now()
    }
  
  | kafka("decoded-sensor-data")
}
```

### Hexadecimal String Decoding

```gflow
flow hex-string-decoding v1.0 {
  from http("/webhook")  // Receives hex strings in JSON
  | json.parse(payload)
  
  // Decode hex string payload
  | decode.hex(hex_payload) {
      format: [
        // Example hex: "41424344001E00C8FF64A5B2"
        device_type: hex_bytes(0, 4, "ascii"),    // "ABCD" 
        device_id: hex_bytes(4, 2, "uint16_be"),  // 0x001E = 30
        temperature: hex_bytes(6, 2, "int16_be"), // 0x00C8 = 200 (2.00°C)
        humidity: hex_bytes(8, 2, "uint16_be"),   // 0xFF64 = 65380
        battery: hex_bytes(10, 1, "uint8"),       // 0xA5 = 165
        crc: hex_bytes(11, 1, "uint8")            // 0xB2 = 178
      ]
    }
  
  | transform {
      device_id: device_id
      device_type: device_type
      temperature_celsius: temperature / 100.0    // Convert to decimal
      humidity_percent: humidity / 655.35         // Convert to percentage  
      battery_voltage: battery * 0.02             // Convert to volts
      received_at: now()
      
      // Validate checksum (simple example)
      checksum_valid: crc == ((device_id + temperature + humidity + battery) & 0xFF)
    }
  
  | filter(checksum_valid == true)  // Only process valid messages
  | kafka("validated-sensor-data")
}
```

### Advanced Binary Protocol Decoding

```gflow
flow custom-protocol v1.0 {
  from mqtt("mqtt://industrial.local:1883").topics(["machines/+/telemetry"])
  
  // Handle different binary message types based on header
  | decode.binary(payload) {
      // First, decode the header to determine message type
      header: {
        magic: bytes(0, 2, "uint16_be"),        // Magic number 0xABCD
        version: bytes(2, 1, "uint8"),          // Protocol version
        msg_type: bytes(3, 1, "uint8"),         // Message type (1=status, 2=telemetry)
        length: bytes(4, 2, "uint16_be")        // Payload length
      }
    }
  
  // Route based on message type for different decoding
  | route {
      status_msg: header.msg_type == 1
      telemetry_msg: header.msg_type == 2
      unknown_msg: *
    }
  
  // Status message format  
  status_msg -> decode.binary(payload) {
    format: [
      // Skip 6 byte header, decode status payload
      machine_id: bytes(6, 4, "string"),
      status_code: bytes(10, 1, "uint8"),
      error_flags: bytes(11, 2, "uint16_be"),
      uptime: bytes(13, 4, "uint32_be")         // Seconds since boot
    ]
  } | transform {
      message_type: "status"
      machine_id: machine_id
      is_healthy: status_code == 0
      has_errors: error_flags != 0
      uptime_hours: uptime / 3600.0
      timestamp: now()
    } | kafka("machine-status")
  
  // Telemetry message format
  telemetry_msg -> decode.binary(payload) {
    format: [
      machine_id: bytes(6, 4, "string"),
      temperature: bytes(10, 2, "int16_be"),    // Temperature * 10
      pressure: bytes(12, 4, "uint32_be"),      // Pressure in Pa
      vibration_x: bytes(16, 2, "int16_be"),    // Vibration in mg
      vibration_y: bytes(18, 2, "int16_be"),
      vibration_z: bytes(20, 2, "int16_be"),
      rpm: bytes(22, 2, "uint16_be")            // Rotations per minute
    ]
  } | transform {
      message_type: "telemetry"
      machine_id: machine_id
      temperature_celsius: temperature / 10.0
      pressure_bar: pressure / 100000.0        // Convert Pa to bar
      vibration: {
        x: vibration_x,
        y: vibration_y, 
        z: vibration_z,
        magnitude: sqrt(vibration_x^2 + vibration_y^2 + vibration_z^2)
      }
      rpm: rpm
      timestamp: now()
    } | kafka("machine-telemetry")
  
  // Unknown message types go to error queue
  unknown_msg -> kafka("decode-errors")
}
```

### LoRaWAN Payload Decoding

```gflow
flow lorawan-decoding v1.0 {
  from http("/lorawan/uplink")  // TTN/Chirpstack webhook
  | json.parse(payload)
  
  // Extract LoRaWAN-specific fields and decode payload
  | transform {
      device_eui: end_device_ids.device_id
      gateway_id: uplink_message.rx_metadata[0].gateway_ids.gateway_id
      rssi: uplink_message.rx_metadata[0].rssi
      snr: uplink_message.rx_metadata[0].snr
      raw_payload: uplink_message.frm_payload  // Base64 encoded
      fport: uplink_message.f_port
    }
  
  // Decode base64 payload to hex then to binary
  | decode.base64(raw_payload)  
  | decode.binary(decoded_bytes) {
      format: [
        // Cayenne LPP format example
        channel_1: bytes(0, 1, "uint8"),         // Channel ID
        type_1: bytes(1, 1, "uint8"),            // Data type (103 = temp)
        temp_raw: bytes(2, 2, "int16_be"),       // Temperature * 10
        channel_2: bytes(4, 1, "uint8"),         // Channel ID  
        type_2: bytes(5, 1, "uint8"),            // Data type (104 = humidity)
        humidity_raw: bytes(6, 1, "uint8")       // Humidity * 2
      ]
    }
  
  | transform {
      device_id: device_eui
      gateway: gateway_id
      rssi: rssi
      snr: snr
      fport: fport
      
      // Decode Cayenne LPP values
      temperature: temp_raw / 10.0    // Convert to Celsius
      humidity: humidity_raw / 2.0    // Convert to percentage
      
      // Add LoRaWAN metadata
      lora_metadata: {
        spreading_factor: uplink_message.settings.data_rate.lora.spreading_factor,
        bandwidth: uplink_message.settings.data_rate.lora.bandwidth,
        frequency: uplink_message.settings.frequency
      }
      
      received_at: now()
    }
  
  | kafka("lorawan-sensor-data")
}
```

### Data Enrichment

```gflow
flow enrichment v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  | json.parse(payload)
  
  | enrich {
      // Add static fields
      facility: "factory-1"
      
      // Lookup from external source (planned for future)
      // location: lookup.redis("device:{{device_id}}:location")
      
      // Add computed fields
      message_id: uuid()
      batch_id: hash(device_id + timestamp)
    }
  
  | kafka("enriched-data")
}
```

---

## Routing & Conditions

Routing allows you to send messages to different destinations based on conditions.

### Simple Routing

```gflow
flow basic-routing v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/temp"])
  | json.parse(payload)
  
  | route {
      high_temp: temperature > 35
      normal_temp: temperature <= 35
    }
  
  high_temp -> kafka("alerts")
  normal_temp -> influxdb("metrics")
}
```

### Multiple Conditions

```gflow
flow complex-routing v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  | json.parse(payload)
  
  | route {
      critical: temperature > 80 || battery_level < 5
      warning: temperature > 65 && temperature <= 80
      maintenance: device_age > 365 && error_count > 10
      normal: *  // catches everything else
    }
  
  critical -> [
    kafka("alerts.critical"),
    http("https://alerts.company.com/webhook")
  ]
  
  warning -> kafka("alerts.warning")
  maintenance -> kafka("maintenance.queue")
  normal -> influxdb("sensor_readings")
}
```

---

## Output Destinations

Output destinations define where processed data goes.

### Kafka Producer

```gflow
flow kafka-output v1.0 {
  from http("/webhook")
  | json.parse(payload)
  
  | kafka("output-topic") {
      brokers: ["kafka1:9092", "kafka2:9092"]
      compression: gzip
      partition_by: device_id
    }
}
```

### MQTT Publisher

```gflow
flow mqtt-output v1.0 {
  from kafka("sensor-data")
  | json.parse(payload)
  
  | mqtt("mqtt://broker.local:1883") {
      topic: "processed/{{device_id}}/data"
      qos: 1
      retain: true
    }
}
```

### HTTP Webhook

```gflow
flow webhook-output v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["alerts/+"])
  | json.parse(payload)
  
  | http("https://api.external.com/alerts") {
      method: POST
      headers: {
        "Content-Type": "application/json",
        "Authorization": "Bearer {{env.API_TOKEN}}"
      }
      timeout: 5s
      retry: 3
    }
}
```

### Multiple Outputs

```gflow
flow fan-out v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  | json.parse(payload)
  | transform {
      device_id: topic.split("/")[1]
      processed_at: now()
    }
  
  | [
      kafka("raw-archive"),
      influxdb("time-series") {
        measurement: "sensor_data"
        tags: ["device_id", "sensor_type"]
      },
      mqtt("mqtt://edge.local:1883").topic("processed/{{device_id}}")
    ]
}
```

---

## Schema Definitions

Schemas provide structure validation and enable automatic data migration.

### Basic Schema

```gflow
schema sensor_data v1.0 {
  device_id: string required
  temperature: float range(-50, 150)
  humidity: float range(0, 100)
  timestamp: datetime required
  battery_level: int range(0, 100)
}

flow with-schema v1.0 {
  schema: sensor_data v1.0
  
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  | json.parse(payload)
  | validate_schema()  // Validates against sensor_data v1.0
  | kafka("validated-data")
}
```

### Schema Evolution

```gflow
// Original schema
schema device_status v1.0 {
  device_id: string required
  temperature: float
  timestamp: datetime required
}

// Evolved schema  
schema device_status v2.0 {
  device_id: string required
  temperature: float
  humidity: float        // NEW FIELD
  location: geo_point    // NEW FIELD
  timestamp: datetime required
}

// Migration rule
migrate device_status v1.0 -> v2.0 {
  humidity: null
  location: null
}

flow schema-migration v1.0 {
  schema: device_status v2.0
  
  from mqtt("mqtt://broker.local:1883").topics(["devices/+/status"])
  | json.parse(payload)
  | migrate_schema()     // Auto-migrates v1.0 -> v2.0 if needed
  | validate_schema()
  | kafka("device-status-v2")
}
```

---

## Error Handling

Handle errors gracefully with built-in error handling constructs.

### Basic Error Handling

```gflow
flow error-handling v1.0 {
  from mqtt("mqtt://broker.local:1883").topics(["sensors/+/data"])
  
  | json.parse(payload) {
      on_error: skip  // Skip malformed JSON
    }
  
  | validate_schema() {
      on_error: route_to("invalid_data")
    }
  
  | kafka("processed-data")
  
  // Error route
  invalid_data -> kafka("error-queue")
}
```

### Advanced Error Handling

```gflow
flow robust-processing v1.0 {
  from kafka("raw-sensor-data")
  
  | json.parse(payload) {
      on_error: {
        retry: 3
        fallback: route_to("unparseable")
      }
    }
  
  | http("https://api.weather.com/enrich") {
      timeout: 2s
      on_error: {
        retry: 2
        fallback: skip_enrichment()
      }
    }
  
  | kafka("enriched-data")
  
  unparseable -> kafka("parse-errors")
}
```

---

## Complete Examples

### IoT Sensor Data Pipeline

```gflow
// Schema definition
schema sensor_reading v1.0 {
  device_id: string required
  temperature: float range(-40, 85)
  humidity: float range(0, 100)
  battery_level: int range(0, 100)
  timestamp: datetime required
}

// Main processing workflow
flow iot-pipeline v1.0 {
  description: "Process IoT sensor data with alerts and archiving"
  schema: sensor_reading v1.0
  
  from mqtt("mqtt://iot.company.local:1883") {
    topics: [
      "sensors/temperature/+",
      "sensors/environmental/+"
    ]
    qos: 1
    client_id: "gluey-iot-processor"
  }
  
  // Parse and validate incoming data
  | json.parse(payload) {
      on_error: route_to("parse_errors")
    }
  | validate_schema() {
      on_error: route_to("schema_errors")
    }
  
  // Add metadata
  | transform {
      device_id: topic.split("/")[2]
      facility: "factory-north"
      processed_at: now()
      alert_level: temperature > 30 ? "high" : "normal"
    }
  
  // Route based on conditions
  | route {
      critical_temp: temperature > 40
      low_battery: battery_level < 20
      high_temp_warning: temperature > 30 && temperature <= 40
      normal_operation: *
    }
  
  // Output routing
  critical_temp -> [
    kafka("alerts.critical") {
      partition_by: device_id
      compression: gzip
    },
    http("https://alerts.company.com/critical") {
      method: POST
      timeout: 5s
      retry: 2
    }
  ]
  
  low_battery -> kafka("maintenance.battery")
  high_temp_warning -> kafka("alerts.warning")
  
  normal_operation -> [
    influxdb("sensor_metrics") {
      measurement: "temperature_readings"
      tags: ["device_id", "facility"]
    },
    kafka("archive.sensor_data") {
      compression: snappy
      batch_size: 1000
    }
  ]
  
  // Error handling
  parse_errors -> kafka("errors.parse")
  schema_errors -> kafka("errors.schema")
}
```

### Multi-Protocol Gateway

```gflow
flow protocol-gateway v1.0 {
  description: "Convert between different IoT protocols"
  
  from [
    // Legacy systems
    http("/legacy/webhook").methods([POST]),
    
    // Modern MQTT devices  
    mqtt("mqtt://edge.local:1883").topics(["modern/+/data"]),
    
    // Kafka stream from cloud
    kafka("cloud-data").group("gateway")
  ]
  
  | json.parse(payload) {
      on_error: route_to("format_errors")
    }
  
  // Normalize data format
  | transform {
      device_id: coalesce(device_id, id, sensor_id)
      timestamp: coalesce(timestamp, ts, time)
      value: coalesce(temperature, temp, value)
      source_protocol: input.protocol  // "http", "mqtt", or "kafka"
    }
  
  | route {
      to_cloud: source_protocol != "kafka"
      to_edge: source_protocol != "mqtt" 
      to_legacy: source_protocol != "http"
    }
  
  // Fan out to different protocols
  to_cloud -> kafka("cloud-ingestion") {
    brokers: ["cloud-kafka1:9092", "cloud-kafka2:9092"]
  }
  
  to_edge -> mqtt("mqtt://edge.local:1883") {
    topic: "unified/{{device_id}}/data"
    qos: 1
  }
  
  to_legacy -> http("http://legacy-system.local/api/data") {
    method: POST
    headers: {
      "Content-Type": "application/json"
    }
  }
  
  format_errors -> kafka("errors.format")
}
```

---

## CLI Usage

### Running Workflows

```bash
# Validate a workflow file
gluey validate sensor-pipeline.gflow

# Run a workflow (development mode)
gluey run sensor-pipeline.gflow

# Deploy to cluster
gluey deploy sensor-pipeline.gflow

# List running workflows
gluey list

# Stop a workflow
gluey stop sensor-pipeline

# View workflow logs
gluey logs sensor-pipeline --follow
```

### Schema Management

```bash
# Validate schema
gluey schema validate sensor_data.schema

# Register schema
gluey schema register sensor_data.schema

# List schemas
gluey schema list

# Show schema evolution
gluey schema evolution sensor_data
```

### Development Tools

```bash
# Live debug mode (shows message flow)
gluey debug sensor-pipeline.gflow --live

# Test with sample data
gluey test sensor-pipeline.gflow --input sample-data.json

# Generate sample data for schema
gluey schema sample sensor_data v1.0
```

---

## Next Steps

This MVP version focuses on core functionality. Future versions will include:

- **Windowing & Aggregation**: Time-based and count-based windows
- **State Management**: Stateful operations and checkpointing  
- **Machine Learning**: Built-in ML inference nodes
- **Advanced Schema Evolution**: Complex migration rules
- **Plugin System**: Custom input/transform/output plugins
- **Monitoring Dashboard**: Real-time workflow visualization

---

## File Extension

Save your workflows as `.gflow` files, for example:
- `sensor-processing.gflow`
- `iot-gateway.gflow` 
- `data-pipeline.gflow`
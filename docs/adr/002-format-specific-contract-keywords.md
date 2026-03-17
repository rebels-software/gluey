# ADR-002: Format-Specific Contract Keywords

## Status

Accepted

## Context

Gluey contracts describe data shapes used across the system — API payloads, device telemetry, configuration, etc. These data shapes come in fundamentally different serialization formats: JSON, Protocol Buffers, custom binary encodings for IoT devices, XML, CSV, and others.

Each format has its own structural and validation concerns:

- **JSON** has properties with JSON Schema-style validation (required, minLength, pattern, enum)
- **Protocol Buffers** has field numbers, wire types, and oneofs
- **Custom binary** has byte offsets, endianness, and bit packing
- **XML** has elements, attributes, namespaces, and XSD

Gluey's design principle is that the first keyword in a `.gluey` file tells you exactly what the contract is. A generic keyword would hide the format and force the reader to inspect the body.

## Decision

### 1. Each serialization format is its own contract keyword

The keyword identifies both the contract type and the serialization format. No separate `format:` field is needed.

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
      description: { "en-US": "UTC timestamp when the telemetry was recorded" }
    }
  }
}
```

JSON contracts support JSON Schema-style validation: `required`, `minLength`, `maxLength`, `pattern`, `enum`, `minimum`, `maximum`.

### 2. Format keywords

| Keyword | Format | Validation concerns |
|---------|--------|-------------------|
| `json` | JSON | Properties, JSON Schema validation rules |
| `protobuf` | Protocol Buffers | Field numbers, wire types, oneofs |
| `binary` | Custom binary encoding | Byte offsets, endianness, bit packing |
| `xml` | XML | Elements, attributes, namespaces, XSD |
| `csv` | CSV | Column positions, delimiters, quoting |

Each keyword defines its own structure and validation rules appropriate to the format. New formats are added as new keywords — no changes to existing contracts.

### 3. Plugin-specific data types are separate

Contract types that belong to a plugin (like `http-headers`, `http-response`) use plugin-prefixed keywords. These are protocol-specific structures, not serialization formats.

| Keyword | Owned by | Reason |
|---------|----------|--------|
| `json` | Gluey core | Serialization format |
| `protobuf` | Gluey core | Serialization format |
| `binary` | Gluey core | Serialization format |
| `http-headers` | HTTP plugin | Protocol-specific structure |
| `http-response` | HTTP plugin | Protocol-specific wrapper |

### 4. All properties reference named, versioned types

All format contracts reference Gluey's logical type system (`text 1.0.0`, `int32 1.0.0`, `timestamp 1.0.0`, etc.). The format keyword determines how those logical types are serialized — JSON serializes `text` as a JSON string, `protobuf` assigns it a field number and wire type, `binary` maps it to specific bytes.

## Consequences

- The first keyword in a `.gluey` file fully identifies the contract type and format — no need to read the body
- Each format has its own validation model tailored to its concerns
- Adding a new format means adding a new keyword and its validation rules — no impact on existing format contracts
- Plugin-specific structures remain under plugin ownership, keeping a clear separation between serialization formats and protocol concerns

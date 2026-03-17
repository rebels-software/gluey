# ADR-001: Logical Types and Runtime Mapping

## Status

Accepted

## Context

Gluey contracts describe data shapes that must work across multiple runtimes (.NET, Rust, Go, etc.). Each runtime has different in-memory representations for the same logical concept:

| Concept | .NET | Rust | Go |
|---------|------|------|----|
| Text | `System.String` (UTF-16) | `String` (UTF-8) | `string` (UTF-8) |
| 32-bit integer | `int` (little-endian) | `i32` (platform-endian) | `int32` (platform-endian) |
| Timestamp | `DateTimeOffset` | `chrono::DateTime<Utc>` | `time.Time` |
| Byte sequence | `byte[]` | `Vec<u8>` | `[]byte` |

Hardcoding memory representation (e.g., `memory: utf-16`) in type definitions couples the contract to a specific runtime, which contradicts Gluey's goal of being runtime-agnostic.

Gluey must not prescribe how the runtime holds values in memory — only what the value logically represents.

## Decision

### 1. Gluey types are logical, not physical

A `primitive` defines a logical type — what the value represents, not how it's stored in memory. The runtime is responsible for mapping the logical type to its native representation.

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

### 2. Runtime mapping is external to the type definition

Each supported runtime provides a mapping contract that translates logical types to native representations:

```gluey
runtime-mapping dotnet 1.0.0 {
  text       -> "System.String"          // UTF-16
  int8       -> "System.SByte"
  int16      -> "System.Int16"
  int32      -> "System.Int32"
  int64      -> "System.Int64"
  uint8      -> "System.Byte"
  uint16     -> "System.UInt16"
  uint32     -> "System.UInt32"
  uint64     -> "System.UInt64"
  float32    -> "System.Single"
  float64    -> "System.Double"
  bool       -> "System.Boolean"
  timestamp  -> "System.DateTimeOffset"
  bytes      -> "System.Byte[]"
  map        -> "System.Collections.Generic.Dictionary<string, string>"
}

runtime-mapping rust 1.0.0 {
  text       -> "String"                 // UTF-8
  int8       -> "i8"
  int16      -> "i16"
  int32      -> "i32"
  int64      -> "i64"
  uint8      -> "u8"
  uint16     -> "u16"
  uint32     -> "u32"
  uint64     -> "u64"
  float32    -> "f32"
  float64    -> "f64"
  bool       -> "bool"
  timestamp  -> "chrono::DateTime<Utc>"
  bytes      -> "Vec<u8>"
  map        -> "HashMap<String, String>"
}
```

### 3. Enums

An enum is a fixed-size map from a key to a text label. The key type is any Gluey integer or bool primitive. This allows the contract to describe both the wire value (what the device sends) and the human-readable meaning.

```gluey
primitive device-status 1.0.0 {
  enum: uint8 1.0.0 {
    0: "offline"
    1: "online"
    2: "maintenance"
    3: "error"
  }
  displayName: { "en-US": "Device Status" }
  description: { "en-US": "Current operational status of the device" }
}
```

The key type determines how the value is stored on the wire and in memory. A `uint8` enum uses 1 byte. A `uint16` enum uses 2 bytes. The text labels are for display and flow logic:

```gluey
| match telemetry.status {
    "online"      -> forward
    "error"       -> alert("device error: ${telemetry.device_id}")
    "maintenance" -> skip
  }
```

Runtime mapping for enums:

| Runtime | Representation |
|---------|---------------|
| .NET | `enum DeviceStatus : byte { Offline = 0, Online = 1, ... }` |
| Rust | `#[repr(u8)] enum DeviceStatus { Offline = 0, Online = 1, ... }` |
| Go | `type DeviceStatus uint8; const ( Offline DeviceStatus = 0; ... )` |

Enums with bool keys represent binary states:

```gluey
primitive alarm-active 1.0.0 {
  enum: bool 1.0.0 {
    false: "inactive"
    true: "active"
  }
  displayName: { "en-US": "Alarm Active" }
  description: { "en-US": "Whether the alarm is currently triggered" }
}
```

### 4. Flags (bitfield enums)

Flags represent a set of non-mutually-exclusive options packed into a single integer value. Each bit position maps to a named flag. Multiple flags can be active simultaneously via bitwise OR.

```gluey
primitive device-capabilities 1.0.0 {
  flags: uint8 1.0.0 {
    0: "telemetry"
    1: "commands"
    2: "ota_update"
    3: "geolocation"
    4: "battery_reporting"
  }
  displayName: { "en-US": "Device Capabilities" }
  description: { "en-US": "Bitmask of features supported by the device" }
}
```

A device reporting `0b00000111` (7) supports telemetry, commands, and OTA updates. The bit position is the index (0-based), the value is `1 << index`.

Flow logic works with named flags:

```gluey
| match telemetry.capabilities {
    has "ota_update"  -> check-firmware-version
    has "geolocation" -> enrich-with-location
  }
```

Runtime mapping for flags:

| Runtime | Representation |
|---------|---------------|
| .NET | `[Flags] enum DeviceCapabilities : byte { Telemetry = 1, Commands = 2, OtaUpdate = 4, ... }` |
| Rust | `bitflags! { struct DeviceCapabilities: u8 { const TELEMETRY = 1; const COMMANDS = 2; ... } }` |
| Go | `type DeviceCapabilities uint8; const ( Telemetry DeviceCapabilities = 1 << iota; Commands; ... )` |

The backing integer type determines the maximum number of flags: `uint8` supports 8, `uint16` supports 16, `uint32` supports 32, `uint64` supports 64.

## Consequences

- Gluey contracts are fully runtime-agnostic — the same `.gluey` file works for .NET, Rust, Go, or any future runtime
- Serialization/deserialization complexity is isolated in the plugin layer, not in the contract
- Adding a new runtime requires only a new `runtime-mapping` contract, no changes to existing types or data contracts
- Enums and flags are first-class citizens with explicit wire representation, enabling Gluey to validate values and generate correct code for any target runtime
- Custom wire formats (IoT binary encodings, etc.) are a separate concern to be addressed in a dedicated ADR

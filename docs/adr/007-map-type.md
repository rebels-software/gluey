# ADR-007: Map Type with Typed Keys and Values

## Status

Accepted

## Context

Maps (dictionaries, associative arrays) are a fundamental data structure. Gluey needs to describe map properties in contracts — HTTP headers, user-defined labels, device settings, configuration registries.

A map has two type dimensions: the key and the value. Both must be explicitly typed and versioned. Additionally, key comparison semantics vary by use case — HTTP header names are case-insensitive per RFC 9110, while user-defined labels are case-sensitive.

## Decision

### 1. Map properties declare key and value types

A `map` property specifies its key and value types inline using `key:` and `value:` attributes. Both are versioned type references.

```gluey
labels: map 1.0.0 {
  required: true
  key: text 1.0.0
  value: text 1.0.0
  displayName: { "en-US": "Labels" }
  description: { "en-US": "User-defined labels" }
}
```

Values can reference any type — primitives, json contracts, or other complex types:

```gluey
device_settings: map 1.0.0 {
  required: true
  key: text 1.0.0
  value: device-setting 1.0.0
  displayName: { "en-US": "Device Settings" }
  description: { "en-US": "Per-device configuration" }
}
```

### 2. Key and value can carry attributes

The `key` and `value` blocks support attributes in curly braces. This enables key comparison strategies, `represents` on values, and other per-type configuration.

```gluey
headers: map 1.0.0 {
  required: true
  key: text 1.0.0 { comparison: "ordinal-ignore-case" }
  value: text 1.0.0
  displayName: { "en-US": "Headers" }
  description: { "en-US": "HTTP headers, keys are case-insensitive per RFC 9110" }
}

raw_configs: map 1.0.0 {
  required: true
  key: text 1.0.0
  value: text 1.0.0 { represents: json app-config 1.0.0 }
  displayName: { "en-US": "Raw Configs" }
  description: { "en-US": "Config JSONs stored as strings, keyed by app name" }
}
```

### 3. Key comparison strategies

Keys require equality comparison to enforce uniqueness. The `comparison` attribute on the key type declares how equality is determined.

| Strategy | Description |
|----------|-------------|
| `ordinal` | Byte-by-byte, case-sensitive (default) |
| `ordinal-ignore-case` | Byte-by-byte, case-insensitive |
| `linguistic` | Culture-aware |
| `linguistic-ignore-case` | Culture-aware, case-insensitive |

When no `comparison` is specified, the default is `ordinal`.

Comparison is declared on the property's key, not on the primitive itself. The same `text 1.0.0` primitive can be compared differently depending on the use case.

### 4. Allowed key types

Map keys must be types that support reliable, deterministic equality. The following primitives are allowed as key types:

| Type | Allowed | Rationale |
|------|---------|-----------|
| `text` | Yes | String equality with configurable comparison |
| `int8/16/32/64` | Yes | Numeric equality |
| `uint8/16/32/64` | Yes | Numeric equality |
| `bool` | Yes | True/false equality |
| `timestamp` | Yes | Temporal equality |
| `float32/64` | No | IEEE 754 precision issues, NaN != NaN |
| `bytes` | No | Mutable, variable length, no natural equality |
| `json` contracts | No | Deep equality is expensive and fragile |

### 5. The map primitive is generic

The `map` primitive itself does not declare key and value types. It is a structural container. The key and value types are declared at the property level, making each usage fully typed and versioned.

Both key and value type references participate in version propagation and impact analysis.

## Consequences

- Every map property is fully typed — key and value types are explicit, versioned, and traceable
- Key comparison semantics are declared per property, not per primitive, allowing the same text type to be compared differently in different contexts
- The `represents` attribute works on map values, enabling maps of embedded structured content
- Key types are constrained to those with reliable equality, preventing runtime comparison issues
- Key and value type versions participate in the dependency graph — a major bump in either affects the containing contract

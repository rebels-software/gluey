# ADR-006: Structural Representation for Embedded Contracts

## Status

Accepted

## Context

It is common practice for text and byte fields to carry structured data inside them — a JSON string embedded as an escaped string inside another JSON document, a protobuf message packed into a byte array field, an XML document stored as a text column.

These embedded payloads have their own structure, their own validation rules, and their own version. Without explicit declaration, the outer contract has no way to express what's inside — it's just `text` or `bytes` with no traceability, no validation, and no version tracking.

This is a structural concern, not a semantic one. The question is not "what does this value mean in the real world?" (that's what `displayName` and `description` are for) but "this field contains a parseable document that conforms to a specific contract."

## Decision

### 1. The `represents` attribute declares embedded structured content

The `represents` attribute is available on `text` and `bytes` properties. It declares that the field's content is a structured document in a specific format, conforming to a specific versioned contract.

```
represents: <format-keyword> <contract-name> <version>
```

The format keyword (`json`, `protobuf`, `binary`, `xml`) tells the runtime how to deserialize. The contract reference tells it what to validate against.

### 2. Applies only to text and bytes

`represents` is not available on numeric types, booleans, timestamps, or other primitives. Those types don't carry nested structured data. Their real-world meaning is expressed through `displayName` and `description`.

| Type | Can use `represents` | Rationale |
|------|---------------------|-----------|
| `text` | Yes | Can contain serialized JSON, XML, YAML, Base64 |
| `bytes` | Yes | Can contain serialized protobuf, binary, compressed data |
| `int32`, `int64`, etc. | No | Does not contain nested structure |
| `bool` | No | Does not contain nested structure |
| `timestamp` | No | Does not contain nested structure |
| `map` | No | Already typed by its own contract |

### 3. The runtime deserializes and validates

When a property has `represents`, the runtime:

1. Reads the field as its declared type (`text` or `bytes`)
2. Deserializes the content using the specified format
3. Validates the result against the referenced contract
4. Hands the flow a typed, validated object

The flow author works with the inner contract's properties directly — no manual parsing, no string manipulation.

### 4. The reference is a versioned edge in the dependency graph

A `represents` reference is a first-class dependency. It participates in version propagation and impact analysis like any other contract reference.

If the inner contract bumps its version, the outer contract is affected. If the inner contract introduces a breaking change, the outer contract must evaluate the impact.

### 5. Scope

`represents` is strictly structural. It answers "what format is inside this field and what contract does it conform to?" It does not express real-world meaning, domain semantics, or units of measurement. Those concerns are already served by `displayName`, `description`, and domain-specific `primitive` definitions.

## Consequences

- Embedded structured data is no longer opaque — Gluey knows what's inside text and byte fields
- Validation extends through nested layers — a JSON document inside a JSON string inside an MQTT payload is fully validated end to end
- The dependency graph tracks embedded contracts — version changes in inner documents surface in impact analysis
- The flow engine can deserialize embedded payloads automatically, eliminating manual parsing in flow logic
- The attribute is limited to types that can structurally contain other documents, avoiding overloaded semantics on primitives

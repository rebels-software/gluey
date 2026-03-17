# ADR-004: JSON Contract DSL

## Status

Accepted

## Context

Gluey needs a declarative language for describing JSON data shapes — API payloads, responses, event schemas, configuration. The language must be readable by humans and machines (including LLMs), support validation, versioning, localization, and composition.

JSON is the most common serialization format in Gluey's target domain: HTTP APIs, MQTT text payloads, configuration files, and inter-service messaging.

## Decision

### 1. Contract declaration

A JSON contract starts with the `json` keyword, followed by a name and semver version:

```gluey
json device-telemetry 1.0.0 {
  properties: {
    // ...
  }
}
```

One contract per file. The file extension is `.gluey`.

### 2. Property declaration

Each property declares a name, a reference to a named versioned type, and a block of attributes:

```gluey
device_id: text 1.0.0 {
  required: true
  obfuscate: false
  displayName: { "en-US": "Device ID" }
  description: { "en-US": "Unique identifier of the device" }
}
```

#### Standard attributes

Every property across all format contracts supports these attributes:

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `required` | bool | `false` | Whether the property must be present |
| `obfuscate` | bool | `false` | Whether the value is redacted in logs and payload revision |
| `displayName` | map | — | Localized display name, keyed by language code |
| `description` | map | — | Localized description, keyed by language code |

#### JSON-specific validation attributes

JSON contracts support all [JSON Schema](https://json-schema.org/understanding-json-schema/) validation attributes. The full set is available for use on properties where the underlying type is applicable.

**String validation:**

| Attribute | Description |
|-----------|-------------|
| `minLength` | Minimum string length |
| `maxLength` | Maximum string length |
| `pattern` | Regex pattern the value must match |
| `format` | Semantic format hint (e.g., `"email"`, `"uri"`, `"ipv4"`, `"date-time"`, `"uuid"`) |
| `contentEncoding` | Encoding of the string content (e.g., `"base64"`) |
| `contentMediaType` | Media type of the encoded content (e.g., `"application/json"`) |

**Numeric validation:**

| Attribute | Description |
|-----------|-------------|
| `minimum` | Minimum value (inclusive) |
| `maximum` | Maximum value (inclusive) |
| `exclusiveMinimum` | Minimum value (exclusive) |
| `exclusiveMaximum` | Maximum value (exclusive) |
| `multipleOf` | Value must be a multiple of this number |

**Array validation:**

| Attribute | Description |
|-----------|-------------|
| `minItems` | Minimum number of items |
| `maxItems` | Maximum number of items |
| `uniqueItems` | Whether all items must be unique |

**General validation:**

| Attribute | Description |
|-----------|-------------|
| `enum` | List of allowed values |
| `const` | Exact value the property must equal |
| `default` | Default value when property is absent |

```gluey
json create-server-request 1.0.0 {
  properties: {
    name: text 1.0.0 {
      required: true
      minLength: 1
      maxLength: 63
      pattern: "^[a-z0-9\\-]+$"
      displayName: { "en-US": "Server Name" }
      description: { "en-US": "Name of the server, lowercase alphanumeric and hyphens" }
    }
    email: text 1.0.0 {
      required: true
      format: "email"
      displayName: { "en-US": "Email" }
      description: { "en-US": "Contact email address" }
    }
    cpu_count: int32 1.0.0 {
      required: true
      minimum: 1
      maximum: 96
      displayName: { "en-US": "CPU Count" }
      description: { "en-US": "Number of CPU cores" }
    }
    ssh_keys: [int64 1.0.0] {
      required: false
      maxItems: 50
      uniqueItems: true
      displayName: { "en-US": "SSH Keys" }
      description: { "en-US": "SSH key IDs to deploy, no duplicates" }
    }
    start_after_create: bool 1.0.0 {
      required: false
      default: true
      displayName: { "en-US": "Start After Create" }
      description: { "en-US": "Whether to start the server after creation" }
    }
  }
}
```

### 3. Type references

Every property type is a reference to a named, versioned Gluey type. No anonymous or inline type declarations.

**Primitive types** (provided by Gluey core):

```gluey
name: text 1.0.0 { ... }
count: int32 1.0.0 { ... }
ratio: float64 1.0.0 { ... }
active: bool 1.0.0 { ... }
created_at: timestamp 1.0.0 { ... }
payload: bytes 1.0.0 { ... }
tags: map 1.0.0 { ... }
```

**Nested contracts** — a property can reference another `json` contract:

```gluey
json server-detail 1.0.0 {
  properties: {
    public_net: server-public-net 1.0.0 {
      required: true
      displayName: { "en-US": "Public Network" }
      description: { "en-US": "Public network information" }
    }
  }
}
```

**Enum types** — a property can reference a `primitive` with an enum definition:

```gluey
status: device-status 1.0.0 {
  required: true
  displayName: { "en-US": "Status" }
  description: { "en-US": "Current device status" }
}
```

### 4. Arrays

Arrays are declared with square brackets around the type reference:

```gluey
ssh_keys: [int64 1.0.0] {
  required: false
  displayName: { "en-US": "SSH Keys" }
  description: { "en-US": "SSH key IDs to deploy" }
}

rules: [firewall-rule 1.0.0] {
  required: true
  displayName: { "en-US": "Rules" }
  description: { "en-US": "List of firewall rules" }
}
```

Arrays can contain primitives, contract references, or other arrays. Nesting is supported to any depth:

```gluey
matrix: [[float64 1.0.0]] {
  required: true
  displayName: { "en-US": "Matrix" }
  description: { "en-US": "2D matrix of floating point values" }
}

cube: [[[int32 1.0.0]]] {
  required: true
  displayName: { "en-US": "Cube" }
  description: { "en-US": "3D grid of integer values" }
}
```

Array validation attributes (`minItems`, `maxItems`, `uniqueItems`) apply to the outermost array.

### 5. Localization

`displayName` and `description` are maps keyed by IETF language tags:

```gluey
displayName: { "en-US": "Device ID", "de-DE": "Geräte-ID", "ja-JP": "デバイスID" }
description: { "en-US": "Unique device identifier", "de-DE": "Eindeutiger Gerätebezeichner" }
```

These are used by:
- UI generation — form labels, tooltips
- Documentation generation
- LLM-assisted interactions — explaining contracts to users in their language
- Error messages — validation failures referencing the display name

### 6. Complete example

```gluey
json create-ssh-key-request 1.0.0 {
  properties: {
    name: text 1.0.0 {
      required: true
      minLength: 1
      maxLength: 255
      displayName: { "en-US": "Name" }
      description: { "en-US": "Name of the SSH key" }
    }
    public_key: text 1.0.0 {
      required: true
      obfuscate: true
      displayName: { "en-US": "Public Key" }
      description: { "en-US": "Public key content in OpenSSH or PEM format" }
    }
    labels: map 1.0.0 {
      required: false
      displayName: { "en-US": "Labels" }
      description: { "en-US": "User-defined key-value labels" }
    }
  }
}
```

## Consequences

- JSON contracts are self-documenting — every property carries its type, validation rules, localized descriptions, and sensitivity classification
- Validation is declarative and enforceable at runtime — the flow engine rejects payloads that violate the contract before they hit the wire
- Composition through named type references enables reuse and version tracking across the full contract tree
- LLMs can read, generate, and validate JSON contracts without external documentation
- The DSL is extensible — new validation attributes can be added without breaking existing contracts

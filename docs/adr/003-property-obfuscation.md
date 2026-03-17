# ADR-003: Property Obfuscation Flag

## Status

Accepted

## Context

Contracts describe data shapes that flow through the system. Some properties contain sensitive information — API tokens, passwords, personal data, device secrets. When Gluey logs payloads, records message revisions, or exposes data for debugging, these values must not appear in clear text.

The contract is the right place to declare this. The contract author knows which properties are sensitive at definition time. Leaving obfuscation to runtime configuration or per-deployment rules is fragile — it's easy to miss a field, and the decision gets separated from the person who understands the data.

## Decision

### 1. Every property in every format contract has an `obfuscate` flag

The `obfuscate` flag is a standard property attribute available across all format contracts (`json`, `protobuf`, `binary`, `xml`, `csv`, and plugin-specific types like `http-headers`). It defaults to `false`.

```gluey
json create-server-request 1.0.0 {
  properties: {
    name: text 1.0.0 {
      required: true
      displayName: { "en-US": "Server Name" }
      description: { "en-US": "Name of the server to create" }
    }
    user_data: text 1.0.0 {
      required: false
      obfuscate: true
      displayName: { "en-US": "User Data" }
      description: { "en-US": "Cloud-init user data, may contain secrets" }
    }
  }
}

http-headers hetzner-auth-headers 1.0.0 {
  properties: {
    Authorization: text 1.0.0 {
      required: true
      obfuscate: true
      pattern: "^Bearer .+$"
      displayName: { "en-US": "Authorization" }
      description: { "en-US": "Bearer token for Hetzner Cloud API authentication" }
    }
  }
}
```

### 2. Obfuscation applies to all output channels

When `obfuscate: true`, the runtime replaces the property value with a redacted placeholder in:

- **Logs** — flow execution logs, error logs, debug output
- **Payload revision history** — stored message snapshots for auditing or replay
- **Debug/inspect tools** — any UI or CLI that displays contract data
- **Error messages** — exception details that include payload content

The actual value is still available to the flow at runtime — obfuscation affects only observability output, not processing.

### 3. Obfuscation is inherited by nested contracts

If a property references another contract and that contract has obfuscated properties, the obfuscation carries through. A parent contract does not need to redeclare obfuscation for nested sensitive fields.

```gluey
json auth-credentials 1.0.0 {
  properties: {
    username: text 1.0.0 {
      required: true
      displayName: { "en-US": "Username" }
      description: { "en-US": "Authentication username" }
    }
    password: text 1.0.0 {
      required: true
      obfuscate: true
      displayName: { "en-US": "Password" }
      description: { "en-US": "Authentication password" }
    }
  }
}

// password is automatically obfuscated in logs — no need to redeclare
json login-request 1.0.0 {
  properties: {
    credentials: auth-credentials 1.0.0 {
      required: true
      displayName: { "en-US": "Credentials" }
      description: { "en-US": "Login credentials" }
    }
  }
}
```

## Consequences

- Sensitive data protection is declared once, at the contract level, by the person who understands the data
- All observability channels respect the flag automatically — no per-tool configuration
- The flag is format-agnostic — works identically in `json`, `protobuf`, `binary`, `xml`, `http-headers`, and any future format
- Defaults to `false` — no impact on existing contracts until explicitly opted in

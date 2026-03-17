# ADR-008: Plugin Contract

## Status

Accepted

## Context

Gluey flows don't interact with infrastructure directly. They don't open TCP connections, parse HTTP status codes, negotiate MQTT QoS levels, or manage gRPC channels. Something must sit between the flow and the protocol — an adapter that speaks the protocol and exposes a clean, versioned interface to the flow.

In software, this is an SDK. You don't write raw HTTP requests — you call `client.CreateServer()` and the SDK handles serialization, transport, retries, and error mapping. The SDK version determines what capabilities are available and how results are shaped.

## Decision

### 1. A plugin is a protocol adapter

A plugin represents a communication protocol at a specific version. It is the SDK that Gluey uses to interact with external systems.

```gluey
plugin http 2.0.0 {
  response: http-response 2.0.0
}
```

The plugin declares:
- **type** — the protocol family it implements
- **response** — the wrapper contract it uses to normalize responses for flows

### 2. Plugin version tracks the protocol version

The plugin version corresponds to the protocol specification it implements. This is not an arbitrary application version — it maps to the actual protocol version.

| Plugin | Version | Protocol |
|--------|---------|----------|
| `plugin http 0.9.0` | HTTP/0.9 | Single-line protocol, GET only, no headers |
| `plugin http 1.0.0` | HTTP/1.0 | Headers, status codes, Content-Type |
| `plugin http 1.1.0` | HTTP/1.1 | Persistent connections, chunked transfer, Host header |
| `plugin http 2.0.0` | HTTP/2 | Binary framing, multiplexing, server push, header compression |
| `plugin http 3.0.0` | HTTP/3 | QUIC transport, 0-RTT, connection migration |
| `plugin mqtt 3.1.0` | MQTT 3.1 | Basic pub/sub |
| `plugin mqtt 3.1.1` | MQTT 3.1.1 | Clarifications, session management improvements |
| `plugin mqtt 5.0.0` | MQTT 5.0 | Shared subscriptions, message expiry, user properties |
| `plugin grpc 1.0.0` | gRPC over HTTP/2 | Unary and streaming RPCs, protobuf |

The version tells you exactly what protocol capabilities are available. An endpoint referencing `plugin: http 2.0.0` knows that multiplexing and header compression are supported. One referencing `plugin: http 1.1.0` knows they are not.

### 3. Plugin owns the response wrapper

Each plugin defines how it normalizes protocol responses for flow consumption. The flow never sees raw protocol output — it works with the plugin's response contract.

The response contract is versioned alongside the plugin. When the plugin version changes, the response shape may change. This is a first-class dependency — flows that consume the response are affected.

### 4. Endpoints reference a plugin

An endpoint declares which plugin (and therefore which protocol version) it uses:

```gluey
http-endpoint create-server 1.0.0 {
  app: hetzner 1.0.0
  plugin: http 2.0.0
  method: POST
  path: "/v1/servers"
  headers: hetzner-write-headers 1.0.0
  payload: create-server-request 1.0.0
  response: create-server-response 1.0.0
}
```

The `app:` reference connects the endpoint to the application it belongs to. This closes the dependency chain: `flow → endpoint → app`. Gluey can trace from a flow to the application it interacts with.

The `plugin: http 2.0.0` reference is a versioned dependency. If the infrastructure provider upgrades to HTTP/3, the endpoint updates to `plugin: http 3.0.0`. Gluey flags this change and identifies every flow that uses this endpoint.

### 5. Multiple protocol versions can coexist

A system can use multiple versions of the same protocol simultaneously. One API may require HTTP/2, another may still be on HTTP/1.1. Each endpoint declares its own plugin version independently.

This is common during migrations. The contracts make the migration explicit — you can see exactly which endpoints are on which protocol version and track the migration's progress.

### 6. Bidirectional auth

A plugin can act as both client and server depending on the flow direction:

- **`to` (client)** — the plugin presents credentials from the auth chain to the remote system
- **`from` (server)** — the plugin validates incoming credentials against the auth chain

The same auth contract serves both directions. The plugin determines the behavior based on whether the flow uses `from` (receiving) or `to` (sending). This applies to any protocol — HTTP (client/listener), MQTT (today always client to broker, but a future broker plugin would validate connecting devices), gRPC (client/server).

The auth chain and the flow direction give the plugin all the information it needs. No separate auth configuration is required per direction.

### 7. Plugin versioning rules

| Change | Bump | Rationale |
|--------|------|-----------|
| New protocol version | major | Different wire behavior, new capabilities |
| Response wrapper adds optional field | minor | Existing flows unaffected |
| Response wrapper removes field | major | Existing flows may reference it |
| Response wrapper changes field type | major | Existing flows expect the old type |
| Internal implementation change | patch | Same protocol, same interface |

## Consequences

- Flows are decoupled from protocol details — they work with the plugin's typed response contract, not raw protocol output
- Protocol version is explicit and trackable — every endpoint declares which protocol version it uses
- Protocol upgrades are visible in the dependency graph — changing from HTTP/2 to HTTP/3 surfaces every affected endpoint and flow
- Multiple protocol versions can coexist, enabling incremental migration
- Adding a new protocol requires a new plugin contract — no changes to existing flows or endpoints

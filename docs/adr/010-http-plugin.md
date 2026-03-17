# ADR-010: HTTP Plugin

## Status

Accepted

## Context

HTTP is the most common protocol for API communication. Gluey flows need to send and receive HTTP requests — calling cloud provider APIs, serving webhooks, communicating between services. The HTTP plugin is the adapter between flows and the HTTP protocol.

The plugin must:
- Support multiple HTTP versions (0.9, 1.0, 1.1, 2, 3)
- Define the wire format and the parsed output shape
- Introduce contract types for endpoints and headers
- Keep endpoints clean while providing full protocol traceability
- Work as both input (listener) and output (client)

## Decision

### 1. One plugin per protocol version

Each HTTP version is a separate plugin with its own structs. Different protocol versions have different wire behavior — HTTP/1.1 is text-based, HTTP/2 uses binary framing with pseudo-headers and stream IDs, HTTP/3 uses QUIC with connection migration. These are breaking differences that produce different structs.

```gluey
plugin http 0.9.0 {
  structs: [http-request 0.9.0, http-response 0.9.0]
  contracts: [http-endpoint, http-headers]
}

plugin http 1.0.0 {
  structs: [http-request 1.0.0, http-response 1.0.0]
  contracts: [http-endpoint, http-headers]
}

plugin http 1.1.0 {
  structs: [http-request 1.1.0, http-response 1.1.0]
  contracts: [http-endpoint, http-headers]
}

plugin http 2.0.0 {
  structs: [http-request 2.0.0, http-response 2.0.0]
  contracts: [http-endpoint, http-headers]
}

plugin http 3.0.0 {
  structs: [http-request 3.0.0, http-response 3.0.0]
  contracts: [http-endpoint, http-headers]
}
```

- `structs` — the in-memory shapes the plugin produces after parsing the wire format. Versioned per plugin version because different protocol versions produce different structures.
- `contracts` — the contract keywords this plugin registers with Gluey. Shared across all versions of the plugin — `http-endpoint` and `http-headers` work the same regardless of HTTP version.

The `contracts` registration is what makes `http-endpoint` and `http-headers` valid keywords in `.gluey` files. Without the plugin declaring them, Gluey would reject those keywords as unknown.

An endpoint pinning `plugin: http 2.0.0` gets `http-request 2.0.0` / `http-response 2.0.0`. Upgrading to `plugin: http 3.0.0` is a major change — different structs, different wire behavior. Gluey flags every affected endpoint and flow.

### 2. Protocol structs

Structs describe the in-memory shape after the plugin parses the wire format. They are not JSON, not binary — they are the parsed protocol frame. Each protocol version defines its own structs.

**Request struct (HTTP/2 example):**

```gluey
struct http-request 2.0.0 {
  properties: {
    method: text 1.0.0 {
      required: true
      displayName: { "en-US": "Method" }
      description: { "en-US": "HTTP method, e.g. GET, POST, PUT, DELETE" }
    }
    path: text 1.0.0 {
      required: true
      displayName: { "en-US": "Path" }
      description: { "en-US": "Request path including query string" }
    }
    headers: map 1.0.0 {
      required: true
      key: text 1.0.0 { comparison: "ordinal-ignore-case" }
      value: text 1.0.0
      displayName: { "en-US": "Headers" }
      description: { "en-US": "HTTP request headers" }
    }
    body: bytes 1.0.0 {
      required: false
      displayName: { "en-US": "Body" }
      description: { "en-US": "Raw request body" }
    }
  }
}
```

**Response struct (HTTP/2 example):**

```gluey
struct http-response 2.0.0 {
  properties: {
    status_code: int32 1.0.0 {
      required: true
      minimum: 100
      maximum: 599
      displayName: { "en-US": "Status Code" }
      description: { "en-US": "HTTP status code returned by the server" }
    }
    reason_phrase: text 1.0.0 {
      required: true
      displayName: { "en-US": "Reason Phrase" }
      description: { "en-US": "HTTP reason phrase, e.g. OK, Not Found, Internal Server Error" }
    }
    headers: map 1.0.0 {
      required: true
      key: text 1.0.0 { comparison: "ordinal-ignore-case" }
      value: text 1.0.0
      displayName: { "en-US": "Response Headers" }
      description: { "en-US": "HTTP response headers" }
    }
    body: bytes 1.0.0 {
      required: false
      displayName: { "en-US": "Body" }
      description: { "en-US": "Raw response body" }
    }
  }
}
```

### 3. HTTP endpoint contract

An endpoint describes a specific API call — method, path, what goes in, what comes out. The plugin defines the request/response structs; the endpoint defines what's inside them.

```gluey
http-endpoint create-server 1.0.0 {
  app: hetzner 1.0.0
  plugin: http 2.0.0
  method: POST
  path: "/v1/servers"
  headers: hetzner-write-headers 1.0.0
  payload: json create-server-request 1.0.0
  responses: {
    201: json create-server-response 1.0.0
    429: json hetzner-rate-limit 1.0.0
    default: json hetzner-error 1.0.0
  }
}
```

- `app` — the application this endpoint belongs to, connecting it to the dependency graph
- `plugin` — which HTTP version, determining the request/response structs
- `method` / `path` — populate the request struct's method and path fields
- `headers` — a headers contract for the runtime to validate before sending
- `payload` — the format and contract for the request body
- `responses` — maps status codes to body format and contract; `default` catches any status code not explicitly listed

The endpoint does not repeat the struct — the plugin owns that. The endpoint only declares what goes inside the struct's body and headers.

### 4. HTTP headers contract

Headers are key-value pairs with validation rules. The plugin owns this contract type.

```gluey
http-headers hetzner-write-headers 1.0.0 {
  properties: {
    Authorization: text 1.0.0 {
      required: true
      obfuscate: true
      pattern: "^Bearer .+$"
      displayName: { "en-US": "Authorization" }
      description: { "en-US": "Bearer token for Hetzner Cloud API authentication" }
    }
    Content-Type: text 1.0.0 {
      required: true
      enum: ["application/json"]
      displayName: { "en-US": "Content Type" }
      description: { "en-US": "Request body media type" }
    }
  }
}
```

The runtime validates that the flow populates all required headers with values matching the declared constraints before sending the request.

### 5. Input and output

The HTTP plugin works in two directions:

- **Client (output)** — the flow sends an HTTP request via `to`. Used when calling external APIs.
- **Listener (input)** — the flow receives an HTTP request via `from`. Used when serving webhooks or APIs.

The same `http-endpoint` contract describes both sides. The endpoint defines the method, path, headers, payload, and responses. The flow decides which direction it uses:

```gluey
// Client — calling Hetzner API
flow provision-server 1.0.0 {
  // ...
  | to http-endpoint create-server 1.0.0
}

// Listener — receiving webhooks
flow handle-webhook 1.0.0 {
  from http-endpoint webhook-event 1.0.0
  // ...
}
```

The plugin handles the protocol mechanics for both directions. The endpoint contract is the same — it's the shared API shape.

### 6. The plugin does not interpret responses

The plugin delivers the raw struct — status code, reason phrase, headers, body. It does not classify responses as success or error. It does not decide what is retryable. That is the flow's responsibility, informed by the endpoint's response map.

The endpoint declares what status codes are possible and what the body contains for each. The flow matches on status codes and acts accordingly:

```gluey
flow provision-server 1.0.0 {
  | to http-endpoint create-server 1.0.0

  | match response.status_code {
      201 -> log("created: ${response.body.server.id}")
      429 -> retry(after: response.headers["Retry-After"])
      _   -> alert("${response.status_code}: ${response.reason_phrase}")
    }
}
```

## Consequences

- Each HTTP version is a separate plugin with its own structs — upgrading from HTTP/2 to HTTP/3 is a tracked breaking change
- Protocol details (structs, contract keywords) are owned by the plugin, not hardcoded in Gluey core
- Endpoints are clean — they declare method, path, headers, payload, and response mappings without repeating protocol framing
- The `default` response catches unmapped status codes, keeping endpoints concise when the API returns a common error shape
- The same endpoint contract works for both client and listener — the flow decides the direction
- Response interpretation (retry, error handling) is the flow's responsibility, not the plugin's
- Multiple HTTP versions can coexist — each endpoint declares its plugin version independently
- Adding a new protocol (MQTT, gRPC) follows the same pattern — define a plugin, its structs, and its contract types

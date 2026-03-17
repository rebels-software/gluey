# ADR-009: HTTP Plugin Response and Endpoint Response Mapping

## Status

Accepted

## Context

When an HTTP request is made, the server returns a status code, reason phrase, headers, and a body. Different APIs assign different meanings to the same status codes. A `500` might be transient in one API and permanent in another. A `429` might include a `Retry-After` header or might not. A `200` from one endpoint returns a server object, while a `200` from another returns a list of images.

The question is: who interprets the response? The plugin, the endpoint, or the flow?

## Decision

### 1. The plugin delivers raw protocol output

The HTTP plugin's response contract exposes exactly what the server sent — no interpretation, no normalization, no opinion on retryability or success/failure.

```gluey
json http-response 2.0.0 {
  properties: {
    status_code: int32 1.0.0 {
      required: true
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

The plugin does not classify responses as "success" or "error." It does not decide what is retryable. It does not parse the body. It is transport — it delivers status code, reason phrase, headers, and raw body.

### 2. The endpoint defines what responses are possible

Each endpoint declares the set of status codes it can return and maps each to a typed response contract. This is the endpoint author's knowledge — they know what their API returns.

```gluey
http-endpoint create-server 1.0.0 {
  app: hetzner 1.0.0
  plugin: http 2.0.0
  method: POST
  path: "/v1/servers"
  headers: hetzner-write-headers 1.0.0
  payload: create-server-request 1.0.0
  responses: {
    201: create-server-response 1.0.0
    400: hetzner-error 1.0.0
    401: hetzner-error 1.0.0
    403: hetzner-error 1.0.0
    404: hetzner-error 1.0.0
    409: hetzner-error 1.0.0
    422: hetzner-error 1.0.0
    429: hetzner-rate-limit 1.0.0
    500: hetzner-error 1.0.0
  }
}
```

The responses map is exhaustive for the documented API. Each status code maps to a versioned contract that describes the body shape for that response. Different status codes can map to different contracts — a `201` returns a server object, a `429` returns rate limit details, a `400` returns an error.

### 3. The flow decides what to do

The flow matches on status codes and acts on the typed response. Retry logic, error handling, alerting — all flow concerns.

The flow has full context:
- **status_code** — what happened
- **reason_phrase** — human-readable explanation
- **headers** — protocol metadata (e.g., `Retry-After`)
- **body** — typed per endpoint's response map

The flow author decides: is a `500` retryable for this API? Should a `429` wait or fail? Should a `409` retry with a different payload? These are business decisions, not protocol decisions.

### 4. Why not in the plugin

Putting retry logic or success/failure classification in the plugin forces a single interpretation across all APIs. This is wrong because:

- A `500` from a payment gateway means "do not retry — you might double-charge"
- A `500` from a telemetry ingest means "retry — it's probably transient"
- A `503` with `Retry-After: 86400` means "come back tomorrow" — not a short retry

The plugin cannot know the API's semantics. Only the endpoint and the flow can.

### 5. Why not in the endpoint

The endpoint could declare `retryable: true` per status code. But this still forces a single answer per endpoint, while the right action depends on the flow's context:

- A real-time alerting flow may skip retries and fail fast
- A batch processing flow may retry with exponential backoff
- A provisioning flow may retry `503` but alert on `500`

Retry and error handling are flow-level decisions.

## Consequences

- The HTTP plugin is pure transport — status code, reason phrase, headers, body. No interpretation.
- Each endpoint documents its possible responses with typed contracts per status code
- Flows have full control over error handling and retry logic, with full protocol context available
- The response map is a versioned dependency — adding or removing a status code from an endpoint is a trackable change
- Different endpoints on the same API can have different response shapes per status code

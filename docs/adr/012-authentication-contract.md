# ADR-012: Authentication Contract

## Status

Accepted

## Context

Every protocol has authentication. HTTP has bearer tokens, API keys, and mutual TLS. MQTT has username/password and client certificates. gRPC has mutual TLS and per-call tokens. Each mechanism operates at a different layer — some at transport (mTLS), some at application (headers, CONNECT packets).

Authentication credentials can come from different sources — a secret store like OpenBao, Azure Key Vault, environment variables, or a PKI certificate authority. The credential source is a deployment decision, not an application decision. Gluey ships with OpenBao out of the box, but a company hosted on Azure should be able to configure Azure Key Vault instead.

Credential acquisition ranges from simple (read a static token) to complex (call an OAuth endpoint, parse the response, cache the token, refresh on expiry). The simple cases should be built into the auth contract. The complex cases should be handled by flow functions — reusable flow logic.

Server-side TLS (HTTPS, MQTTS) is not authentication. It is transport encryption — the server presents its certificate to prove identity and establish an encrypted channel. This happens before any authentication and is a property of the connection, not the identity.

## Decision

### 1. Authentication is a contract with a chain

Authentication methods are declared as versioned contracts using the `auth` keyword. The `chain` is an ordered list of methods — the runtime tries each until one succeeds, similar to .NET's `DefaultAzureCredential`.

```gluey
auth hetzner-api 1.0.0 {
  chain: [
    bearer { provider: openbao 1.0.0, path: "secret/data/hetzner/api-token" }
    bearer { provider: env, key: "HETZNER_API_TOKEN" }
  ]
}
```

The runtime tries bearer from OpenBao first. If OpenBao is unreachable or the path doesn't exist, it falls back to the environment variable.

Empty chain means no authentication:

```gluey
auth public-api 1.0.0 {
  chain: []
}
```

Or simply omit `auth:` from the endpoint — no authentication required.

### 2. Built-in methods

Gluey supports these credential acquisition methods natively:

**Bearer token (static):**

```gluey
auth hetzner-api 1.0.0 {
  chain: [
    bearer { provider: openbao 1.0.0, path: "secret/data/hetzner/api-token" }
  ]
}
```

**Mutual TLS (PKI):**

```gluey
auth device-mtls 1.0.0 {
  chain: [
    mtls { provider: openbao 1.0.0, path: "pki/issue/device-gateway" }
  ]
}
```

**Username/password:**

```gluey
auth mqtt-credentials 1.0.0 {
  chain: [
    username-password { provider: openbao 1.0.0, path: "secret/data/emqx/credentials" }
  ]
}
```

**API key:**

```gluey
auth third-party-api 1.0.0 {
  chain: [
    api-key { provider: openbao 1.0.0, path: "secret/data/vendor/api-key", header: "X-API-Key" }
  ]
}
```

**Environment variable (any method):**

```gluey
bearer { provider: env, key: "HETZNER_API_TOKEN" }
```

### 3. Flow functions for complex acquisition

For OAuth, login flows, token refresh, or any acquisition that requires calling an endpoint and parsing a response — use a flow function. Flow functions are reusable flow logic that other flows and auth contracts can reference.

```gluey
flow-function acquire-keycloak-token 1.0.0 {
  | to http-endpoint keycloak-token 1.0.0

  | match response.status_code {
      200 -> return response.body.access_token
    }
}
```

The auth contract references the flow function as a provider:

```gluey
auth keycloak-oauth 1.0.0 {
  chain: [
    bearer { provider: flow-function acquire-keycloak-token 1.0.0 }
    bearer { provider: env, key: "KEYCLOAK_FALLBACK_TOKEN" }
  ]
}
```

Caching (Redis, in-memory, TTL), retry logic, and refresh are the flow function's responsibility. They are logic, not something to encode in the auth DSL.

### 4. Endpoints and subscriptions reference auth contracts

```gluey
http-endpoint create-server 1.0.0 {
  app: hetzner 1.0.0
  plugin: http 2.0.0
  auth: hetzner-api 1.0.0
  method: POST
  path: "/v1/servers"
  headers: hetzner-content-headers 1.0.0
  payload: json create-server-request 1.0.0
  responses: {
    201: json create-server-response 1.0.0
    default: json hetzner-error 1.0.0
  }
}

mqtt-subscription telemetry-sub 1.0.0 {
  app: emqx 1.0.0
  plugin: mqtt 5.0.0
  auth: mqtt-credentials 1.0.0
  topic: "riot/+/telemetry" { ... }
  qos: 1
  payload: json device-telemetry 1.0.0
}
```

### 5. The plugin applies auth at the right layer

The plugin knows how to apply each auth method for its protocol:

| Plugin | Method | Where applied |
|--------|--------|--------------|
| HTTP | `bearer` | `Authorization: Bearer {token}` header |
| HTTP | `api-key` | Custom header with key value |
| HTTP | `mtls` | TLS handshake with client certificate |
| MQTT | `username-password` | CONNECT packet |
| MQTT | `mtls` | TLS handshake with client certificate |
| gRPC | `bearer` | `authorization` metadata |
| gRPC | `mtls` | TLS handshake with client certificate |

The auth contract declares what credentials are needed and where they come from. The plugin knows where to put them on the wire. The flow never touches authentication.

### 6. Auth works bidirectionally

The same auth contract serves both directions of a flow:

- **`to` (client mode)** — the plugin acquires credentials from the auth chain and presents them to the remote system (e.g., attaches Bearer header, performs mTLS client handshake, sends CONNECT with username/password)
- **`from` (server mode)** — the plugin receives incoming connections and validates credentials against the auth chain (e.g., verifies Bearer token, validates client certificate against CA, checks CONNECT credentials)

The plugin determines the behavior based on the flow direction. No separate auth configuration is needed per direction. This applies to any protocol:

- **HTTP** — client presents credentials; listener validates them
- **MQTT** — today always client mode (authenticate to broker). A future broker plugin would validate connecting devices in `from` mode
- **gRPC** — client presents credentials; server validates them

### 7. Server-side TLS is not auth

Server-side TLS (HTTPS, MQTTS) is transport encryption. It belongs on the app or infrastructure contract, not on the auth contract:

```gluey
app hetzner 1.0.0 {
  host: "api.hetzner.cloud"
  port api: 443
  tls: {
    ca_certificate: openbao 1.0.0 { path: "pki/ca/hetzner" }
  }
}
```

This is an infrastructure concern, not an endpoint or auth concern.

### 8. Provider is a deployment decision

The auth contract references a provider (OpenBao, Azure Key Vault, env). Switching providers means updating the auth contract's chain entries. The endpoint contract is unaffected — it still references `auth: hetzner-api 1.0.0`.

A company migrating from OpenBao to Azure Key Vault changes:

```gluey
// Before
auth hetzner-api 1.0.0 {
  chain: [
    bearer { provider: openbao 1.0.0, path: "secret/data/hetzner/api-token" }
  ]
}

// After
auth hetzner-api 2.0.0 {
  chain: [
    bearer { provider: azure-keyvault 1.0.0, path: "secrets/hetzner-api-token" }
  ]
}
```

The auth version bumps (different provider is a breaking change). Gluey flags every endpoint using this auth. But the endpoint contracts themselves don't change.

## Consequences

- Authentication is a first-class, versioned contract with an ordered chain of methods
- Simple acquisition (static tokens, PKI certs, env vars) is built in
- Complex acquisition (OAuth, login, refresh, caching) is handled by flow functions — reusable flow logic, not DSL complexity
- The chain provides fallback — if one method fails, try the next
- Server-side TLS is separated from authentication — transport encryption is an infrastructure concern
- Provider is a deployment decision — switching from OpenBao to Azure Key Vault changes the auth contract, not the endpoints
- The plugin applies credentials at the correct protocol layer automatically
- The same auth contract can be shared across multiple endpoints and subscriptions

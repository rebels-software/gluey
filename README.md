# Gluey CLI

A no-code IoT message router that connects data sources to destinations using a simple, readable DSL.

## What is Gluey?

Gluey lets you model data flows like `MQTT → filter → transform → SQL` without writing code. Define your workflows in `.gflow` files and run them as daemons. Perfect for IoT integrators who need to forward, transform, and route messages between protocols.

```gflow
flow sensor-pipeline v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/+/temperature"]
  }

  | json.parse(payload)
  | filter(temperature > 20)
  | transform {
      device_id: topic.split("/")[1]
      temp_fahrenheit: temperature * 9/5 + 32
      alert: temperature > 35 ? "high" : "normal"
    }
  | sql("Host=localhost;Database=sensors") {
      table: "readings"
      columns: {
        device: "device_id"
        temp_f: "temp_fahrenheit"
        alert_level: "alert"
      }
    }
}
```

## Features

**Data Sources & Destinations**
- HTTP webhooks for REST API integration
- MQTT with TLS support and wildcard subscriptions
- SQL output for PostgreSQL and SQL Server
- Console output for debugging

**Message Processing**
- JSON parsing with error handling
- Field filtering with expressions (`temperature > 20 && humidity < 80`)
- Data transformation with arithmetic, ternary, and built-in functions
- Binary protocol decoding with explicit endianness support
- Conditional routing to multiple outputs

**Operations**
- Validate workflows before deployment
- Run as foreground daemon with graceful shutdown
- Docker support with Alpine-based images under 100MB

## Quick Start

**Prerequisites:** .NET 10 SDK

```bash
# Build from source
git clone https://github.com/rebels-software/gluey.git
cd gluey/engine
dotnet build

# Validate a workflow
dotnet run --project src/Gluey.Cli -- validate samples/01-hello-world.gflow

# Run a workflow
dotnet run --project src/Gluey.Cli -- run samples/01-hello-world.gflow
```

Test with curl in another terminal:

```bash
curl -X POST http://localhost:8080/webhook \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello, Gluey!", "temperature": 23.5}'
```

Press `Ctrl+C` to stop the workflow.

## Docker

```bash
# Build
docker build -f docker/Dockerfile -t gluey:latest .

# Run
docker run --rm -p 8080:8080 -v $(pwd)/samples:/workflows \
  gluey:latest run /workflows/01-hello-world.gflow
```

## Sample Workflows

| File | Description |
|------|-------------|
| `01-hello-world.gflow` | HTTP → console (getting started) |
| `02-smart-sensor.gflow` | Filter + transform pipeline |
| `03-binary-sensor.gflow` | Binary protocol decoding |
| `04-industrial-pipeline.gflow` | MQTT → routing → MQTT/SQL |
| `05-advanced-protocol.gflow` | Binary header-based routing |

## Documentation

- [Getting Started](docs/getting-started.md) — Installation and first workflow
- [DSL Reference](docs/dsl-reference.md) — Complete syntax guide
- [Plugins Reference](docs/plugins.md) — Available inputs, transforms, and outputs

## Built-in Plugins

| Type | Plugins |
|------|---------|
| **Input** | `http`, `mqtt` |
| **Transform** | `json.parse`, `filter`, `transform`, `decode.binary`, `decode.base64`, `decode.hex`, `route` |
| **Output** | `console`, `http`, `mqtt`, `sql` |

## License

Apache License 2.0 — see [LICENSE](LICENSE) for details.

Copyright 2026 Rebels Software

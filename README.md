# Gluey CLI

<p align="center">
  <a href="https://github.com/rebels-software/gluey/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/rebels-software/gluey/ci.yml?branch=develop&style=for-the-badge&label=Build" alt="Build Status"></a>
  <a href="https://github.com/rebels-software/gluey/releases/latest"><img src="https://img.shields.io/badge/Version-0.2.4-brightgreen?style=for-the-badge" alt="Version 0.2.0"></a>
  <a href="https://github.com/rebels-software/gluey/pkgs/container/gluey"><img src="https://img.shields.io/badge/Docker-ghcr.io-blue?style=for-the-badge&logo=docker" alt="Docker Image"></a>
  <a href="https://www.apache.org/licenses/LICENSE-2.0"><img src="https://img.shields.io/badge/License-Apache_2.0-blue.svg?style=for-the-badge" alt="Apache License 2.0"></a>
</p>

Gluey is a command-line tool and daemon runtime for building IoT message pipelines. Write your data flows in `.gflow` files - a readable DSL designed for integrators - and let Gluey handle the protocol details, error handling, and graceful shutdowns.

**The CLI** validates and runs workflows. **The runtime** keeps them alive as daemons with proper signal handling.

No SDKs. No framework lock-in. Just declare what goes where.

## How It Works

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              Gluey Flow                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────┐    ┌─────────────────────────────────┐    ┌─────────────┐  │
│  │  INPUT  │───▶│          TRANSFORMS             │───▶│   OUTPUT    │  │
│  └─────────┘    └─────────────────────────────────┘    └─────────────┘  │
│                                                                         │
│  • http         • json.parse    • filter            • console           │
│  • mqtt         • transform     • route             • http              │
│                 • decode.*      (binary/base64/hex) • mqtt              │
│                                                      • sql              │
└─────────────────────────────────────────────────────────────────────────┘

         .gflow DSL                    Daemon Runtime
    ┌──────────────────┐          ┌─────────────────────────┐
    │ flow my-flow v1  │          │   gluey run flow.gflow  │
    │   from mqtt(...) │   ───▶   │                         │
    │   | json.parse   │  parse   │   • Graceful shutdown   │
    │   | filter(...)  │          │   • Error handling      │
    │   | sql(...)     │          │   • Route branching     │
    └──────────────────┘          └─────────────────────────┘
```

## Why Gluey?

Most IoT data routing tools are either too heavy or too general-purpose:

| Tool | Gluey | Node-RED | Apache NiFi | n8n |
|------|-------|----------|-------------|-----|
| **Focus** | IoT message pipelines | Visual flow programming | Enterprise data flow | Workflow automation |
| **Config** | `.gflow` text files | JSON (GUI-generated) | XML templates | JSON (GUI-generated) |
| **Binary decode** | Built-in (endianness) | Requires custom nodes | Requires processors | Not supported |
| **Footprint** | ~70MB single binary | ~200MB + Node.js | ~1GB+ JVM | ~200MB + Node.js |
| **Git-friendly** | Yes (plain text DSL) | Painful (JSON diffs) | No (XML blobs) | No (JSON blobs) |
| **Daemon mode** | Built-in multi-workflow | Single process | Cluster mode | Single process |
| **Target user** | IoT integrators | Hobbyists/prototyping | Enterprise teams | Business automation |

Gluey is purpose-built for the IoT integrator who needs to get `MQTT -> filter -> transform -> SQL` running in minutes, not hours. The `.gflow` DSL is version-controllable, diffable, and reviewable. No GUI required.

## What is Gluey?

Gluey lets you model data flows like `MQTT → filter → transform → SQL` in a declarative DSL. Define your workflows in `.gflow` files and run them as daemons. Perfect for IoT integrators who need to forward, transform, and route messages between protocols.

```gflow
flow sensor-pipeline v1.0 {
  from mqtt("mqtt://broker:1883") {
    topics: ["sensors/+/temperature"]
  }

  | json.parse(payload)
  | filter(temperature > 20)
  | transform {
      device_id: device_id
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
- Fan-out to multiple destinations in parallel

**Operations**
- Validate workflows before deployment
- Run as foreground daemon with graceful shutdown
- Daemon mode for managing multiple workflows concurrently
- Smart `start` auto-launches daemon when needed
- Live log streaming with `--follow`
- Hot reload workflows without downtime
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

## CLI Reference

### Single Workflow Mode

```bash
gluey validate <file.gflow>        # Check syntax and plugin names
gluey run <file.gflow>             # Run as foreground process (Ctrl+C to stop)
gluey run <file.gflow> --verbose   # Run with full .NET framework logs
```

### Daemon Mode

The daemon manages multiple workflows concurrently via an HTTP API on port 6262.

```bash
# Daemon lifecycle
gluey daemon start                 # Start in foreground
gluey daemon start --background    # Start in background (fork)
gluey daemon start --port 7000     # Custom port
gluey daemon status                # Show daemon status, port, pid, uptime
gluey daemon stop                  # Graceful shutdown
```

### Workflow Management (via daemon)

```bash
# Smart start - auto-launches daemon if not running
gluey start <file.gflow>          # Load + start workflow (starts daemon if needed)

# Manual workflow lifecycle
gluey load <file.gflow>           # Load workflow into daemon (Draft status)
gluey start <name|id>             # Start a loaded workflow
gluey stop <name|id>              # Stop a running workflow
gluey pause <name|id>             # Pause a running workflow
gluey reload <name|id>            # Hot reload .gflow file without downtime
gluey unload <name|id>            # Remove workflow from daemon

# Monitoring
gluey list                        # List all workflows with status
gluey logs <name|id>              # Show recent workflow logs
gluey logs <name|id> --follow     # Stream logs continuously

# Maintenance
gluey prune                       # Clear persisted workflow state
```

### Workflow States

```
Draft → Active → Stopped
         ↓  ↑
        Paused
```

- **Draft** - Loaded but not started
- **Active** - Running and processing messages
- **Paused** - Suspended, can be resumed
- **Stopped** - Halted, can be restarted

## Sample Workflows

| File | Description |
|------|-------------|
| `01-hello-world.gflow` | HTTP → console (getting started) |
| `02-smart-sensor.gflow` | Filter + transform pipeline |
| `03-binary-sensor.gflow` | Binary protocol decoding |
| `04-industrial-pipeline.gflow` | MQTT → routing → MQTT/SQL |
| `05-advanced-protocol.gflow` | Binary header-based routing |
| `06-fanout-pattern.gflow` | Parallel output to console + HTTP |

## Documentation

- [Getting Started](docs/getting-started.md) - Installation and first workflow
- [DSL Reference](docs/dsl-reference.md) - Complete syntax guide
- [Plugins Reference](docs/plugins.md) - Available inputs, transforms, and outputs
- [Changelog](CHANGELOG.md) - Release history

## Built-in Plugins

| Type | Plugins |
|------|---------|
| **Input** | `http`, `mqtt` |
| **Transform** | `json.parse`, `filter`, `transform`, `decode.binary`, `decode.base64`, `decode.hex`, `route` |
| **Output** | `console`, `http`, `mqtt`, `sql` |

## Contributing

Contributions are welcome. Please open an issue first to discuss what you'd like to change.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Run the tests (`dotnet test`)
4. Commit your changes
5. Open a Pull Request

## License

Apache License 2.0 - see [LICENSE](LICENSE) for details.

Copyright 2026 Rebels Software

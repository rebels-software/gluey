# Getting Started with Gluey CLI

This guide walks you through installing Gluey and running your first workflow.

## Installation

### Option 1: Build from Source

**Prerequisites:**
- .NET 10 SDK

```bash
# Clone the repository
git clone https://github.com/rebels-software/gluey.git
cd gluey/engine

# Build the solution
dotnet build

# Verify installation
dotnet run --project src/Gluey.Cli -- --help
```

### Option 2: Docker (pre-built image)

```bash
# Pull the pre-built image
docker run --rm ghcr.io/rebels-software/gluey --help
```

### Option 3: Docker (build from source)

```bash
# Build the image
docker build -f docker/Dockerfile -t gluey:latest .

# Verify installation
docker run --rm gluey:latest --help
```

## Your First Workflow

Let's create a simple workflow that receives JSON via HTTP and prints it to the console.

### The Workflow File

Create a file named `hello-world.gflow`:

```gflow
flow hello-world v1.0 {
  from http("/webhook") {
    port: 8080
  }

  | json.parse(payload)
  | console()
}
```

This workflow:
1. Listens for HTTP POST requests on port 8080 at `/webhook`
2. Parses incoming JSON payloads
3. Prints the parsed data to the console with a timestamp

### Validate the Workflow

Before running, validate your syntax:

```bash
# From source
dotnet run --project src/Gluey.Cli -- validate hello-world.gflow

# Docker
docker run --rm -v $(pwd):/workflows gluey:latest validate /workflows/hello-world.gflow
```

On success, you'll see:
```
✓ Valid: hello-world.gflow
```

### Run the Workflow

Start the workflow as a daemon:

```bash
# From source
dotnet run --project src/Gluey.Cli -- run hello-world.gflow

# Docker (expose port 8080)
docker run --rm -p 8080:8080 -v $(pwd):/workflows gluey:latest run /workflows/hello-world.gflow
```

You'll see:
```
Starting workflow 'hello-world' v1.0...
```

### Test with curl

Open another terminal and send a test request:

```bash
curl -X POST http://localhost:8080/webhook \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello, Gluey!", "temperature": 23.5}'
```

In the workflow terminal, you'll see the output:

```
[2026-01-29T10:30:45.123+00:00] {
  "message": "Hello, Gluey!",
  "temperature": 23.5
}
```

### Stop the Workflow

Press `Ctrl+C` to stop the workflow. You'll see:

```
Shutting down...
```

The workflow drains gracefully within 5 seconds.

## Daemon Mode

When you need to run multiple workflows at once, use daemon mode. The daemon manages workflow lifecycles through an HTTP API on port 6262.

### Quick Start with Smart Start

The easiest way is `gluey start`, which auto-launches the daemon if it isn't running:

```bash
# Starts daemon in background, loads and starts the workflow
gluey start hello-world.gflow

# Start more workflows
gluey start samples/02-smart-sensor.gflow
gluey start samples/04-industrial-pipeline.gflow

# See what's running
gluey list
```

### Manual Daemon Control

```bash
# Start the daemon in background
gluey daemon start --background

# Load and start workflows individually
gluey load hello-world.gflow
gluey start hello-world

# Check status
gluey daemon status

# Stop everything
gluey daemon stop
```

## Logs

Each workflow logs its pipeline activity (messages received, transforms applied, output delivery). View logs with:

```bash
# Show recent logs for a workflow
gluey logs hello-world

# Stream logs continuously
gluey logs hello-world --follow
```

## Using the Sample Workflows

Gluey includes sample workflows to help you learn:

| File | Description |
|------|-------------|
| `01-hello-world.gflow` | HTTP → console (basic) |
| `02-smart-sensor.gflow` | HTTP → filter → transform → console |
| `03-binary-sensor.gflow` | HTTP → binary decode → transform → console |
| `04-industrial-pipeline.gflow` | MQTT → transform → route → MQTT/SQL |
| `05-advanced-protocol.gflow` | MQTT → binary decode → route → SQL |
| `06-fanout-pattern.gflow` | HTTP → transform → fan-out to console + HTTP |

Run a sample:

```bash
dotnet run --project src/Gluey.Cli -- run samples/01-hello-world.gflow
```

## Next Steps

- [DSL Reference](dsl-reference.md) - Complete syntax documentation
- [Plugins Reference](plugins.md) - All available inputs, transforms, and outputs
- [Sample Workflows](../samples/) - More examples to learn from

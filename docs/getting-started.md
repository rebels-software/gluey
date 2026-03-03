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

### Option 2: Docker

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

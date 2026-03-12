# AGENTS.md - Gluey CLI Engine

## Project Overview

Gluey CLI is a .NET 10 command-line tool and daemon runtime that parses `.gflow` DSL files and executes message routing workflows. Connect data sources (HTTP, MQTT) to destinations (SQL, MQTT, HTTP) with transforms, filtering, and routing—without writing code.

## Development Commands

```bash
# Build
dotnet build

# Run tests
dotnet test

# Validate a workflow
dotnet run --project src/Gluey.Cli -- validate samples/01-hello-world.gflow

# Run a workflow
dotnet run --project src/Gluey.Cli -- run samples/01-hello-world.gflow
```

## Solution Structure

```
engine/
├── src/
│   ├── Gluey.Cli/           # CLI entry point (validate, run commands)
│   ├── Gluey.Core/          # Domain models & plugin interfaces
│   ├── Gluey.Parser/        # Hand-rolled DSL lexer & parser
│   ├── Gluey.Runtime/       # Workflow executor (IHostedService daemon)
│   └── Gluey.Plugins/       # Built-in inputs, transforms, outputs
├── tests/
│   └── Gluey.Parser.Tests/  # Parser unit tests
├── samples/                 # 5 demo .gflow workflows
├── docs/                    # Documentation
└── docker/                  # Dockerfile
```

## Key Interfaces

```csharp
// Input: emits messages
IInputPlugin { ReadAsync() -> IAsyncEnumerable<Message> }

// Transform: processes messages (return null to filter)
ITransformPlugin { ProcessAsync(Message) -> Message? }

// Output: sends messages
IOutputPlugin { WriteAsync(Message) }
```

## Built-in Plugins

| Type | Plugins |
|------|---------|
| Input | `http`, `mqtt` (TLS supported) |
| Transform | `json.parse`, `filter`, `transform`, `decode.binary`, `decode.base64`, `decode.hex`, `route` |
| Output | `console`, `http`, `mqtt` (TLS supported), `sql` (PostgreSQL + SQL Server) |

## DSL Syntax Quick Reference

```gflow
flow my-workflow v1.0 {
  from http("/webhook") { port: 8080 }

  | json.parse(payload)
  | filter(temperature > 20)
  | transform {
      device_id: device_id
      temp_f: temperature * 9/5 + 32
    }
  | console()
}
```

## Technical Decisions

- **Parser**: Hand-rolled recursive descent (no dependencies, clear errors)
- **Hosting**: IHostedService daemon with graceful shutdown (5s drain)
- **Expressions**: JSONPath.Net for nested field access (`device.location.lat`)
- **Binary**: Span<byte> with explicit endianness (`int16_be`, `uint16_le`)
- **SQL errors**: Graceful (log and continue, don't crash)
- **Target**: .NET 10

## Dependencies

- `System.CommandLine` - CLI parsing
- `MQTTnet` - MQTT client
- `Npgsql` - PostgreSQL
- `Microsoft.Data.SqlClient` - SQL Server
- `JsonPath.Net` - JSONPath expressions

## Copyright Header

All C# source files (*.cs) must include this copyright header at the top:

```csharp
// Copyright (C) 2026 Rebels Software
//
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Add this header before any `using` statements or namespace declarations.

## Documentation Guidelines

When writing or updating documentation (README files, markdown docs, user guides), always use the **humanizer** skill (`/humanizer`) to review the content before committing. This removes signs of AI-generated writing and makes documentation sound more natural.

## Task Tracking

See `prd.json` for user stories. Update `progress.txt` as work completes.

## Specs & Architecture

- Architecture overview: `../architecture/gluey.md`
- DSL specification: `specs/gluey_dsl_docs.md`
- Architecture session: `specs/session_01_architecture.md`

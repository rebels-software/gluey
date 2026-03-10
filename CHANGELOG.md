# Changelog

All notable changes to Gluey CLI are documented here.

## [0.2.8] - 2026-03-10
### Added
- Type casting functions: `int()`, `float()`, `string()` for expression evaluation
- 38 unit tests for casting functions

## [0.2.7] - 2026-03-09
### Fixed
- Ternary expressions in transform blocks now parse correctly (nested `?:` no longer conflicts with field mapping `:`)
- Transform plugin logs expression evaluation errors to stderr instead of silently dropping messages

## [0.2.6] - 2026-03-08
### Added
- Pipeline execution logging in WorkflowRunner (message received, transforms applied, output delivery)
- SQL output plugin now re-throws exceptions for proper error logging

## [0.2.5] - 2026-03-07
### Fixed
- Pipeline output plugins (sql, mqtt, http) now correctly receive parenthesized config arguments

## [0.2.0] - 2026-02-07
### Added
- Daemon mode with HTTP API for managing multiple workflows concurrently
- Workflow lifecycle commands: `load`, `unload`, `start`, `stop`, `pause`, `reload`
- Smart `gluey start file.gflow` auto-launches daemon in background
- `gluey daemon start --background` for background daemon mode
- Custom log formatter: `YYYY-MM-DD HH:mm:ss | workflow-name | message`
- `--verbose` flag for full .NET framework logs
- `gluey logs <name> [--follow]` for live log streaming
- `gluey prune` to clear persisted workflow state
- Metadata access in expressions via `$meta.<key>` syntax
- String functions: `split()`, `substring()`, `indexOf()`, `toLower()`, `toUpper()`, `trim()`
- Fan-out to multiple outputs in parallel: `route -> [output1(), output2()]`
- File-based state persistence in `~/.gluey/state/`
- WorkflowManager for coordinating multiple workflow lifecycles
- HTTP API endpoints for workflow CRUD and control
- GlueyDaemonService with BackgroundService pattern
- 6 sample workflows demonstrating all features

## [0.1.0] - 2026-01-29
### Added
- Initial release
- `.gflow` DSL with lexer and recursive descent parser
- CLI commands: `validate`, `run`
- Input plugins: `http`, `mqtt`
- Transform plugins: `json.parse`, `filter`, `transform`, `decode.binary`, `decode.base64`, `decode.hex`, `route`
- Output plugins: `console`, `http`, `mqtt`, `sql`
- Conditional routing with `| route { }` blocks
- Error handling with `on_error: skip` and `on_error: route_to()`
- Binary protocol decoding with endianness support
- Graceful shutdown with drain timeout
- Docker support with Alpine-based image

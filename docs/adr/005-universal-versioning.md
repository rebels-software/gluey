# ADR-005: Universal Versioning and Change Tracking

## Status

Accepted

## Context

Gluey's purpose is to describe an entire system — from application contracts, through plugins and data schemas, down to individual data properties flowing between services. Every layer depends on other layers: a flow reads from an MQTT topic, transforms data through a schema, and writes to a database via a plugin.

When something changes — a property is renamed, an API endpoint adds a required field, a server type is deprecated — the impact ripples across the system. Today, teams discover these breaks in production, in integration tests, or not at all. The entire point of Gluey is to make every dependency explicit and every change trackable, so the system can tell you exactly what needs attention before anything breaks.

## Decision

### 1. Every contract carries a version

Every contract in Gluey follows the format `keyword name major.minor.patch`. The version is part of the contract's identity — two versions of the same contract are two different contracts.

The version appears in the contract declaration and in every reference to that contract. There are no unversioned contracts and no unversioned references.

### 2. Every reference is an exact version lock

References pin to a specific version. There are no version ranges, no caret or tilde syntax, no "latest" resolution.

This is intentional:

- **Explicit over implicit.** Every reference states exactly which version it depends on. There is no ambiguity.
- **Changes are deliberate.** Updating a reference from `1.0.0` to `1.1.0` is a conscious action that forces the team to evaluate the impact.
- **Reproducible.** The same set of contracts always describes the same system. No version resolution surprises.

### 3. Semver semantics

Version numbers follow semantic versioning with rules defined per contract type. The common principle: if a change can break an existing consumer or producer, it is a major bump.

#### Data contracts (json, protobuf, binary, xml)

| Change | Bump | Rationale |
|--------|------|-----------|
| Add optional property | minor | Existing consumers are unaffected |
| Add required property | major | Existing producers will fail validation |
| Remove property | major | Existing consumers relying on it will break |
| Rename property | major | Equivalent to remove + add required |
| Tighten validation | major | Previously valid payloads may become invalid |
| Loosen validation | minor | All existing payloads remain valid |
| Change property type | major | Wire format and memory representation change |
| Fix description/displayName | patch | No functional impact |

#### App contracts

| Change | Bump | Rationale |
|--------|------|-----------|
| Add new port | minor | Existing ports unaffected |
| Remove port | major | Consumers referencing it break |
| Change image tag | patch | Same contract interface, different build |

#### Flow contracts

| Change | Bump | Rationale |
|--------|------|-----------|
| Add transform step | minor | Input/output contracts unchanged |
| Change source plugin | major | Different data source |
| Change destination plugin | major | Different data destination |
| Modify transform logic | patch | Same input/output shape, different processing |

### 4. Version propagation

Changes propagate up the dependency tree. When a referenced contract changes, every contract that references it must evaluate the impact.

| Referenced contract change | Referencing contract impact |
|---------------------------|---------------------------|
| Patch bump | Patch bump |
| Minor bump | Patch bump |
| Major bump | Major bump |

A breaking change in any dependency is a breaking change in the parent. This propagation is transitive — it walks the full tree from leaf to root.

### 5. Cross-layer dependency graph

Gluey tracks versioned references across all layers:

- **Flow → Plugin**: which plugins a flow reads from and writes to
- **Plugin → App**: which app a plugin connects to
- **Plugin → Data**: which data contract a plugin expects
- **Endpoint → Headers/Payload/Response**: which data contracts an endpoint uses

Every arrow is a versioned reference. When any node in this graph changes its version, Gluey walks the edges and identifies every affected contract. This produces a concrete impact report: a list of contracts that reference the changed version and need attention.

This is the core value. No silent breaks. The system knows every reference, every dependency, every consumer. A change in one place produces a complete list of everything that needs to be updated.

## Consequences

- Every component in the system, from app contracts to individual data properties, is versioned and trackable
- Changes produce a concrete impact report — no silent breaks across layer boundaries
- Version propagation ensures that a breaking change in a leaf type surfaces as a breaking change in every contract that depends on it
- Exact version locks eliminate ambiguity and force deliberate upgrades
- Gluey acts as the system's dependency graph — it knows how everything connects and can answer "what breaks if I change this?"

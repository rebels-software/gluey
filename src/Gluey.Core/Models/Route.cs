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

namespace Gluey.Core.Models;

/// <summary>
/// Represents a routing rule that directs messages to specific destination pipelines.
/// </summary>
public sealed class Route
{
    /// <summary>
    /// The name of this route (used as a reference in route blocks).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The condition expression that determines if a message matches this route.
    /// Examples: "temperature > 35", "device.type == 'sensor'", "*" (catch-all).
    /// </summary>
    public required string Condition { get; init; }

    /// <summary>
    /// The destination steps to execute when this route matches.
    /// Can include transforms and output destinations.
    /// </summary>
    public IReadOnlyList<PipelineStep> DestinationSteps { get; init; } = [];

    /// <summary>
    /// Indicates if this is a catch-all route (condition is "*").
    /// </summary>
    public bool IsCatchAll => Condition == "*";
}

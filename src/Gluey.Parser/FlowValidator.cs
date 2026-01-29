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

using Gluey.Core.Models;

namespace Gluey.Parser;

/// <summary>
/// Validation error with location and optional suggestion.
/// </summary>
public sealed class ValidationError
{
    public required string Message { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public string? Suggestion { get; init; }

    public override string ToString()
    {
        var location = Line > 0 ? $" at line {Line}, column {Column}" : "";
        var suggestion = !string.IsNullOrEmpty(Suggestion) ? $" Did you mean '{Suggestion}'?" : "";
        return $"{Message}{location}.{suggestion}";
    }
}

/// <summary>
/// Result of flow validation containing errors and warnings.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; } = new();
    public List<ValidationError> Warnings { get; } = new();
}

/// <summary>
/// Validates a Flow AST for semantic correctness including plugin availability.
/// </summary>
public sealed class FlowValidator
{
    private readonly HashSet<string> _knownInputPlugins;
    private readonly HashSet<string> _knownTransformPlugins;
    private readonly HashSet<string> _knownOutputPlugins;

    /// <summary>
    /// Creates a FlowValidator with the known plugin types.
    /// </summary>
    public FlowValidator(
        IEnumerable<string> inputPlugins,
        IEnumerable<string> transformPlugins,
        IEnumerable<string> outputPlugins)
    {
        _knownInputPlugins = new HashSet<string>(inputPlugins, StringComparer.OrdinalIgnoreCase);
        _knownTransformPlugins = new HashSet<string>(transformPlugins, StringComparer.OrdinalIgnoreCase);
        _knownOutputPlugins = new HashSet<string>(outputPlugins, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a FlowValidator with the default known plugin types.
    /// </summary>
    public static FlowValidator CreateDefault()
    {
        return new FlowValidator(
            inputPlugins: new[] { "http", "mqtt" },
            transformPlugins: new[] { "json.parse", "filter", "transform", "decode.binary", "decode.base64", "decode.hex", "route" },
            outputPlugins: new[] { "console", "http", "mqtt", "sql" }
        );
    }

    /// <summary>
    /// Validates a Flow AST and returns validation results.
    /// </summary>
    public ValidationResult Validate(Flow flow)
    {
        var result = new ValidationResult();

        // Validate flow name
        ValidateFlowName(flow.Name, result);

        // Validate version
        ValidateVersion(flow.Version, result);

        // Validate input plugin
        ValidateInputPlugin(flow.Input, result);

        // Validate pipeline steps
        ValidatePipelineSteps(flow.PipelineSteps, flow.Routes, result);

        // Validate routes
        ValidateRoutes(flow.Routes, flow.PipelineSteps, result);

        // Validate overall flow structure
        ValidateFlowStructure(flow, result);

        return result;
    }

    private void ValidateFlowName(string name, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add(new ValidationError
            {
                Message = "Flow name is required",
                Line = 1,
                Column = 1
            });
            return;
        }

        // Flow name must be alphanumeric with hyphens, starting with a letter
        if (!char.IsLetter(name[0]))
        {
            result.Errors.Add(new ValidationError
            {
                Message = $"Flow name '{name}' must start with a letter",
                Line = 1,
                Column = 6 // after "flow "
            });
        }

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                result.Errors.Add(new ValidationError
                {
                    Message = $"Flow name '{name}' contains invalid character '{c}'. Only letters, digits, hyphens, and underscores are allowed",
                    Line = 1,
                    Column = 6 + i
                });
                break;
            }
        }
    }

    private void ValidateVersion(string version, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            result.Errors.Add(new ValidationError
            {
                Message = "Flow version is required",
                Line = 1,
                Column = 1
            });
            return;
        }

        // Version should be in format X.Y (major.minor)
        var parts = version.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
        {
            result.Errors.Add(new ValidationError
            {
                Message = $"Version '{version}' has invalid format. Expected 'X.Y' (e.g., '1.0')",
                Line = 1,
                Column = 1
            });
        }
    }

    private void ValidateInputPlugin(InputNode input, ValidationResult result)
    {
        if (!_knownInputPlugins.Contains(input.Type))
        {
            var suggestion = FindSimilarPlugin(input.Type, _knownInputPlugins);
            result.Errors.Add(new ValidationError
            {
                Message = $"Unknown input plugin type: '{input.Type}'. Available inputs: {string.Join(", ", _knownInputPlugins.OrderBy(x => x))}",
                Line = 0, // Line info not available from Flow AST
                Column = 0,
                Suggestion = suggestion
            });
        }
    }

    private void ValidatePipelineSteps(IReadOnlyList<PipelineStep> steps, IReadOnlyList<Route> routes, ValidationResult result)
    {
        bool hasRouteStep = false;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            bool isLastStep = i == steps.Count - 1;

            // Check if it's a known plugin type
            var isTransform = _knownTransformPlugins.Contains(step.Type);
            var isOutput = _knownOutputPlugins.Contains(step.Type);
            var isRouteStep = step.Type.Equals("route", StringComparison.OrdinalIgnoreCase);

            if (isRouteStep)
            {
                hasRouteStep = true;
            }

            if (!isTransform && !isOutput)
            {
                // Find suggestion from both transform and output plugins
                var allPlugins = _knownTransformPlugins.Concat(_knownOutputPlugins);
                var suggestion = FindSimilarPlugin(step.Type, allPlugins);

                result.Errors.Add(new ValidationError
                {
                    Message = $"Unknown plugin type: '{step.Type}'. Available transforms: {string.Join(", ", _knownTransformPlugins.OrderBy(x => x))}. Available outputs: {string.Join(", ", _knownOutputPlugins.OrderBy(x => x))}",
                    Line = 0,
                    Column = 0,
                    Suggestion = suggestion
                });
            }
            else if (isOutput && !isLastStep && !hasRouteStep)
            {
                // Output plugin should typically be the last step unless routing is involved
                result.Warnings.Add(new ValidationError
                {
                    Message = $"Output plugin '{step.Type}' is not at the end of the pipeline. This is unusual unless using routing",
                    Line = 0,
                    Column = 0
                });
            }
        }
    }

    private void ValidateRoutes(IReadOnlyList<Route> routes, IReadOnlyList<PipelineStep> steps, ValidationResult result)
    {
        // Find route conditions defined in the pipeline
        var routeConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (step.Type.Equals("route", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var key in step.Config.Keys)
                {
                    routeConditions.Add(key);
                }
            }
        }

        // Check that all route destinations have matching conditions
        foreach (var route in routes)
        {
            // Check if this route destination has a matching condition
            if (routeConditions.Count > 0 && !routeConditions.Contains(route.Name))
            {
                result.Errors.Add(new ValidationError
                {
                    Message = $"Route destination '{route.Name}' has no matching condition in route block. Defined conditions: {string.Join(", ", routeConditions)}",
                    Line = 0,
                    Column = 0
                });
            }

            // Validate route destination steps
            foreach (var step in route.DestinationSteps)
            {
                var isOutput = _knownOutputPlugins.Contains(step.Type);
                var isTransform = _knownTransformPlugins.Contains(step.Type);

                if (!isOutput && !isTransform)
                {
                    var suggestion = FindSimilarPlugin(step.Type, _knownOutputPlugins.Concat(_knownTransformPlugins));
                    result.Errors.Add(new ValidationError
                    {
                        Message = $"Unknown plugin type in route '{route.Name}': '{step.Type}'",
                        Line = 0,
                        Column = 0,
                        Suggestion = suggestion
                    });
                }
            }

            // Warn if route has no destination steps
            if (route.DestinationSteps.Count == 0)
            {
                result.Warnings.Add(new ValidationError
                {
                    Message = $"Route '{route.Name}' has no destination steps",
                    Line = 0,
                    Column = 0
                });
            }
        }

        // Check that all route conditions have matching destinations
        foreach (var conditionName in routeConditions)
        {
            if (!routes.Any(r => r.Name.Equals(conditionName, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add(new ValidationError
                {
                    Message = $"Route condition '{conditionName}' has no matching destination (missing '{conditionName} -> output(...)' definition)",
                    Line = 0,
                    Column = 0
                });
            }
        }
    }

    private void ValidateFlowStructure(Flow flow, ValidationResult result)
    {
        // Check if pipeline has any steps
        if (flow.PipelineSteps.Count == 0)
        {
            result.Errors.Add(new ValidationError
            {
                Message = "Flow has no pipeline steps. At least one transform or output is required",
                Line = 0,
                Column = 0
            });
            return;
        }

        // Check if there's an output destination
        var hasRouteStep = flow.PipelineSteps.Any(s => s.Type.Equals("route", StringComparison.OrdinalIgnoreCase));
        var lastStep = flow.PipelineSteps[^1];
        var lastStepIsOutput = _knownOutputPlugins.Contains(lastStep.Type);
        var hasRouteDestinations = flow.Routes.Any(r => r.DestinationSteps.Count > 0);

        if (!lastStepIsOutput && !hasRouteDestinations)
        {
            // No output at end and no route destinations
            if (hasRouteStep)
            {
                result.Errors.Add(new ValidationError
                {
                    Message = "Flow has a route block but no route destinations defined. Add 'route_name -> output(...)' for each route condition",
                    Line = 0,
                    Column = 0
                });
            }
            else
            {
                result.Errors.Add(new ValidationError
                {
                    Message = "Flow has no output. Add an output plugin (console, http, mqtt, sql) at the end of the pipeline",
                    Line = 0,
                    Column = 0
                });
            }
        }
    }

    /// <summary>
    /// Finds the most similar plugin name using Levenshtein distance.
    /// </summary>
    private static string? FindSimilarPlugin(string input, IEnumerable<string> candidates)
    {
        string? bestMatch = null;
        int bestDistance = int.MaxValue;
        const int maxDistance = 3; // Only suggest if within 3 edits

        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(input.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance && distance <= maxDistance)
            {
                bestDistance = distance;
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (int i = 0; i <= m; i++)
            d[i, 0] = i;
        for (int j = 0; j <= n; j++)
            d[0, j] = j;

        for (int j = 1; j <= n; j++)
        {
            for (int i = 1; i <= m; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}

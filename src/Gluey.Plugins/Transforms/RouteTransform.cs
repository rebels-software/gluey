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

using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;

namespace Gluey.Plugins.Transforms;

/// <summary>
/// Transform plugin that routes messages to different outputs based on conditions.
/// Evaluates multiple route conditions in order; first matching route wins.
/// Sets the route destination in message metadata for WorkflowRunner to use.
/// </summary>
/// <remarks>
/// Supports:
/// - Multiple route conditions evaluated in order
/// - First matching route wins
/// - * (asterisk) as catch-all/default route
/// - Sets "_route" metadata with the matching route name
/// </remarks>
public sealed class RouteTransform : ITransformPlugin
{
    /// <summary>
    /// Metadata key used to store the matched route name.
    /// </summary>
    public const string RouteMetadataKey = "_route";

    private readonly List<RouteCondition> _routes = [];

    public string Type => "route";

    /// <summary>
    /// Initializes the route transform with configuration.
    /// Configuration is a map of route names to condition expressions.
    /// </summary>
    /// <param name="config">Configuration containing route name -> condition mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Config format: { "route_name": "condition_expression", ... }
        // Conditions are evaluated in order; first match wins
        // Special condition "*" is the catch-all (default) route
        foreach (var kvp in config)
        {
            var routeName = kvp.Key;
            var condition = kvp.Value.ValueKind == JsonValueKind.String
                ? kvp.Value.GetString() ?? "*"
                : "*";

            _routes.Add(new RouteCondition(routeName, condition));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by evaluating route conditions and setting the destination.
    /// </summary>
    /// <param name="message">The input message to route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with "_route" metadata set to the matching route name.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_routes.Count == 0)
        {
            // No routes configured - pass through
            return Task.FromResult<Message?>(message);
        }

        // Evaluate conditions in order; first match wins
        foreach (var route in _routes)
        {
            if (EvaluateCondition(route.Condition, message.Payload.RootElement))
            {
                // Set the route destination in metadata
                var routedMessage = message.WithMetadata(RouteMetadataKey, route.Name);
                return Task.FromResult<Message?>(routedMessage);
            }
        }

        // No route matched - pass through without route metadata
        // WorkflowRunner can decide what to do (use default output or drop)
        return Task.FromResult<Message?>(message);
    }

    /// <summary>
    /// Evaluates a condition expression against a JSON element.
    /// </summary>
    /// <param name="condition">The condition expression to evaluate.</param>
    /// <param name="root">The JSON root element containing message data.</param>
    /// <returns>True if condition matches, false otherwise.</returns>
    private static bool EvaluateCondition(string condition, JsonElement root)
    {
        // Special case: * (catch-all) always matches
        if (condition == "*")
        {
            return true;
        }

        // Empty condition also matches all (catch-all behavior)
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        try
        {
            var evaluator = new ExpressionEvaluator(root);
            return evaluator.EvaluateBoolean(condition);
        }
        catch
        {
            // On evaluation error, don't match this route
            return false;
        }
    }

    /// <summary>
    /// Represents a single route condition.
    /// </summary>
    private sealed record RouteCondition(string Name, string Condition);

    /// <summary>
    /// Simple expression evaluator for route conditions.
    /// Supports: comparisons, logical operators, field access.
    /// </summary>
    /// <remarks>
    /// This is intentionally duplicated from FilterTransform to keep the plugins
    /// self-contained and avoid cross-plugin dependencies. In a larger codebase,
    /// this could be extracted to a shared utility.
    /// </remarks>
    private sealed class ExpressionEvaluator
    {
        private readonly JsonElement _root;
        private string _expression = string.Empty;
        private int _position;

        public ExpressionEvaluator(JsonElement root)
        {
            _root = root;
        }

        public bool EvaluateBoolean(string expression)
        {
            _expression = expression;
            _position = 0;
            return ParseOrExpression();
        }

        private char Current => _position < _expression.Length ? _expression[_position] : '\0';

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position]))
            {
                _position++;
            }
        }

        private bool Match(string s)
        {
            SkipWhitespace();
            if (_position + s.Length <= _expression.Length &&
                _expression.Substring(_position, s.Length) == s)
            {
                // Check that it's not part of a longer identifier
                var nextPos = _position + s.Length;
                if (nextPos < _expression.Length && (char.IsLetterOrDigit(_expression[nextPos]) || _expression[nextPos] == '_'))
                {
                    return false;
                }
                _position += s.Length;
                return true;
            }
            return false;
        }

        private bool MatchOperator(string op)
        {
            SkipWhitespace();
            if (_position + op.Length <= _expression.Length &&
                _expression.Substring(_position, op.Length) == op)
            {
                _position += op.Length;
                return true;
            }
            return false;
        }

        // Parse || (lowest precedence)
        private bool ParseOrExpression()
        {
            var left = ParseAndExpression();

            while (MatchOperator("||"))
            {
                var right = ParseAndExpression();
                left = left || right;
            }

            return left;
        }

        // Parse && (higher precedence than ||)
        private bool ParseAndExpression()
        {
            var left = ParseComparison();

            while (MatchOperator("&&"))
            {
                var right = ParseComparison();
                left = left && right;
            }

            return left;
        }

        // Parse comparison expressions: a > b, a == b, etc.
        private bool ParseComparison()
        {
            SkipWhitespace();

            // Handle parentheses
            if (Current == '(')
            {
                _position++;
                var result = ParseOrExpression();
                SkipWhitespace();
                if (Current == ')')
                {
                    _position++;
                }
                return result;
            }

            // Handle boolean literals
            if (Match("true"))
            {
                return true;
            }
            if (Match("false"))
            {
                return false;
            }

            // Parse left operand
            var left = ParseValue();

            SkipWhitespace();

            // Check for comparison operators
            string? op = null;
            if (MatchOperator(">=")) op = ">=";
            else if (MatchOperator("<=")) op = "<=";
            else if (MatchOperator("!=")) op = "!=";
            else if (MatchOperator("==")) op = "==";
            else if (MatchOperator(">")) op = ">";
            else if (MatchOperator("<")) op = "<";

            if (op == null)
            {
                // No operator - treat as boolean value
                return ConvertToBoolean(left);
            }

            // Parse right operand
            var right = ParseValue();

            return Compare(left, right, op);
        }

        // Parse a value (field reference, number, string literal)
        private object? ParseValue()
        {
            SkipWhitespace();

            // String literal (single or double quoted)
            if (Current == '"' || Current == '\'')
            {
                return ParseStringLiteral();
            }

            // Number
            if (char.IsDigit(Current) || Current == '-' || Current == '+')
            {
                return ParseNumber();
            }

            // Boolean literals
            if (Match("true"))
            {
                return true;
            }
            if (Match("false"))
            {
                return false;
            }

            // Null literal
            if (Match("null"))
            {
                return null;
            }

            // Field reference (identifier with optional dots)
            return ParseFieldValue();
        }

        private string ParseStringLiteral()
        {
            var quote = Current;
            _position++; // skip opening quote

            var start = _position;
            while (_position < _expression.Length && _expression[_position] != quote)
            {
                if (_expression[_position] == '\\' && _position + 1 < _expression.Length)
                {
                    _position++; // skip escape char
                }
                _position++;
            }

            var value = _expression.Substring(start, _position - start);
            if (_position < _expression.Length)
            {
                _position++; // skip closing quote
            }

            // Handle escape sequences
            return value.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private double ParseNumber()
        {
            var start = _position;
            if (Current == '-' || Current == '+')
            {
                _position++;
            }

            while (_position < _expression.Length && (char.IsDigit(_expression[_position]) || _expression[_position] == '.'))
            {
                _position++;
            }

            var numberStr = _expression.Substring(start, _position - start);
            return double.TryParse(numberStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
                ? result
                : 0;
        }

        private object? ParseFieldValue()
        {
            var fieldPath = ParseIdentifier();
            if (string.IsNullOrEmpty(fieldPath))
            {
                return null;
            }

            return GetFieldValue(_root, fieldPath);
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            var start = _position;

            // First character must be letter or underscore
            if (_position < _expression.Length && (char.IsLetter(_expression[_position]) || _expression[_position] == '_'))
            {
                _position++;

                // Subsequent characters can include digits and dots
                while (_position < _expression.Length &&
                       (char.IsLetterOrDigit(_expression[_position]) ||
                        _expression[_position] == '_' ||
                        _expression[_position] == '.'))
                {
                    _position++;
                }
            }

            return _expression.Substring(start, _position - start);
        }

        private static object? GetFieldValue(JsonElement element, string fieldPath)
        {
            var parts = fieldPath.Split('.');
            var current = element;

            foreach (var part in parts)
            {
                if (current.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!current.TryGetProperty(part, out current))
                {
                    return null;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => current.GetRawText()
            };
        }

        private static bool Compare(object? left, object? right, string op)
        {
            // Handle null comparisons
            if (left == null || right == null)
            {
                return op switch
                {
                    "==" => left == null && right == null,
                    "!=" => !(left == null && right == null),
                    _ => false
                };
            }

            // Try numeric comparison
            if (TryGetNumeric(left, out var leftNum) && TryGetNumeric(right, out var rightNum))
            {
                return op switch
                {
                    ">" => leftNum > rightNum,
                    "<" => leftNum < rightNum,
                    ">=" => leftNum >= rightNum,
                    "<=" => leftNum <= rightNum,
                    "==" => Math.Abs(leftNum - rightNum) < 0.0000001,
                    "!=" => Math.Abs(leftNum - rightNum) >= 0.0000001,
                    _ => false
                };
            }

            // String comparison
            var leftStr = left?.ToString() ?? string.Empty;
            var rightStr = right?.ToString() ?? string.Empty;

            return op switch
            {
                "==" => string.Equals(leftStr, rightStr, StringComparison.Ordinal),
                "!=" => !string.Equals(leftStr, rightStr, StringComparison.Ordinal),
                ">" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) > 0,
                "<" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) < 0,
                ">=" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) >= 0,
                "<=" => string.Compare(leftStr, rightStr, StringComparison.Ordinal) <= 0,
                _ => false
            };
        }

        private static bool TryGetNumeric(object? value, out double result)
        {
            if (value is double d)
            {
                result = d;
                return true;
            }
            if (value is int i)
            {
                result = i;
                return true;
            }
            if (value is long l)
            {
                result = l;
                return true;
            }
            if (value is float f)
            {
                result = f;
                return true;
            }
            if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private static bool ConvertToBoolean(object? value)
        {
            if (value is bool b)
            {
                return b;
            }
            if (value is double d)
            {
                return d != 0;
            }
            if (value is string s)
            {
                return !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            return value != null;
        }
    }
}

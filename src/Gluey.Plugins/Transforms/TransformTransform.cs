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
/// Transform plugin that creates a new payload from field mappings.
/// Each mapping is an expression that can reference input fields, perform arithmetic,
/// use ternary conditionals, or call built-in functions.
/// </summary>
/// <remarks>
/// Supports:
/// - Direct field copy: device_id: device_id
/// - Nested field access: lat: device.location.lat
/// - Arithmetic: temp_f: temperature * 9/5 + 32
/// - Ternary: level: temp > 30 ? "high" : "normal"
/// - Built-in functions: now()
/// </remarks>
public sealed class TransformTransform : ITransformPlugin
{
    private Dictionary<string, string> _mappings = new();

    public string Type => "transform";

    /// <summary>
    /// Initializes the transform with field mappings from configuration.
    /// </summary>
    /// <param name="config">Configuration containing field name -> expression mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in config)
        {
            if (kvp.Value.ValueKind == JsonValueKind.String)
            {
                _mappings[kvp.Key] = kvp.Value.GetString() ?? string.Empty;
            }
            else
            {
                // For non-string values, convert to string representation
                _mappings[kvp.Key] = kvp.Value.ToString();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by creating a new payload from field mappings.
    /// </summary>
    /// <param name="message">The input message to transform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with transformed payload.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_mappings.Count == 0)
        {
            // No mappings means pass through
            return Task.FromResult<Message?>(message);
        }

        try
        {
            var evaluator = new TransformExpressionEvaluator(message.Payload.RootElement);
            var result = new Dictionary<string, object?>();

            foreach (var mapping in _mappings)
            {
                var value = evaluator.Evaluate(mapping.Value);
                result[mapping.Key] = value;
            }

            // Create new JsonDocument from result
            var jsonString = JsonSerializer.Serialize(result);
            var newPayload = JsonDocument.Parse(jsonString);

            return Task.FromResult<Message?>(message.WithPayload(newPayload));
        }
        catch
        {
            // On evaluation error, return null to filter out
            return Task.FromResult<Message?>(null);
        }
    }

    /// <summary>
    /// Expression evaluator for transform mappings.
    /// Supports field access, arithmetic, comparisons, ternary, and function calls.
    /// </summary>
    private sealed class TransformExpressionEvaluator
    {
        private readonly JsonElement _root;
        private string _expression = string.Empty;
        private int _position;

        public TransformExpressionEvaluator(JsonElement root)
        {
            _root = root;
        }

        public object? Evaluate(string expression)
        {
            _expression = expression;
            _position = 0;
            return ParseTernary();
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
                // Check that it's not part of a longer identifier (for keywords)
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

        // Parse ternary: condition ? true_value : false_value
        private object? ParseTernary()
        {
            var condition = ParseOrExpression();

            SkipWhitespace();
            if (MatchOperator("?"))
            {
                var trueValue = ParseTernary();
                SkipWhitespace();
                if (!MatchOperator(":"))
                {
                    throw new InvalidOperationException("Expected ':' in ternary expression");
                }
                var falseValue = ParseTernary();

                return ConvertToBoolean(condition) ? trueValue : falseValue;
            }

            return condition;
        }

        // Parse || (lowest precedence among boolean)
        private object? ParseOrExpression()
        {
            var left = ParseAndExpression();

            while (MatchOperator("||"))
            {
                var right = ParseAndExpression();
                left = ConvertToBoolean(left) || ConvertToBoolean(right);
            }

            return left;
        }

        // Parse && (higher precedence than ||)
        private object? ParseAndExpression()
        {
            var left = ParseComparison();

            while (MatchOperator("&&"))
            {
                var right = ParseComparison();
                left = ConvertToBoolean(left) && ConvertToBoolean(right);
            }

            return left;
        }

        // Parse comparison expressions: a > b, a == b, etc.
        private object? ParseComparison()
        {
            var left = ParseAdditive();

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
                return left;
            }

            var right = ParseAdditive();
            return Compare(left, right, op);
        }

        // Parse addition/subtraction
        private object? ParseAdditive()
        {
            var left = ParseMultiplicative();

            while (true)
            {
                SkipWhitespace();
                if (MatchOperator("+"))
                {
                    var right = ParseMultiplicative();
                    if (TryGetNumeric(left, out var leftNum) && TryGetNumeric(right, out var rightNum))
                    {
                        left = leftNum + rightNum;
                    }
                    else
                    {
                        // String concatenation
                        left = (left?.ToString() ?? "") + (right?.ToString() ?? "");
                    }
                }
                else if (MatchOperator("-"))
                {
                    var right = ParseMultiplicative();
                    if (TryGetNumeric(left, out var leftNum) && TryGetNumeric(right, out var rightNum))
                    {
                        left = leftNum - rightNum;
                    }
                    else
                    {
                        left = 0.0; // Invalid subtraction
                    }
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        // Parse multiplication/division
        private object? ParseMultiplicative()
        {
            var left = ParseUnary();

            while (true)
            {
                SkipWhitespace();
                if (MatchOperator("*"))
                {
                    var right = ParseUnary();
                    if (TryGetNumeric(left, out var leftNum) && TryGetNumeric(right, out var rightNum))
                    {
                        left = leftNum * rightNum;
                    }
                    else
                    {
                        left = 0.0;
                    }
                }
                else if (MatchOperator("/"))
                {
                    var right = ParseUnary();
                    if (TryGetNumeric(left, out var leftNum) && TryGetNumeric(right, out var rightNum) && rightNum != 0)
                    {
                        left = leftNum / rightNum;
                    }
                    else
                    {
                        left = 0.0;
                    }
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        // Parse unary operators (negation, not)
        private object? ParseUnary()
        {
            SkipWhitespace();

            if (MatchOperator("!"))
            {
                var value = ParseUnary();
                return !ConvertToBoolean(value);
            }

            if (Current == '-')
            {
                // Check if this is a negative number or subtraction
                // Peek ahead to see if next char is digit
                if (_position + 1 < _expression.Length && char.IsDigit(_expression[_position + 1]))
                {
                    return ParsePrimary();
                }
            }

            return ParsePrimary();
        }

        // Parse primary values: literals, field references, function calls, parentheses
        private object? ParsePrimary()
        {
            SkipWhitespace();

            // Parentheses
            if (Current == '(')
            {
                _position++;
                var result = ParseTernary();
                SkipWhitespace();
                if (Current == ')')
                {
                    _position++;
                }
                return result;
            }

            // String literal (double quoted)
            if (Current == '"')
            {
                return ParseStringLiteral();
            }

            // Number (including negative)
            if (char.IsDigit(Current) || (Current == '-' && _position + 1 < _expression.Length && char.IsDigit(_expression[_position + 1])))
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

            // Function call or field reference
            return ParseIdentifierOrFunction();
        }

        private string ParseStringLiteral()
        {
            _position++; // skip opening quote

            var start = _position;
            while (_position < _expression.Length && _expression[_position] != '"')
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
            return value.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private double ParseNumber()
        {
            var start = _position;
            if (Current == '-')
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

        private object? ParseIdentifierOrFunction()
        {
            var identifier = ParseIdentifier();
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            SkipWhitespace();

            // Check for function call: identifier()
            if (Current == '(')
            {
                _position++; // consume '('
                SkipWhitespace();

                // For now, only support no-argument functions
                if (Current == ')')
                {
                    _position++; // consume ')'
                    return EvaluateFunction(identifier);
                }

                // Skip to closing paren if there are arguments (not fully supported yet)
                while (_position < _expression.Length && Current != ')')
                {
                    _position++;
                }
                if (Current == ')')
                {
                    _position++;
                }
                return EvaluateFunction(identifier);
            }

            // Field reference
            return GetFieldValue(_root, identifier);
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            var start = _position;

            // First character must be letter or underscore
            if (_position < _expression.Length && (char.IsLetter(_expression[_position]) || _expression[_position] == '_'))
            {
                _position++;

                // Subsequent characters can include digits and dots (for nested access)
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

        private object? EvaluateFunction(string functionName)
        {
            return functionName.ToLowerInvariant() switch
            {
                "now" => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), // ISO 8601 format
                "uuid" => Guid.NewGuid().ToString(),
                "timestamp" => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _ => null
            };
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
                JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText()),
                JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(current.GetRawText()),
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

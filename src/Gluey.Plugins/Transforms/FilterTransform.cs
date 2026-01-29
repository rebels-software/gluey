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
/// Transform plugin that filters messages based on condition expressions.
/// Returns the message if the condition evaluates to true, null if false.
/// </summary>
/// <remarks>
/// Supports:
/// - Comparisons: > &lt; >= &lt;= == !=
/// - Logical operators: &amp;&amp; ||
/// - Nested field access: device.location.type
/// - Literal values: numbers, strings (quoted), booleans
/// </remarks>
public sealed class FilterTransform : ITransformPlugin
{
    private string _condition = string.Empty;

    public string Type => "filter";

    /// <summary>
    /// Initializes the filter transform with configuration.
    /// </summary>
    /// <param name="config">Configuration containing 'condition' expression.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        if (config.TryGetValue("condition", out var conditionElement) &&
            conditionElement.ValueKind == JsonValueKind.String)
        {
            _condition = conditionElement.GetString() ?? string.Empty;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by evaluating the filter condition.
    /// </summary>
    /// <param name="message">The input message to filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message if condition is true, null if false (filtered out).</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_condition))
        {
            // No condition means pass through
            return Task.FromResult<Message?>(message);
        }

        try
        {
            var result = EvaluateCondition(_condition, message.Payload.RootElement);
            return Task.FromResult<Message?>(result ? message : null);
        }
        catch
        {
            // On evaluation error, filter out the message
            return Task.FromResult<Message?>(null);
        }
    }

    /// <summary>
    /// Evaluates a condition expression against a JSON element.
    /// </summary>
    private bool EvaluateCondition(string condition, JsonElement root)
    {
        var evaluator = new ExpressionEvaluator(root);
        return evaluator.EvaluateBoolean(condition);
    }

    /// <summary>
    /// Simple expression evaluator for filter conditions.
    /// Supports: comparisons, logical operators, field access.
    /// </summary>
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

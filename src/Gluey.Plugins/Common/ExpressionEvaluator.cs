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

namespace Gluey.Plugins.Common;

/// <summary>
/// Shared expression evaluator for transform, filter, and route plugins.
/// Supports field access, arithmetic, comparisons, ternary, function calls,
/// metadata access via $meta.key, and string methods (split, substring, indexOf).
/// </summary>
public sealed class ExpressionEvaluator
{
    private readonly JsonElement _root;
    private readonly IReadOnlyDictionary<string, string> _metadata;
    private string _expression = string.Empty;
    private int _position;

    /// <summary>
    /// Creates an expression evaluator with payload and optional metadata.
    /// </summary>
    /// <param name="root">The JSON root element containing message payload.</param>
    /// <param name="metadata">Optional metadata dictionary for $meta.key access.</param>
    public ExpressionEvaluator(JsonElement root, IReadOnlyDictionary<string, string>? metadata = null)
    {
        _root = root;
        _metadata = metadata ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Evaluates an expression and returns the result.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The evaluated result (can be any type).</returns>
    public object? Evaluate(string expression)
    {
        _expression = expression;
        _position = 0;
        return ParseTernary();
    }

    /// <summary>
    /// Evaluates an expression and returns a boolean result.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>True if the expression evaluates to true.</returns>
    public bool EvaluateBoolean(string expression)
    {
        var result = Evaluate(expression);
        return ConvertToBoolean(result);
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

        // Single quoted string literal
        if (Current == '\'')
        {
            return ParseSingleQuotedStringLiteral();
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

        // Metadata access ($meta.key)
        if (Current == '$')
        {
            return ParseMetadataAccess();
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

    private string ParseSingleQuotedStringLiteral()
    {
        _position++; // skip opening quote

        var start = _position;
        while (_position < _expression.Length && _expression[_position] != '\'')
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
        return value.Replace("\\'", "'").Replace("\\n", "\n").Replace("\\t", "\t");
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

    /// <summary>
    /// Parses metadata access: $meta.key or $meta.key.method()
    /// </summary>
    private object? ParseMetadataAccess()
    {
        _position++; // skip '$'

        // Expect "meta"
        if (!Match("meta"))
        {
            return null;
        }

        SkipWhitespace();

        // Expect dot
        if (Current != '.')
        {
            return null;
        }
        _position++; // skip '.'

        // Parse metadata key (can be nested like meta.key.subkey, but we only support single level)
        var key = ParseSimpleIdentifier();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        // Get the metadata value
        object? value = _metadata.TryGetValue(key, out var metaValue) ? metaValue : null;

        // Check for method calls or array indexing on the result
        value = ParseMethodChainOrIndex(value);

        return value;
    }

    /// <summary>
    /// Parses method calls and array indexing on a value.
    /// e.g., .split('/')[1] or .substring(0, 5)
    /// </summary>
    private object? ParseMethodChainOrIndex(object? value)
    {
        while (true)
        {
            SkipWhitespace();

            // Array indexing: [n]
            if (Current == '[')
            {
                _position++; // skip '['
                var indexExpr = ParseTernary();
                SkipWhitespace();
                if (Current == ']')
                {
                    _position++; // skip ']'
                }

                if (TryGetNumeric(indexExpr, out var indexNum))
                {
                    var index = (int)indexNum;
                    if (value is string[] arr && index >= 0 && index < arr.Length)
                    {
                        value = arr[index];
                    }
                    else if (value is List<object?> list && index >= 0 && index < list.Count)
                    {
                        value = list[index];
                    }
                    else
                    {
                        value = null;
                    }
                }
                else
                {
                    value = null;
                }
                continue;
            }

            // Method call: .methodName(args)
            if (Current == '.')
            {
                _position++; // skip '.'
                var methodName = ParseSimpleIdentifier();
                if (string.IsNullOrEmpty(methodName))
                {
                    break;
                }

                SkipWhitespace();
                if (Current == '(')
                {
                    _position++; // skip '('
                    var args = ParseFunctionArguments();

                    value = EvaluateStringMethod(value, methodName, args);
                    continue;
                }
                else
                {
                    // Not a method call, might be nested property - restore position and break
                    // For now we don't support nested property access after $meta
                    break;
                }
            }

            break;
        }

        return value;
    }

    /// <summary>
    /// Parses function arguments separated by commas.
    /// </summary>
    private List<object?> ParseFunctionArguments()
    {
        var args = new List<object?>();

        SkipWhitespace();
        if (Current == ')')
        {
            _position++; // skip ')'
            return args;
        }

        while (true)
        {
            args.Add(ParseTernary());
            SkipWhitespace();

            if (Current == ',')
            {
                _position++; // skip ','
                continue;
            }

            if (Current == ')')
            {
                _position++; // skip ')'
                break;
            }

            break;
        }

        return args;
    }

    /// <summary>
    /// Evaluates string methods: split, substring, indexOf.
    /// </summary>
    private object? EvaluateStringMethod(object? value, string methodName, List<object?> args)
    {
        var str = value?.ToString() ?? string.Empty;

        return methodName.ToLowerInvariant() switch
        {
            "split" => EvaluateSplit(str, args),
            "substring" => EvaluateSubstring(str, args),
            "indexof" => EvaluateIndexOf(str, args),
            "tolower" or "tolowercase" => str.ToLowerInvariant(),
            "toupper" or "touppercase" => str.ToUpperInvariant(),
            "trim" => str.Trim(),
            "length" => (double)str.Length,
            _ => null
        };
    }

    /// <summary>
    /// Evaluates string.split(delimiter) - returns string array.
    /// </summary>
    private string[] EvaluateSplit(string str, List<object?> args)
    {
        if (args.Count == 0)
        {
            return [str];
        }

        var delimiter = args[0]?.ToString() ?? "";
        if (string.IsNullOrEmpty(delimiter))
        {
            return [str];
        }

        return str.Split(delimiter);
    }

    /// <summary>
    /// Evaluates string.substring(start) or string.substring(start, length).
    /// </summary>
    private string EvaluateSubstring(string str, List<object?> args)
    {
        if (args.Count == 0)
        {
            return str;
        }

        if (!TryGetNumeric(args[0], out var startNum))
        {
            return str;
        }

        var start = (int)startNum;
        if (start < 0) start = 0;
        if (start >= str.Length) return string.Empty;

        if (args.Count >= 2 && TryGetNumeric(args[1], out var lengthNum))
        {
            var length = (int)lengthNum;
            if (length <= 0) return string.Empty;
            if (start + length > str.Length) length = str.Length - start;
            return str.Substring(start, length);
        }

        return str.Substring(start);
    }

    /// <summary>
    /// Evaluates string.indexOf(search) - returns index or -1.
    /// </summary>
    private double EvaluateIndexOf(string str, List<object?> args)
    {
        if (args.Count == 0)
        {
            return -1;
        }

        var search = args[0]?.ToString() ?? "";
        return str.IndexOf(search, StringComparison.Ordinal);
    }

    private object? ParseIdentifierOrFunction()
    {
        // Parse the first identifier segment
        var firstSegment = ParseSimpleIdentifier();
        if (string.IsNullOrEmpty(firstSegment))
        {
            return null;
        }

        SkipWhitespace();

        // Check for function call: identifier()
        if (Current == '(')
        {
            _position++; // consume '('
            var args = ParseFunctionArguments();
            return EvaluateFunction(firstSegment, args);
        }

        // Build the field path and handle method chains
        // Parse the field path segment by segment, stopping when we hit a method call
        var fieldPath = new List<string> { firstSegment };

        while (Current == '.')
        {
            // Save position in case we need to back up
            var savedPosition = _position;
            _position++; // skip '.'

            var nextSegment = ParseSimpleIdentifier();
            if (string.IsNullOrEmpty(nextSegment))
            {
                // Invalid - restore position and break
                _position = savedPosition;
                break;
            }

            SkipWhitespace();

            // Is this a method call?
            if (Current == '(')
            {
                // This is a method call - restore position to just before the '.'
                // and let ParseMethodChainOrIndex handle it
                _position = savedPosition;
                break;
            }

            // It's a nested field - add to path
            fieldPath.Add(nextSegment);
        }

        // Get the field value
        var value = GetFieldValueByPath(_root, fieldPath);

        // Check for method calls on the field value
        value = ParseMethodChainOrIndex(value);

        return value;
    }

    /// <summary>
    /// Gets a field value from JSON by path segments.
    /// </summary>
    private static object? GetFieldValueByPath(JsonElement element, List<string> pathSegments)
    {
        var current = element;

        foreach (var segment in pathSegments)
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number when current.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number when current.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number => current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText()),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(current.GetRawText()),
            _ => current.GetRawText()
        };
    }

    /// <summary>
    /// Parses a simple identifier (no dots).
    /// </summary>
    private string ParseSimpleIdentifier()
    {
        SkipWhitespace();
        var start = _position;

        if (_position < _expression.Length && (char.IsLetter(_expression[_position]) || _expression[_position] == '_'))
        {
            _position++;

            while (_position < _expression.Length &&
                   (char.IsLetterOrDigit(_expression[_position]) || _expression[_position] == '_'))
            {
                _position++;
            }
        }

        return _expression.Substring(start, _position - start);
    }

    /// <summary>
    /// Parses an identifier that may contain dots (for nested field access).
    /// Used for backward compatibility in places that don't need method chain detection.
    /// </summary>
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

    private object? EvaluateFunction(string functionName, List<object?> args)
    {
        return functionName.ToLowerInvariant() switch
        {
            "now" => DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"), // ISO 8601 with local timezone offset
            "uuid" => Guid.NewGuid().ToString(),
            "timestamp" => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            "int" => CastToInt(args.Count > 0 ? args[0] : null),
            "float" => CastToFloat(args.Count > 0 ? args[0] : null),
            "string" => CastToString(args.Count > 0 ? args[0] : null),
            _ => null
        };
    }

    /// <summary>
    /// Casts a value to integer (long). Handles doubles (truncate), strings (parse),
    /// booleans (1/0), and null (0).
    /// </summary>
    private static object? CastToInt(object? value) => value switch
    {
        null => 0L,
        long l => l,
        double d => (long)d,
        int i => (long)i,
        bool b => b ? 1L : 0L,
        string s => long.TryParse(s, out var result) ? result :
                    double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d) ? (long)d : 0L,
        _ => 0L
    };

    /// <summary>
    /// Casts a value to floating-point (double). Handles ints, strings (parse),
    /// booleans (1.0/0.0), and null (0.0).
    /// </summary>
    private static object? CastToFloat(object? value) => value switch
    {
        null => 0.0,
        double d => d,
        long l => (double)l,
        int i => (double)i,
        bool b => b ? 1.0 : 0.0,
        string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0.0,
        _ => 0.0
    };

    /// <summary>
    /// Casts a value to string. Returns empty string for null.
    /// </summary>
    private static object? CastToString(object? value) => value?.ToString() ?? "";

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
            JsonValueKind.Number when current.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number when current.TryGetInt64(out var longVal) => longVal,
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
        if (value is int i)
        {
            return i != 0;
        }
        if (value is long l)
        {
            return l != 0;
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

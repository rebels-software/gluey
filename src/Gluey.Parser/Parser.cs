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
using Gluey.Core.Models;

namespace Gluey.Parser;

/// <summary>
/// Exception thrown when parsing fails with location information.
/// </summary>
public sealed class ParseException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public ParseException(string message, int line, int column)
        : base($"{message} at line {line}, column {column}")
    {
        Line = line;
        Column = column;
    }

    public ParseException(string message, Token token)
        : this(message, token.Line, token.Column)
    {
    }
}

/// <summary>
/// Recursive descent parser for the .gflow DSL.
/// Converts tokens from the Lexer into a Flow AST.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _current;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _current = 0;
    }

    /// <summary>
    /// Parses the tokens and returns a Flow AST.
    /// </summary>
    public Flow Parse()
    {
        return ParseFlow();
    }

    /// <summary>
    /// Parses: flow <name> v<version> { ... }
    /// </summary>
    private Flow ParseFlow()
    {
        // Expect 'flow' keyword
        Expect(TokenType.Flow, "Expected 'flow' keyword");

        // Parse flow name (identifier or hyphenated name like "hello-world")
        var name = ParseFlowName();

        // Parse version: v<version> (e.g., v1.0)
        var version = ParseVersion();

        // Expect opening brace
        Expect(TokenType.LeftBrace, "Expected '{' after flow declaration");

        // Parse flow body
        InputNode? input = null;
        var pipelineSteps = new List<PipelineStep>();
        var routes = new List<Route>();

        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Check(TokenType.From))
            {
                if (input != null)
                {
                    throw new ParseException("Multiple 'from' blocks are not allowed", Current());
                }
                input = ParseInputBlock();
            }
            else if (Check(TokenType.Pipe))
            {
                pipelineSteps.Add(ParsePipelineStep());
            }
            else if (Check(TokenType.Route))
            {
                // Route parsing will be implemented in US-006
                throw new ParseException("Route blocks not yet implemented", Current());
            }
            else
            {
                throw new ParseException($"Unexpected token '{Current().Value}'", Current());
            }
        }

        // Expect closing brace
        Expect(TokenType.RightBrace, "Expected '}' to close flow block");

        if (input == null)
        {
            throw new ParseException("Flow must have a 'from' input block", _tokens[0]);
        }

        return new Flow
        {
            Name = name,
            Version = version,
            Input = input,
            PipelineSteps = pipelineSteps,
            Routes = routes
        };
    }

    /// <summary>
    /// Parses a flow name which can be a simple identifier or hyphenated (e.g., "hello-world").
    /// </summary>
    private string ParseFlowName()
    {
        var nameParts = new List<string>();

        // First part must be an identifier
        var firstToken = Expect(TokenType.Identifier, "Expected flow name");
        nameParts.Add(firstToken.Value);

        // Allow hyphenated names like "hello-world"
        while (Check(TokenType.Minus))
        {
            Advance(); // consume '-'
            var nextPart = Expect(TokenType.Identifier, "Expected identifier after '-' in flow name");
            nameParts.Add(nextPart.Value);
        }

        return string.Join("-", nameParts);
    }

    /// <summary>
    /// Parses version: v<version> (e.g., v1.0, v2.1)
    /// The version is an identifier starting with 'v' followed by a number.
    /// </summary>
    private string ParseVersion()
    {
        var token = Current();

        // Version should be an identifier like "v1" followed by optional ".0"
        if (token.Type != TokenType.Identifier || !token.Value.StartsWith('v'))
        {
            throw new ParseException("Expected version (e.g., 'v1.0')", token);
        }

        Advance();
        var version = token.Value.Substring(1); // Remove 'v' prefix

        // Check for decimal version (v1.0)
        if (Check(TokenType.Dot))
        {
            Advance(); // consume '.'
            var minorToken = Expect(TokenType.Number, "Expected minor version number after '.'");
            version += "." + minorToken.Value;
        }

        return version;
    }

    /// <summary>
    /// Parses: from <type>("url") { key: value, ... }
    /// </summary>
    private InputNode ParseInputBlock()
    {
        Expect(TokenType.From, "Expected 'from' keyword");

        // Parse input type (e.g., http, mqtt, kafka)
        var typeToken = Expect(TokenType.Identifier, "Expected input type (e.g., 'http', 'mqtt')");
        var inputType = typeToken.Value;

        // Parse optional URL in parentheses: ("url")
        string? url = null;
        if (Check(TokenType.LeftParen))
        {
            Advance(); // consume '('
            var urlToken = Expect(TokenType.String, "Expected URL string");
            url = urlToken.Value;
            Expect(TokenType.RightParen, "Expected ')' after URL");
        }

        // Parse optional config block: { key: value, ... }
        var config = new Dictionary<string, JsonElement>();
        if (Check(TokenType.LeftBrace))
        {
            config = ParseConfigBlock();
        }

        return new InputNode
        {
            Type = inputType,
            Url = url,
            Config = config
        };
    }

    /// <summary>
    /// Parses: | <transform>(args) or | <transform> { config }
    /// </summary>
    private PipelineStep ParsePipelineStep()
    {
        Expect(TokenType.Pipe, "Expected '|' for pipeline step");

        // Parse transform type (may be dotted like "json.parse" or "decode.binary")
        var transformType = ParseDottedIdentifier();

        // Parse arguments or config block
        var config = new Dictionary<string, JsonElement>();
        OnErrorConfig? onError = null;

        if (Check(TokenType.LeftParen))
        {
            // Parse parenthesized arguments: (arg1, arg2, ...)
            Advance(); // consume '('

            if (!Check(TokenType.RightParen))
            {
                // For now, treat single argument as "field" config for things like json.parse(payload)
                var args = ParseArgumentList();
                if (args.Count == 1)
                {
                    config["field"] = args[0];
                }
                else
                {
                    // Store as positional args
                    config["args"] = JsonSerializer.SerializeToElement(args.Select(e => e.ToString()).ToArray());
                }
            }

            Expect(TokenType.RightParen, "Expected ')' after arguments");

            // Check for optional config block after parentheses
            if (Check(TokenType.LeftBrace))
            {
                var blockConfig = ParseConfigBlock();
                foreach (var kvp in blockConfig)
                {
                    config[kvp.Key] = kvp.Value;
                }
            }
        }
        else if (Check(TokenType.LeftBrace))
        {
            // Parse config block: { key: value, ... }
            config = ParseConfigBlock();
        }

        // Check for on_error in config
        if (config.TryGetValue("on_error", out var onErrorValue))
        {
            onError = ParseOnErrorValue(onErrorValue);
            config = config.Where(kvp => kvp.Key != "on_error")
                          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        return new PipelineStep
        {
            Type = transformType,
            Config = config,
            OnError = onError
        };
    }

    /// <summary>
    /// Parses a dotted identifier like "json.parse" or "decode.binary".
    /// </summary>
    private string ParseDottedIdentifier()
    {
        // Handle keywords that can also be transform names (filter, transform, route)
        var firstToken = Current();
        if (firstToken.Type == TokenType.Identifier ||
            firstToken.Type == TokenType.Filter ||
            firstToken.Type == TokenType.Transform ||
            firstToken.Type == TokenType.Route)
        {
            Advance();
            var name = firstToken.Value;

            // Check for dotted name (json.parse, decode.binary)
            while (Check(TokenType.Dot))
            {
                Advance(); // consume '.'
                var nextPart = Expect(TokenType.Identifier, "Expected identifier after '.'");
                name += "." + nextPart.Value;
            }

            return name;
        }

        throw new ParseException("Expected transform type", firstToken);
    }

    /// <summary>
    /// Parses a config block: { key: value, key: value, ... }
    /// </summary>
    private Dictionary<string, JsonElement> ParseConfigBlock()
    {
        Expect(TokenType.LeftBrace, "Expected '{'");

        var config = new Dictionary<string, JsonElement>();

        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            // Parse key
            var keyToken = Current();
            string key;

            if (keyToken.Type == TokenType.Identifier || keyToken.Type == TokenType.OnError)
            {
                key = keyToken.Value;
                Advance();
            }
            else
            {
                throw new ParseException("Expected config key", keyToken);
            }

            // Expect colon
            Expect(TokenType.Colon, "Expected ':' after config key");

            // Parse value
            var value = ParseConfigValue();
            config[key] = value;

            // Optional comma between entries
            if (Check(TokenType.Comma))
            {
                Advance();
            }
        }

        Expect(TokenType.RightBrace, "Expected '}'");

        return config;
    }

    /// <summary>
    /// Parses a config value (string, number, boolean, array, or nested object).
    /// </summary>
    private JsonElement ParseConfigValue()
    {
        var token = Current();

        switch (token.Type)
        {
            case TokenType.String:
                Advance();
                return JsonSerializer.SerializeToElement(token.Value);

            case TokenType.Number:
                Advance();
                // Try to parse as int first, then double
                if (token.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    var hexValue = Convert.ToInt64(token.Value, 16);
                    return JsonSerializer.SerializeToElement(hexValue);
                }
                if (token.Value.Contains('.') || token.Value.Contains('e') || token.Value.Contains('E'))
                {
                    return JsonSerializer.SerializeToElement(double.Parse(token.Value));
                }
                return JsonSerializer.SerializeToElement(long.Parse(token.Value));

            case TokenType.True:
                Advance();
                return JsonSerializer.SerializeToElement(true);

            case TokenType.False:
                Advance();
                return JsonSerializer.SerializeToElement(false);

            case TokenType.Null:
                Advance();
                return JsonDocument.Parse("null").RootElement.Clone();

            case TokenType.LeftBracket:
                return ParseArrayValue();

            case TokenType.LeftBrace:
                return ParseObjectValue();

            case TokenType.Identifier:
                // Could be a reference to another field or an enum-like value
                Advance();
                return JsonSerializer.SerializeToElement(token.Value);

            default:
                throw new ParseException($"Unexpected value token '{token.Value}'", token);
        }
    }

    /// <summary>
    /// Parses an array value: [value, value, ...]
    /// </summary>
    private JsonElement ParseArrayValue()
    {
        Expect(TokenType.LeftBracket, "Expected '['");

        var values = new List<JsonElement>();

        while (!Check(TokenType.RightBracket) && !IsAtEnd())
        {
            values.Add(ParseConfigValue());

            if (Check(TokenType.Comma))
            {
                Advance();
            }
        }

        Expect(TokenType.RightBracket, "Expected ']'");

        return JsonSerializer.SerializeToElement(values.Select(v => JsonSerializer.Deserialize<object>(v)).ToArray());
    }

    /// <summary>
    /// Parses a nested object value: { key: value, ... }
    /// </summary>
    private JsonElement ParseObjectValue()
    {
        var config = ParseConfigBlock();
        var dict = config.ToDictionary(kvp => kvp.Key, kvp => JsonSerializer.Deserialize<object>(kvp.Value));
        return JsonSerializer.SerializeToElement(dict);
    }

    /// <summary>
    /// Parses a list of arguments: arg1, arg2, ...
    /// </summary>
    private List<JsonElement> ParseArgumentList()
    {
        var args = new List<JsonElement>();

        args.Add(ParseConfigValue());

        while (Check(TokenType.Comma))
        {
            Advance();
            args.Add(ParseConfigValue());
        }

        return args;
    }

    /// <summary>
    /// Parses on_error config value to OnErrorConfig.
    /// </summary>
    private OnErrorConfig ParseOnErrorValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var strValue = value.GetString()!;
            if (strValue.Equals("skip", StringComparison.OrdinalIgnoreCase))
            {
                return new OnErrorConfig { Action = OnErrorAction.Skip };
            }

            // Check for route_to("name") format stored as string
            if (strValue.StartsWith("route_to(", StringComparison.OrdinalIgnoreCase))
            {
                var routeName = strValue.Substring(9).TrimEnd(')').Trim('"', '\'');
                return new OnErrorConfig { Action = OnErrorAction.RouteTo, RouteName = routeName };
            }
        }

        throw new ParseException($"Invalid on_error value: {value}", _tokens[_current > 0 ? _current - 1 : 0]);
    }

    // ===== Helper Methods =====

    private Token Current()
    {
        if (_current >= _tokens.Count)
            return _tokens[^1]; // Return last token (should be EOF)
        return _tokens[_current];
    }

    private Token Previous()
    {
        return _tokens[_current - 1];
    }

    private bool IsAtEnd()
    {
        return Current().Type == TokenType.Eof;
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Current().Type == type;
    }

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private Token Expect(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new ParseException(message, Current());
    }
}

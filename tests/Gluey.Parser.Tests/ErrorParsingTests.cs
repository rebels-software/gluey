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

using GlueyParser = Gluey.Parser;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for error cases - ensure meaningful error messages with line/column info
/// </summary>
public class ErrorParsingTests
{
    [Fact]
    public void Parse_MissingFlowKeyword_ThrowsParseException()
    {
        var source = @"myworkflow v1.0 { from http(""/webhook"") | console() }";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("flow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingVersion_ThrowsParseException()
    {
        var source = @"flow myworkflow { from http(""/webhook"") | console() }";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingOpenBrace_ThrowsParseException()
    {
        var source = @"flow myworkflow v1.0 from http(""/webhook"") | console() }";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("{", ex.Message);
    }

    [Fact]
    public void Parse_MissingCloseBrace_ThrowsParseException()
    {
        var source = @"flow myworkflow v1.0 { from http(""/webhook"") | console()";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("}", ex.Message);
    }

    [Fact]
    public void Parse_MissingFromBlock_ThrowsParseException()
    {
        var source = @"flow myworkflow v1.0 { | console() }";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("from", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MultipleFromBlocks_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook1"")
  from http(""/webhook2"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains("from", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingInputUrl_ParsesWithoutUrl()
    {
        // Input without URL is valid (just type with config)
        var source = @"
flow test v1.0 {
  from http {
    port: 8080
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("http", flow.Input.Type);
        Assert.Null(flow.Input.Url);
    }

    [Fact]
    public void Parse_ErrorIncludesLineNumber_InMessage()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  |
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.True(ex.Line > 0, "Line number should be set");
        Assert.True(ex.Column > 0, "Column number should be set");
    }

    [Fact]
    public void Parse_MissingColonInConfig_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    port 8080
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.Contains(":", ex.Message);
    }

    [Fact]
    public void Parse_UnterminatedString_ReportsLexerError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();

        // The lexer should produce an Error token for unterminated string
        Assert.Contains(tokens, t => t.Type == TokenType.Error);
    }

    [Fact]
    public void Parse_MissingParenAfterType_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http ""/webhook""
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        // This might parse differently - the string would be unexpected
        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.True(ex.Line > 0);
    }

    [Fact]
    public void Parse_InvalidTokenInExpression_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | filter(temperature @@ 25)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();

        // Check if lexer produces error token for @@
        var hasError = tokens.Any(t => t.Type == TokenType.Error);
        if (hasError)
        {
            Assert.True(true, "Lexer correctly reported error for invalid token");
        }
        else
        {
            // If lexer doesn't error, parser might
            var parser = new GlueyParser.Parser(tokens);
            Assert.Throws<ParseException>(() => parser.Parse());
        }
    }

    [Fact]
    public void Parse_MissingArrowInRoute_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | route {
      alert: level == ""high""
    }
  alert kafka(""alerts"")
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        // Parser expects '->' after route name but gets 'kafka'
        var ex = Assert.Throws<ParseException>(() => parser.Parse());
        Assert.True(ex.Line > 0);
    }

    [Fact]
    public void Parse_EmptyConfigBlock_ParsesSuccessfully()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Empty(flow.Input.Config);
    }

    [Fact]
    public void Parse_NestedBracesMismatch_ThrowsParseException()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") {
    nested: {
      value: 1
  }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        Assert.Throws<ParseException>(() => parser.Parse());
    }

    [Fact]
    public void ParseException_IncludesLineAndColumn_InProperties()
    {
        var source = @"
flow test v1.0 {
  from
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);

        var ex = Assert.Throws<ParseException>(() => parser.Parse());

        // Line and column should be accessible
        Assert.True(ex.Line >= 1);
        Assert.True(ex.Column >= 1);
        Assert.Contains($"line {ex.Line}", ex.Message);
        Assert.Contains($"column {ex.Column}", ex.Message);
    }
}

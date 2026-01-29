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
/// Tests for basic flow parsing: name and version extraction
/// </summary>
public class FlowParsingTests
{
    [Fact]
    public void Parse_SimpleFlow_ExtractsName()
    {
        var source = @"
flow myworkflow v1.0 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("myworkflow", flow.Name);
    }

    [Fact]
    public void Parse_HyphenatedFlowName_ExtractsFullName()
    {
        var source = @"
flow my-workflow-name v1.0 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("my-workflow-name", flow.Name);
    }

    [Fact]
    public void Parse_Version_ExtractsMajorMinor()
    {
        var source = @"
flow test v2.5 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("2.5", flow.Version);
    }

    [Fact]
    public void Parse_VersionMajorOnly_ExtractsVersion()
    {
        var source = @"
flow test v3 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("3", flow.Version);
    }

    [Fact]
    public void Parse_FlowWithComments_IgnoresComments()
    {
        var source = @"
// This is a comment
flow commentedflow v1.0 {
  // Input section
  from http(""/webhook"")

  /* Multi-line
     comment */
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("commentedflow", flow.Name);
        Assert.Equal("1.0", flow.Version);
    }

    [Fact]
    public void Parse_MinimalFlow_ParsesCorrectly()
    {
        var source = @"flow minimal v1.0 { from http(""/api"") | console() }";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("minimal", flow.Name);
        Assert.Equal("1.0", flow.Version);
        Assert.Equal("http", flow.Input.Type);
        Assert.Single(flow.PipelineSteps);
    }
}

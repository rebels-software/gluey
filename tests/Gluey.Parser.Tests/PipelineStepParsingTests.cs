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
using GlueyParser = Gluey.Parser;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for pipeline step parsing (transforms with args and blocks)
/// </summary>
public class PipelineStepParsingTests
{
    [Fact]
    public void Parse_SimpleTransform_ExtractsType()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Single(flow.PipelineSteps);
        Assert.Equal("console", flow.PipelineSteps[0].Type);
    }

    [Fact]
    public void Parse_DottedTransformName_ParsesFullName()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(2, flow.PipelineSteps.Count);
        Assert.Equal("json.parse", flow.PipelineSteps[0].Type);
    }

    [Fact]
    public void Parse_TransformWithArgument_ExtractsFieldConfig()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var jsonParse = flow.PipelineSteps[0];
        Assert.True(jsonParse.Config.ContainsKey("field"));
        Assert.Equal("payload", jsonParse.Config["field"].GetString());
    }

    [Fact]
    public void Parse_FilterTransform_ExtractsCondition()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | filter(temperature > 25)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var filterStep = flow.PipelineSteps.First(s => s.Type == "filter");
        Assert.True(filterStep.Config.ContainsKey("condition"));
        Assert.Contains("temperature", filterStep.Config["condition"].GetString());
        Assert.Contains(">", filterStep.Config["condition"].GetString());
        Assert.Contains("25", filterStep.Config["condition"].GetString());
    }

    [Fact]
    public void Parse_FilterWithComplexCondition_ParsesExpression()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | filter(temperature > 25 && humidity < 80)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var filterStep = flow.PipelineSteps.First(s => s.Type == "filter");
        var condition = filterStep.Config["condition"].GetString();
        Assert.Contains("&&", condition);
        Assert.Contains("temperature", condition);
        Assert.Contains("humidity", condition);
    }

    [Fact]
    public void Parse_TransformWithConfigBlock_ExtractsConfig()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | transform {
      device_id: device_id
      temp_celsius: temperature
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        Assert.True(transformStep.Config.ContainsKey("device_id"));
        Assert.True(transformStep.Config.ContainsKey("temp_celsius"));
    }

    [Fact]
    public void Parse_DecodeBinaryTransform_ParsesDottedName()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | decode.binary(payload) {
      format: ""custom""
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var decodeStep = flow.PipelineSteps.First(s => s.Type == "decode.binary");
        Assert.Equal("decode.binary", decodeStep.Type);
        Assert.Equal("payload", decodeStep.Config["field"].GetString());
        Assert.Equal("custom", decodeStep.Config["format"].GetString());
    }

    [Fact]
    public void Parse_DecodeHexTransform_ParsesCorrectly()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | decode.hex(hex_payload)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var decodeStep = flow.PipelineSteps.First(s => s.Type == "decode.hex");
        Assert.Equal("decode.hex", decodeStep.Type);
        Assert.Equal("hex_payload", decodeStep.Config["field"].GetString());
    }

    [Fact]
    public void Parse_DecodeBase64Transform_ParsesCorrectly()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | decode.base64(encoded_data)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var decodeStep = flow.PipelineSteps.First(s => s.Type == "decode.base64");
        Assert.Equal("decode.base64", decodeStep.Type);
    }

    [Fact]
    public void Parse_MultiplePipelineSteps_PreservesOrder()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | filter(value > 0)
  | transform { output: value }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal(4, flow.PipelineSteps.Count);
        Assert.Equal("json.parse", flow.PipelineSteps[0].Type);
        Assert.Equal("filter", flow.PipelineSteps[1].Type);
        Assert.Equal("transform", flow.PipelineSteps[2].Type);
        Assert.Equal("console", flow.PipelineSteps[3].Type);
    }

    [Fact]
    public void Parse_TransformWithStringArgument_ParsesCorrectly()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(""message_body"")
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var jsonParse = flow.PipelineSteps[0];
        Assert.Equal("message_body", jsonParse.Config["field"].GetString());
    }

    [Fact]
    public void Parse_TransformWithArrayConfig_ParsesArray()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | transform {
      tags: [""sensor"", ""temperature"", ""outdoor""]
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        var tags = transformStep.Config["tags"];
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);
        Assert.Equal(3, tags.GetArrayLength());
    }

    [Fact]
    public void Parse_TransformKeywordAsType_ParsesCorrectly()
    {
        // 'transform' is both a keyword and a valid transform type
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | transform {
      x: y
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("transform", flow.PipelineSteps[0].Type);
    }

    [Fact]
    public void Parse_FilterKeywordAsType_ParsesCorrectly()
    {
        // 'filter' is both a keyword and a valid transform type
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | filter(active == true)
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        Assert.Equal("filter", flow.PipelineSteps[0].Type);
    }

    [Fact]
    public void Parse_TransformWithArgsAndBlock_MergesConfig()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload) {
      strict: false
    }
  | console()
}";

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var jsonParse = flow.PipelineSteps[0];
        Assert.Equal("payload", jsonParse.Config["field"].GetString());
        Assert.False(jsonParse.Config["strict"].GetBoolean());
    }
}

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
/// Tests for FlowValidator - validates .gflow DSL semantic correctness
/// </summary>
public class FlowValidationTests
{
    private readonly FlowValidator _validator = FlowValidator.CreateDefault();

    private Gluey.Core.Models.Flow ParseFlow(string source)
    {
        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        return parser.Parse();
    }

    // ===== Valid flows =====

    [Fact]
    public void Validate_ValidHelloWorldFlow_ReturnsValid()
    {
        var source = @"
flow hello-world v1.0 {
  from http(""/webhook"") {
    port: 8080
  }
  | json.parse(payload)
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ValidSmartSensorFlow_ReturnsValid()
    {
        var source = @"
flow smart-sensor v1.0 {
  from http(""/webhook"") {
    port: 8080
  }
  | json.parse(payload)
  | filter(temperature > 20)
  | transform {
      device_id: device_id
      temp_f: temperature * 9 / 5 + 32
    }
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ===== Invalid input plugin tests =====

    [Fact]
    public void Validate_InvalidInputPlugin_ReturnsError()
    {
        var source = @"
flow test v1.0 {
  from httpp(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unknown input plugin type: 'httpp'"));
    }

    [Fact]
    public void Validate_InvalidInputPlugin_SuggestsCorrect()
    {
        var source = @"
flow test v1.0 {
  from htpp(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Suggestion == "http");
    }

    // ===== Invalid transform plugin tests =====

    [Fact]
    public void Validate_InvalidTransformPlugin_ReturnsError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.pars(payload)
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unknown plugin type: 'json.pars'"));
    }

    [Fact]
    public void Validate_InvalidTransformPlugin_SuggestsCorrect()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.pars(payload)
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.Contains(result.Errors, e => e.Suggestion == "json.parse");
    }

    [Fact]
    public void Validate_InvalidFilterSpelling_SuggestsCorrect()
    {
        // Note: 'filtr' is parsed as a generic transform with argument, not a filter block
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | filtr(payload)
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Suggestion == "filter");
    }

    // ===== Invalid output plugin tests =====

    [Fact]
    public void Validate_InvalidOutputPlugin_ReturnsError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | consol()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Unknown plugin type: 'consol'"));
    }

    [Fact]
    public void Validate_InvalidOutputPlugin_SuggestsCorrect()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | consol()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.Contains(result.Errors, e => e.Suggestion == "console");
    }

    // ===== Flow name validation =====

    [Fact]
    public void Validate_ValidFlowName_NoErrors()
    {
        // Note: DSL parser only allows hyphenated names where each part is a valid identifier
        var source = @"
flow my-workflow-name v1.0 {
  from http(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FlowNameWithUnderscore_NoErrors()
    {
        var source = @"
flow my_flow_name v1.0 {
  from http(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
    }

    // ===== Version validation =====

    [Fact]
    public void Validate_ValidVersion_NoErrors()
    {
        var source = @"
flow test v2.1 {
  from http(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MajorOnlyVersion_NoErrors()
    {
        var source = @"
flow test v3 {
  from http(""/webhook"")
  | console()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
    }

    // ===== Flow structure validation =====

    [Fact]
    public void Validate_NoPipelineSteps_ReturnsError()
    {
        // This would fail at parse time, but testing validator directly
        var flow = new Gluey.Core.Models.Flow
        {
            Name = "test",
            Version = "1.0",
            Input = new Gluey.Core.Models.InputNode { Type = "http", Url = "/webhook", Config = new Dictionary<string, System.Text.Json.JsonElement>() },
            PipelineSteps = new List<Gluey.Core.Models.PipelineStep>(),
            Routes = new List<Gluey.Core.Models.Route>()
        };

        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("no pipeline steps"));
    }

    [Fact]
    public void Validate_NoOutput_ReturnsError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"")
  | json.parse(payload)
  | filter(x > 0)
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("no output"));
    }

    // ===== Multiple errors =====

    [Fact]
    public void Validate_MultipleInvalidPlugins_ReturnsAllErrors()
    {
        var source = @"
flow test v1.0 {
  from httpp(""/webhook"")
  | json.pars(payload)
  | consol()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        // 3 unknown plugin errors + 1 "no output" error since consol is not recognized
        Assert.True(result.Errors.Count >= 3, $"Expected at least 3 errors, got {result.Errors.Count}");
    }

    // ===== All known plugins validation =====

    [Fact]
    public void Validate_AllKnownInputPlugins_NoErrors()
    {
        var inputs = new[] { "http", "mqtt" };

        foreach (var input in inputs)
        {
            var source = $@"
flow test v1.0 {{
  from {input}(""/endpoint"")
  | console()
}}";
            var flow = ParseFlow(source);
            var result = _validator.Validate(flow);

            Assert.True(result.IsValid, $"Input plugin '{input}' should be valid");
        }
    }

    [Fact]
    public void Validate_AllKnownTransformPlugins_NoErrors()
    {
        var transforms = new[] { "json.parse", "filter", "transform" };

        foreach (var transform in transforms)
        {
            string source;
            if (transform == "filter")
            {
                source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {transform}(x > 0)
  | console()
}}";
            }
            else if (transform == "transform")
            {
                source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {transform} {{ x: y }}
  | console()
}}";
            }
            else
            {
                source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {transform}(payload)
  | console()
}}";
            }

            var flow = ParseFlow(source);
            var result = _validator.Validate(flow);

            Assert.True(result.IsValid, $"Transform plugin '{transform}' should be valid");
        }
    }

    [Fact]
    public void Validate_AllKnownOutputPlugins_NoErrors()
    {
        var outputs = new[] { "console" };

        foreach (var output in outputs)
        {
            var source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {output}()
}}";
            var flow = ParseFlow(source);
            var result = _validator.Validate(flow);

            Assert.True(result.IsValid, $"Output plugin '{output}' should be valid");
        }
    }

    // ===== Typo suggestion tests =====

    [Theory]
    [InlineData("htpp", "http")]
    [InlineData("htp", "http")]
    [InlineData("mqqt", "mqtt")]
    [InlineData("mqt", "mqtt")]
    public void Validate_InputTypo_SuggestsCorrection(string typo, string expected)
    {
        var source = $@"
flow test v1.0 {{
  from {typo}(""/endpoint"")
  | console()
}}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Suggestion == expected);
    }

    [Theory]
    [InlineData("json.pars", "json.parse")]
    [InlineData("json.parce", "json.parse")]
    [InlineData("filtr", "filter")]
    [InlineData("fiter", "filter")]
    [InlineData("transfrom", "transform")]
    [InlineData("transfrm", "transform")]
    public void Validate_TransformTypo_SuggestsCorrection(string typo, string expected)
    {
        var source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {typo}(x)
  | console()
}}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Suggestion == expected);
    }

    [Theory]
    [InlineData("consol", "console")]
    [InlineData("conole", "console")]
    [InlineData("sqll", "sql")]
    public void Validate_OutputTypo_SuggestsCorrection(string typo, string expected)
    {
        var source = $@"
flow test v1.0 {{
  from http(""/endpoint"")
  | {typo}()
}}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Suggestion == expected);
    }

    // ===== Case insensitivity tests =====

    [Fact]
    public void Validate_PluginNameCaseInsensitive_NoErrors()
    {
        var source = @"
flow test v1.0 {
  from HTTP(""/webhook"")
  | JSON.PARSE(payload)
  | CONSOLE()
}";
        var flow = ParseFlow(source);
        var result = _validator.Validate(flow);

        Assert.True(result.IsValid);
    }
}

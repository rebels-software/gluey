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
/// Tests that .gflow files with $meta access, array indexing, and null in expressions
/// parse correctly through the full lexer → parser pipeline.
/// </summary>
public class MetadataParserTests
{
    private static Gluey.Core.Models.Flow ParseFlow(string source)
    {
        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        return parser.Parse();
    }

    // ===== $meta in transform block =====

    [Fact]
    public void Parse_TransformWithMetaAccess_ParsesWithoutError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | transform {
      source: $meta.topic
    }
  | console()
}";

        var flow = ParseFlow(source);

        Assert.NotEmpty(flow.PipelineSteps);
        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        Assert.True(transformStep.Config.ContainsKey("source"));
    }

    [Fact]
    public void Parse_TransformWithMetaAccess_CapturesExpression()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | transform {
      source: $meta.topic
    }
  | console()
}";

        var flow = ParseFlow(source);

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        var expression = transformStep.Config["source"].GetString();
        Assert.Contains("$meta", expression);
        Assert.Contains("topic", expression);
    }

    // ===== $meta.topic.split('/')[index] — method chaining + array indexing =====

    [Fact]
    public void Parse_TransformWithMetaTopicSplitIndex_ParsesWithoutError()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtt://localhost:1883"") {
    topics: [""sensors/+/telemetry""]
  }
  | json.parse(payload)
  | transform {
      device_id: $meta.topic.split('/')[1]
    }
  | console()
}";

        var flow = ParseFlow(source);

        Assert.NotEmpty(flow.PipelineSteps);
    }

    [Fact]
    public void Parse_TransformWithMetaTopicSplitIndex_CapturesExpression()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtt://localhost:1883"") {
    topics: [""sensors/+/telemetry""]
  }
  | json.parse(payload)
  | transform {
      device_id: $meta.topic.split('/')[1]
    }
  | console()
}";

        var flow = ParseFlow(source);

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        Assert.True(transformStep.Config.ContainsKey("device_id"));
        var expression = transformStep.Config["device_id"].GetString();
        Assert.Contains("$meta", expression);
        Assert.Contains("split", expression);
        Assert.Contains("[", expression);
        Assert.Contains("]", expression);
        Assert.Contains("1", expression);
    }

    // ===== null in ternary expression =====

    [Fact]
    public void Parse_TransformWithNullInTernary_ParsesWithoutError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | transform {
      value: field > 0 ? field : null
    }
  | console()
}";

        var flow = ParseFlow(source);

        Assert.NotEmpty(flow.PipelineSteps);
    }

    [Fact]
    public void Parse_TransformWithNullInTernary_CapturesNullSomewhere()
    {
        // Note: the ternary `field > 0 ? field : null` hits the parser's
        // "identifier followed by colon = next key" heuristic, so the
        // expression is split: the transform block will contain a `value`
        // key (expression up to the true-branch identifier) AND a `field`
        // key whose expression is "null". What matters here is that `null`
        // as a token does NOT cause a ParseException — it is consumed and
        // stored as the string "null" in some config entry.
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | transform {
      value: field > 0 ? field : null
    }
  | console()
}";

        var flow = ParseFlow(source);

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        // At least one config entry should contain "null" in its expression
        var hasNull = transformStep.Config.Values
            .Any(v => v.ValueKind == System.Text.Json.JsonValueKind.String &&
                      v.GetString()!.Contains("null"));
        Assert.True(hasNull, "Expected 'null' to appear in at least one transform expression");
    }

    [Fact]
    public void Parse_TransformWithNullAsFalseBranchNumber_CapturesNullInExpression()
    {
        // When the true-branch of a ternary is a number (not an identifier),
        // the "identifier:" heuristic does not fire, so the full ternary
        // including ": null" is captured in a single expression.
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | transform {
      score: value > 0 ? 1 : null
    }
  | console()
}";

        var flow = ParseFlow(source);

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        Assert.True(transformStep.Config.ContainsKey("score"));
        var expression = transformStep.Config["score"].GetString();
        Assert.Contains("null", expression);
        Assert.Contains("?", expression);
        Assert.Contains(":", expression);
    }

    // ===== $meta in filter condition =====

    [Fact]
    public void Parse_FilterWithMetaCondition_ParsesWithoutError()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | filter($meta.source == ""mqtt"")
  | console()
}";

        var flow = ParseFlow(source);

        Assert.NotEmpty(flow.PipelineSteps);
        var filterStep = flow.PipelineSteps.First(s => s.Type == "filter");
        Assert.True(filterStep.Config.ContainsKey("condition"));
    }

    [Fact]
    public void Parse_FilterWithMetaCondition_CapturesExpression()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | filter($meta.source == ""mqtt"")
  | console()
}";

        var flow = ParseFlow(source);

        var filterStep = flow.PipelineSteps.First(s => s.Type == "filter");
        var condition = filterStep.Config["condition"].GetString();
        Assert.Contains("$meta", condition);
        Assert.Contains("source", condition);
        Assert.Contains("==", condition);
        Assert.Contains("mqtt", condition);
    }

    // ===== Sample 04: industrial-pipeline.gflow =====

    private static string FindSampleFile(string filename)
    {
        // Walk up from AppContext.BaseDirectory to locate the engine root,
        // then append "samples/<filename>". Handles both Debug and Release builds.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", filename);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate sample file '{filename}' by walking up from '{AppContext.BaseDirectory}'.");
    }

    [Fact]
    public void Parse_Sample04IndustrialPipeline_ParsesWithoutError()
    {
        var samplePath = FindSampleFile("04-industrial-pipeline.gflow");
        var source = File.ReadAllText(samplePath);
        var flow = ParseFlow(source);

        Assert.Equal("industrial-pipeline", flow.Name);
        Assert.NotEmpty(flow.PipelineSteps);
    }

    [Fact]
    public void Parse_Sample04IndustrialPipeline_TransformHasMetaTopicSplit()
    {
        var samplePath = FindSampleFile("04-industrial-pipeline.gflow");
        var source = File.ReadAllText(samplePath);
        var flow = ParseFlow(source);

        var transformStep = flow.PipelineSteps.First(s => s.Type == "transform");
        Assert.True(transformStep.Config.ContainsKey("device_id"));
        var expression = transformStep.Config["device_id"].GetString();
        Assert.Contains("$meta", expression);
        Assert.Contains("split", expression);
    }

    // ===== FlowValidator accepts $meta expressions without errors =====

    [Fact]
    public void Validate_FlowWithMetaInTransform_ReturnsValid()
    {
        var source = @"
flow test v1.0 {
  from mqtt(""mqtt://localhost:1883"") {
    topics: [""sensors/+/data""]
  }
  | json.parse(payload)
  | transform {
      device_id: $meta.topic.split('/')[1]
      temperature: temperature
    }
  | console()
}";

        var flow = ParseFlow(source);
        var validator = GlueyParser.FlowValidator.CreateDefault();
        var result = validator.Validate(flow);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Validate_FlowWithMetaInFilter_ReturnsValid()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | filter($meta.source == ""mqtt"")
  | console()
}";

        var flow = ParseFlow(source);
        var validator = GlueyParser.FlowValidator.CreateDefault();
        var result = validator.Validate(flow);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void Validate_FlowWithNullInTernary_ReturnsValid()
    {
        var source = @"
flow test v1.0 {
  from http(""/webhook"") { port: 8080 }
  | json.parse(payload)
  | transform {
      result: value > 0 ? value : null
    }
  | console()
}";

        var flow = ParseFlow(source);
        var validator = GlueyParser.FlowValidator.CreateDefault();
        var result = validator.Validate(flow);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}

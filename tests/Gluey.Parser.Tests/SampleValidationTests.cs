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
/// Integration tests that validate all sample .gflow files through the full
/// lexer -> parser -> validator pipeline (same as `gluey validate`).
/// </summary>
public class SampleValidationTests
{
    private static readonly string SamplesDir = Path.Combine(
        Path.GetDirectoryName(typeof(SampleValidationTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "samples");

    private readonly FlowValidator _validator = FlowValidator.CreateDefault();

    private (Gluey.Core.Models.Flow flow, ValidationResult result) ValidateFile(string filename)
    {
        var filePath = Path.GetFullPath(Path.Combine(SamplesDir, filename));
        Assert.True(File.Exists(filePath), $"Sample file not found: {filePath}");

        var source = File.ReadAllText(filePath);
        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var result = _validator.Validate(flow);

        return (flow, result);
    }

    [Theory]
    [InlineData("01-hello-world.gflow")]
    [InlineData("02-smart-sensor.gflow")]
    [InlineData("03-binary-sensor.gflow")]
    [InlineData("04-industrial-pipeline.gflow")]
    [InlineData("05-advanced-protocol.gflow")]
    [InlineData("06-fanout-pattern.gflow")]
    public void Sample_ParsesWithoutError(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.NotNull(flow);
        Assert.False(string.IsNullOrEmpty(flow.Name), "Flow name should not be empty");
    }

    [Theory]
    [InlineData("01-hello-world.gflow")]
    [InlineData("02-smart-sensor.gflow")]
    [InlineData("03-binary-sensor.gflow")]
    [InlineData("04-industrial-pipeline.gflow")]
    [InlineData("05-advanced-protocol.gflow")]
    [InlineData("06-fanout-pattern.gflow")]
    public void Sample_PassesValidation(string filename)
    {
        var (_, result) = ValidateFile(filename);
        Assert.True(result.IsValid, $"Validation errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Theory]
    [InlineData("01-hello-world.gflow", "hello-world", "1.0")]
    [InlineData("02-smart-sensor.gflow", "smart-sensor", "1.0")]
    [InlineData("03-binary-sensor.gflow", "binary-sensor", "1.0")]
    [InlineData("04-industrial-pipeline.gflow", "industrial-pipeline", "1.0")]
    [InlineData("05-advanced-protocol.gflow", "advanced-protocol", "1.0")]
    [InlineData("06-fanout-pattern.gflow", "fanout-pattern", "1.0")]
    public void Sample_HasCorrectNameAndVersion(string filename, string expectedName, string expectedVersion)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.Equal(expectedName, flow.Name);
        Assert.Equal(expectedVersion, flow.Version);
    }

    [Theory]
    [InlineData("01-hello-world.gflow", "http")]
    [InlineData("02-smart-sensor.gflow", "http")]
    [InlineData("03-binary-sensor.gflow", "http")]
    [InlineData("04-industrial-pipeline.gflow", "mqtt")]
    [InlineData("05-advanced-protocol.gflow", "mqtt")]
    [InlineData("06-fanout-pattern.gflow", "http")]
    public void Sample_HasExpectedInputType(string filename, string expectedInput)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.Equal(expectedInput, flow.Input.Type);
    }

    [Theory]
    [InlineData("01-hello-world.gflow")]
    [InlineData("02-smart-sensor.gflow")]
    [InlineData("03-binary-sensor.gflow")]
    [InlineData("04-industrial-pipeline.gflow")]
    [InlineData("05-advanced-protocol.gflow")]
    [InlineData("06-fanout-pattern.gflow")]
    public void Sample_HasAtLeastOnePipelineStep(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.NotEmpty(flow.PipelineSteps);
    }
}

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
/// Auto-discovers samples matching NN-*.gflow pattern — new samples are
/// tested automatically without modifying this file.
/// </summary>
public class SampleValidationTests
{
    private static readonly string SamplesDir = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(SampleValidationTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "samples"));

    private readonly FlowValidator _validator = FlowValidator.CreateDefault();

    /// <summary>
    /// Discovers all numbered sample files (01-*.gflow, 02-*.gflow, etc.)
    /// Test/debug samples (test-*.gflow) are excluded.
    /// </summary>
    public static IEnumerable<object[]> SampleFiles()
    {
        if (!Directory.Exists(SamplesDir))
            yield break;

        foreach (var file in Directory.GetFiles(SamplesDir, "*.gflow").Order())
        {
            var filename = Path.GetFileName(file);
            // Only numbered samples (01-*, 02-*, etc.), skip test-* files
            if (filename.Length >= 3 && char.IsDigit(filename[0]) && char.IsDigit(filename[1]) && filename[2] == '-')
                yield return [filename];
        }
    }

    private (Gluey.Core.Models.Flow flow, ValidationResult result) ValidateFile(string filename)
    {
        var filePath = Path.Combine(SamplesDir, filename);
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
    [MemberData(nameof(SampleFiles))]
    public void Sample_ParsesWithoutError(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.NotNull(flow);
        Assert.False(string.IsNullOrEmpty(flow.Name), $"{filename}: flow name should not be empty");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_PassesValidation(string filename)
    {
        var (_, result) = ValidateFile(filename);
        Assert.True(result.IsValid, $"{filename}: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_HasInputPlugin(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.NotNull(flow.Input);
        Assert.False(string.IsNullOrEmpty(flow.Input.Type), $"{filename}: input type should not be empty");
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_HasAtLeastOnePipelineStep(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.NotEmpty(flow.PipelineSteps);
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Sample_HasVersionFormat(string filename)
    {
        var (flow, _) = ValidateFile(filename);
        Assert.Matches(@"^\d+\.\d+$", flow.Version);
    }
}

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
using Gluey.Plugins.Common;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for metadata access expressions ($meta.key) and string functions.
/// </summary>
public class MetadataExpressionTests
{
    private static JsonElement CreatePayload(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return JsonDocument.Parse(json).RootElement;
    }

    // ===== $meta.key access tests =====

    [Fact]
    public void Evaluate_MetaTopic_ReturnsTopicValue()
    {
        var payload = CreatePayload(new { temperature = 25.5 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "sensors/sensor-001/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic");

        Assert.Equal("sensors/sensor-001/telemetry", result);
    }

    [Fact]
    public void Evaluate_MetaSource_ReturnsSourceValue()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "mqtt",
            ["topic"] = "test/topic"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.source");

        Assert.Equal("mqtt", result);
    }

    [Fact]
    public void Evaluate_MetaMissingKey_ReturnsNull()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "test/topic"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_MetaInComparison_WorksCorrectly()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "mqtt"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.EvaluateBoolean("$meta.source == \"mqtt\"");

        Assert.True(result);
    }

    // ===== string.split() tests =====

    [Fact]
    public void Evaluate_MetaTopicSplit_ReturnsArray()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/sensor-001/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic.split('/')");

        Assert.IsType<string[]>(result);
        var arr = (string[])result;
        Assert.Equal(3, arr.Length);
        Assert.Equal("devices", arr[0]);
        Assert.Equal("sensor-001", arr[1]);
        Assert.Equal("telemetry", arr[2]);
    }

    [Fact]
    public void Evaluate_MetaTopicSplitWithIndex_ReturnsElement()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/sensor-001/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic.split('/')[1]");

        Assert.Equal("sensor-001", result);
    }

    [Fact]
    public void Evaluate_PayloadFieldSplit_ReturnsElement()
    {
        var payload = CreatePayload(new { path = "a/b/c" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("path.split('/')[0]");

        Assert.Equal("a", result);
    }

    [Fact]
    public void Evaluate_SplitWithInvalidIndex_ReturnsNull()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "a/b"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic.split('/')[10]");

        Assert.Null(result);
    }

    // ===== string.substring() tests =====

    [Fact]
    public void Evaluate_SubstringStartOnly_ReturnsFromStart()
    {
        var payload = CreatePayload(new { serial = "ABC123DEF" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("serial.substring(3)");

        Assert.Equal("123DEF", result);
    }

    [Fact]
    public void Evaluate_SubstringStartAndLength_ReturnsSubstring()
    {
        var payload = CreatePayload(new { serial = "ABC123DEF" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("serial.substring(0, 3)");

        Assert.Equal("ABC", result);
    }

    [Fact]
    public void Evaluate_MetaSubstring_WorksOnMetadata()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/sensor-001/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic.substring(0, 7)");

        Assert.Equal("devices", result);
    }

    [Fact]
    public void Evaluate_SubstringBeyondLength_ReturnsEmpty()
    {
        var payload = CreatePayload(new { name = "test" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("name.substring(100)");

        Assert.Equal("", result);
    }

    // ===== string.indexOf() tests =====

    [Fact]
    public void Evaluate_IndexOfFound_ReturnsIndex()
    {
        var payload = CreatePayload(new { path = "devices/sensor/data" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("path.indexOf('/')");

        Assert.Equal(7.0, result);
    }

    [Fact]
    public void Evaluate_IndexOfNotFound_ReturnsMinusOne()
    {
        var payload = CreatePayload(new { path = "no-slash-here" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("path.indexOf('/')");

        Assert.Equal(-1.0, result);
    }

    [Fact]
    public void Evaluate_MetaIndexOf_WorksOnMetadata()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/sensor/data"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.topic.indexOf('sensor')");

        Assert.Equal(8.0, result);
    }

    // ===== Additional string methods =====

    [Fact]
    public void Evaluate_ToLower_ConvertsToLowercase()
    {
        var payload = CreatePayload(new { name = "HELLO World" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("name.toLower()");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Evaluate_ToUpper_ConvertsToUppercase()
    {
        var payload = CreatePayload(new { name = "Hello World" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("name.toUpper()");

        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void Evaluate_Trim_RemovesWhitespace()
    {
        var payload = CreatePayload(new { name = "  hello  " });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("name.trim()");

        Assert.Equal("hello", result);
    }

    // ===== Integration tests combining metadata and transforms =====

    [Fact]
    public void Evaluate_ComplexExpression_MetaWithArithmetic()
    {
        var payload = CreatePayload(new { multiplier = 2 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/sensor-001/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);

        // Extract device ID from topic and compare
        var deviceId = evaluator.Evaluate("$meta.topic.split('/')[1]");
        Assert.Equal("sensor-001", deviceId);
    }

    [Fact]
    public void Evaluate_MetaInTernary_WorksCorrectly()
    {
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "mqtt"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("$meta.source == \"mqtt\" ? \"message\" : \"http\"");

        Assert.Equal("message", result);
    }

    [Fact]
    public void Evaluate_PayloadFieldWithMethodChain_WorksCorrectly()
    {
        var payload = CreatePayload(new { device_path = "zone-a/building-1/floor-2" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);

        // Get zone from path
        var zone = evaluator.Evaluate("device_path.split('/')[0]");
        Assert.Equal("zone-a", zone);

        // Get building from path
        var building = evaluator.Evaluate("device_path.split('/')[1]");
        Assert.Equal("building-1", building);
    }

    // ===== Boolean evaluation with metadata =====

    [Fact]
    public void EvaluateBoolean_MetaComparison_ReturnsTrue()
    {
        var payload = CreatePayload(new { temperature = 25 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "sensors/outdoor/temp"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.EvaluateBoolean("$meta.topic.split('/')[1] == \"outdoor\"");

        Assert.True(result);
    }

    [Fact]
    public void EvaluateBoolean_MetaInLogicalExpression_WorksCorrectly()
    {
        var payload = CreatePayload(new { temperature = 30 });
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "mqtt",
            ["topic"] = "sensors/critical/temp"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.EvaluateBoolean("$meta.source == \"mqtt\" && temperature > 25");

        Assert.True(result);
    }

    // ===== Edge cases =====

    [Fact]
    public void Evaluate_NullMetadata_HandlesGracefully()
    {
        var payload = CreatePayload(new { value = 42 });

        // Pass null metadata
        var evaluator = new ExpressionEvaluator(payload, null);
        var result = evaluator.Evaluate("$meta.topic");

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_EmptyString_SplitReturnsArray()
    {
        var payload = CreatePayload(new { empty = "" });
        var metadata = new Dictionary<string, string>();

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("empty.split('/')");

        Assert.IsType<string[]>(result);
        var arr = (string[])result;
        Assert.Single(arr);
        Assert.Equal("", arr[0]);
    }
}

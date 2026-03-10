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
/// Tests for type casting functions: int(), float(), string().
/// </summary>
public class CastingFunctionTests
{
    private static JsonElement CreatePayload(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return JsonDocument.Parse(json).RootElement;
    }

    // ===== int() casting tests =====

    [Fact]
    public void Int_FromString_ReturnsLong()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(\"42\")");

        Assert.IsType<long>(result);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Int_FromDouble_TruncatesToLong()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(3.7)");

        Assert.IsType<long>(result);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Int_FromTrue_ReturnsOne()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(true)");

        Assert.IsType<long>(result);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Int_FromFalse_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(false)");

        Assert.IsType<long>(result);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Int_FromNull_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(null)");

        Assert.IsType<long>(result);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Int_FromFloatString_ParsesAndTruncates()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(\"3.14\")");

        Assert.IsType<long>(result);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Int_FromInvalidString_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(\"not-a-number\")");

        Assert.IsType<long>(result);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Int_FromPayloadField_CastsFieldValue()
    {
        var payload = CreatePayload(new { temperature = 25.7 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(temperature)");

        Assert.IsType<long>(result);
        Assert.Equal(25L, result);
    }

    [Fact]
    public void Int_NoArgs_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int()");

        Assert.IsType<long>(result);
        Assert.Equal(0L, result);
    }

    // ===== float() casting tests =====

    [Fact]
    public void Float_FromString_ReturnsDouble()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(\"3.14\")");

        Assert.IsType<double>(result);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void Float_FromInteger_ReturnsDouble()
    {
        // JSON numbers are parsed as double by the evaluator,
        // but this tests the cast path for integer-like values
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(42)");

        Assert.IsType<double>(result);
        Assert.Equal(42.0, result);
    }

    [Fact]
    public void Float_FromTrue_ReturnsOne()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(true)");

        Assert.IsType<double>(result);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Float_FromFalse_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(false)");

        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Float_FromNull_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(null)");

        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Float_FromInvalidString_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(\"not-a-number\")");

        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Float_NoArgs_ReturnsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float()");

        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    // ===== string() casting tests =====

    [Fact]
    public void String_FromNumber_ReturnsStringRepresentation()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string(42)");

        Assert.IsType<string>(result);
        Assert.Equal("42", result);
    }

    [Fact]
    public void String_FromNull_ReturnsEmptyString()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string(null)");

        Assert.IsType<string>(result);
        Assert.Equal("", result);
    }

    [Fact]
    public void String_FromBoolean_ReturnsStringRepresentation()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string(true)");

        Assert.IsType<string>(result);
        Assert.Equal("True", result);
    }

    [Fact]
    public void String_FromStringLiteral_ReturnsSameString()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string(\"hello\")");

        Assert.IsType<string>(result);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void String_NoArgs_ReturnsEmptyString()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string()");

        Assert.IsType<string>(result);
        Assert.Equal("", result);
    }

    // ===== Integration: real use case with metadata =====

    [Fact]
    public void Int_MetaTopicSplitIndex_CastsToInteger()
    {
        // Real use case: extract machine ID from MQTT topic as integer
        // Topic: "compass/machines/1/data" -> int($meta.topic.split('/')[2]) -> 1L
        var payload = CreatePayload(new { value = 42 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "compass/machines/1/data"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("int($meta.topic.split('/')[2])");

        Assert.IsType<long>(result);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Float_PayloadFieldExpression_CastsExpressionResult()
    {
        // Cast an arithmetic expression result to float
        var payload = CreatePayload(new { count = 7 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("float(count)");

        Assert.IsType<double>(result);
        Assert.Equal(7.0, result);
    }

    [Fact]
    public void Int_InArithmeticExpression_WorksCorrectly()
    {
        // Use int() result in arithmetic
        var payload = CreatePayload(new { value = 1 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "sensors/5/data"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.Evaluate("int($meta.topic.split('/')[1]) * 10");

        // int() returns 5L, then 5L * 10.0 = 50.0 (arithmetic uses doubles)
        Assert.Equal(50.0, result);
    }

    [Fact]
    public void Int_InComparison_WorksCorrectly()
    {
        // Use int() in a filter/comparison expression
        var payload = CreatePayload(new { value = 1 });
        var metadata = new Dictionary<string, string>
        {
            ["topic"] = "devices/3/telemetry"
        };

        var evaluator = new ExpressionEvaluator(payload, metadata);
        var result = evaluator.EvaluateBoolean("int($meta.topic.split('/')[1]) > 2");

        Assert.True(result);
    }

    [Fact]
    public void String_FromPayloadField_CastsToString()
    {
        var payload = CreatePayload(new { temperature = 25.5 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("string(temperature)");

        Assert.IsType<string>(result);
        Assert.Equal("25.5", result);
    }

    [Fact]
    public void Int_NegativeDouble_TruncatesTowardsZero()
    {
        var payload = CreatePayload(new { value = 1 });
        var evaluator = new ExpressionEvaluator(payload);
        var result = evaluator.Evaluate("int(-3.7)");

        Assert.IsType<long>(result);
        Assert.Equal(-3L, result);
    }
}

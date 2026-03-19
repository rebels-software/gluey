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
using Gluey.Plugins.Transforms;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for RouteTransform "mode: all" feature.
/// Verifies that when mode is "all", all matching routes are collected into a
/// comma-separated _route metadata value instead of stopping at the first match.
/// </summary>
public class RouteModAllTests
{
    private static Message CreateTestMessage(string json, Dictionary<string, string>? metadata = null)
    {
        return new Message(
            JsonDocument.Parse(json),
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
    }

    #region RouteTransform mode: first (default)

    [Fact]
    public async Task DefaultMode_FirstMatchWins()
    {
        // Arrange - both conditions are true, but default mode takes first match
        var config = new Dictionary<string, JsonElement>
        {
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["nok"] = JsonSerializer.SerializeToElement("nok_count > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 5, "nok_count": 3}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - only first match
        Assert.NotNull(result);
        Assert.True(result.Metadata.ContainsKey(RouteTransform.RouteMetadataKey));
        Assert.Equal("ok", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    [Fact]
    public async Task ExplicitModeFirst_FirstMatchWins()
    {
        // Arrange - explicit mode: first
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("first"),
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["nok"] = JsonSerializer.SerializeToElement("nok_count > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 5, "nok_count": 3}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - only first match
        Assert.NotNull(result);
        Assert.Equal("ok", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    #endregion

    #region RouteTransform mode: all

    [Fact]
    public async Task ModeAll_AllMatchingRoutesReturned()
    {
        // Arrange
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["nok"] = JsonSerializer.SerializeToElement("nok_count > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 5, "nok_count": 3}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - both routes matched, comma-separated
        Assert.NotNull(result);
        Assert.Equal("ok,nok", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    [Fact]
    public async Task ModeAll_OnlyMatchingRoutesIncluded()
    {
        // Arrange - ok matches, nok does not
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["nok"] = JsonSerializer.SerializeToElement("nok_count > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 5, "nok_count": 0}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - only "ok" matched
        Assert.NotNull(result);
        Assert.Equal("ok", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    [Fact]
    public async Task ModeAll_NoMatchesPassesThrough()
    {
        // Arrange - no conditions match
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["nok"] = JsonSerializer.SerializeToElement("nok_count > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 0, "nok_count": 0}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - no route metadata set, message passes through
        Assert.NotNull(result);
        Assert.False(result.Metadata.ContainsKey(RouteTransform.RouteMetadataKey));
    }

    [Fact]
    public async Task ModeAll_CatchAllIncludedInMatches()
    {
        // Arrange - catch-all (*) should also be included
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["ok"] = JsonSerializer.SerializeToElement("ok_count > 0"),
            ["fallback"] = JsonSerializer.SerializeToElement("*"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"ok_count": 5}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - both "ok" and "fallback" matched
        Assert.NotNull(result);
        Assert.Equal("ok,fallback", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    [Fact]
    public async Task ModeAll_ThreeRoutesAllMatch()
    {
        // Arrange
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["a"] = JsonSerializer.SerializeToElement("value > 0"),
            ["b"] = JsonSerializer.SerializeToElement("value > 0"),
            ["c"] = JsonSerializer.SerializeToElement("value > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"value": 10}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("a,b,c", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    [Fact]
    public async Task ModeAll_InternalConfigKeysSkipped()
    {
        // Arrange - _route_mode should NOT appear as a route condition
        var config = new Dictionary<string, JsonElement>
        {
            ["_route_mode"] = JsonSerializer.SerializeToElement("all"),
            ["ok"] = JsonSerializer.SerializeToElement("value > 0"),
        };

        var transform = new RouteTransform();
        await transform.InitializeAsync(config);

        var message = CreateTestMessage("""{"value": 10}""");

        // Act
        var result = await transform.ProcessAsync(message);

        // Assert - only "ok", not "_route_mode"
        Assert.NotNull(result);
        Assert.Equal("ok", result.Metadata[RouteTransform.RouteMetadataKey]);
    }

    #endregion

    #region Validator: mode key not flagged as missing destination

    [Fact]
    public void Validator_RouteModeAll_DoesNotRequireModeDestination()
    {
        // Arrange - flow with mode: all, two route conditions, two destinations
        var source = @"
flow validate-mode v1.0 {
  from http(""/webhook"")

  | route {
      mode: all
      ok: ok_count > 0
      nok: nok_count > 0
    }

  ok -> console()
  nok -> console()
}";

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var flow = parser.Parse();

        var validator = FlowValidator.CreateDefault();
        var result = validator.Validate(flow);

        // Assert - no errors about missing "mode" destination
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    #endregion
}

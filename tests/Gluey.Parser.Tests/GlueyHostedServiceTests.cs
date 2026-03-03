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

using System.Runtime.CompilerServices;
using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Gluey.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gluey.Parser.Tests;

/// <summary>
/// Mock input plugin that immediately completes (empty stream) so tests don't block.
/// Tracks whether InitializeAsync was called.
/// </summary>
internal sealed class TrackingInputPlugin : IInputPlugin
{
    public bool WasInitialized { get; private set; }
    public bool WasDisposed { get; private set; }

    public string Type => "testinput";

#pragma warning disable CS1998 // Intentional: yields nothing so the service run-task completes immediately
    public async IAsyncEnumerable<Message> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield break;
    }
#pragma warning restore CS1998

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        WasInitialized = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Mock output plugin that tracks initialization and disposal.
/// A unique id lets tests distinguish instances when multiple are registered.
/// </summary>
internal sealed class TrackingOutputPlugin : IOutputPlugin
{
    public TrackingOutputPlugin(string id = "default")
    {
        Id = id;
    }

    public string Id { get; }
    public bool WasInitialized { get; private set; }
    public bool WasDisposed { get; private set; }
    public List<Message> WrittenMessages { get; } = new();

    public string Type => "testoutput";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        WasInitialized = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        WrittenMessages.Add(message);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Tests for US-066: GlueyHostedService.CreateRoutePipelineAsync with multiple outputs.
///
/// DSL route syntax:
///   | route { routename: condition }          — route transform in pipeline
///   routename -> output()                     — single destination
///   routename -> [ output1(), output2() ]     — fan-out to multiple destinations
///
/// Plugin type names must be valid DSL identifiers (no hyphens).
/// The test registry uses: "testinput", "outa", "outb", "outc".
/// </summary>
public class GlueyHostedServiceTests : IAsyncDisposable
{
    private readonly List<string> _tempFiles = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        await Task.CompletedTask;
    }

    /// <summary>Writes a temp .gflow file and registers it for cleanup.</summary>
    private string WriteTempFlow(string content)
    {
        var path = Path.GetTempFileName() + ".gflow";
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Builds a PluginRegistry with only mock plugins.
    /// Output types "outa", "outb", "outc" each append a new TrackingOutputPlugin
    /// to outputInstances when their factory is called.
    /// </summary>
    private static (PluginRegistry registry, List<TrackingOutputPlugin> outputInstances)
        BuildTestRegistry(TrackingInputPlugin inputPlugin)
    {
        var registry = new PluginRegistry();
        var outputInstances = new List<TrackingOutputPlugin>();

        registry.RegisterInput("testinput", () => inputPlugin);
        registry.RegisterOutput("outa", () => { var p = new TrackingOutputPlugin("outa"); outputInstances.Add(p); return p; });
        registry.RegisterOutput("outb", () => { var p = new TrackingOutputPlugin("outb"); outputInstances.Add(p); return p; });
        registry.RegisterOutput("outc", () => { var p = new TrackingOutputPlugin("outc"); outputInstances.Add(p); return p; });

        return (registry, outputInstances);
    }

    #region AC1 — CreateRoutePipelineAsync collects all outputs from DestinationSteps

    /// <summary>
    /// AC1: A route with a single destination collects exactly one output plugin.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_SingleOutputDestination_CollectsOnePlugin()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      dest: *\n" +
            "    }\n" +
            "\n" +
            "  dest -> outa()\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        // Only the "outa" factory should have been called
        Assert.Single(outputInstances);
        Assert.Equal("outa", outputInstances[0].Id);

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// AC1: A route with two destination outputs (fan-out syntax) collects both plugins.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_TwoOutputDestinations_CollectsBothPlugins()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        // Both "outa" and "outb" factories must have been called
        Assert.Equal(2, outputInstances.Count);
        Assert.Contains(outputInstances, p => p.Id == "outa");
        Assert.Contains(outputInstances, p => p.Id == "outb");

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// AC1: A route with three destination outputs collects all three plugins.
    /// This is the core fan-out scenario for US-066.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_ThreeOutputDestinations_CollectsAllThreePlugins()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb(), outc() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        // All three factories must have been invoked
        Assert.Equal(3, outputInstances.Count);

        await service.StopAsync(CancellationToken.None);
    }

    #endregion

    #region AC2 — Each output plugin is initialized via InitializeAsync

    /// <summary>
    /// AC2: With a single route output, InitializeAsync is called on that plugin.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_SingleOutput_IsInitialized()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      dest: *\n" +
            "    }\n" +
            "\n" +
            "  dest -> outa()\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Single(outputInstances);
        Assert.True(outputInstances[0].WasInitialized,
            "The single route output plugin must be initialized via InitializeAsync.");

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// AC2: With two route outputs (fan-out), InitializeAsync is called on both plugins.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_TwoOutputs_BothAreInitialized()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(2, outputInstances.Count);
        Assert.All(outputInstances, plugin =>
            Assert.True(plugin.WasInitialized, $"Plugin '{plugin.Id}' was not initialized."));

        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// AC2: With three route outputs (fan-out), all three are initialized.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_ThreeOutputs_AllAreInitialized()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb(), outc() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(3, outputInstances.Count);
        Assert.All(outputInstances, plugin =>
            Assert.True(plugin.WasInitialized, $"Plugin '{plugin.Id}' was not initialized."));

        await service.StopAsync(CancellationToken.None);
    }

    #endregion

    #region AC3 — DisposeAsync disposes all route output plugins

    /// <summary>
    /// AC3: DisposeAsync calls DisposeAsync on the single route output plugin.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_SingleRouteOutput_IsDisposed()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      dest: *\n" +
            "    }\n" +
            "\n" +
            "  dest -> outa()\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Single(outputInstances); // Precondition

        // Act
        await service.DisposeAsync();

        // Assert
        Assert.All(outputInstances, plugin =>
            Assert.True(plugin.WasDisposed, $"Plugin '{plugin.Id}' was not disposed."));
    }

    /// <summary>
    /// AC3: DisposeAsync disposes all output plugins for a fan-out route (two outputs).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FanOutRouteWithTwoOutputs_AllAreDisposed()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, outputInstances.Count); // Precondition

        // Act
        await service.DisposeAsync();

        // Assert — DisposeAsync iterates routePipeline.Outputs and disposes each
        Assert.All(outputInstances, plugin =>
            Assert.True(plugin.WasDisposed, $"Plugin '{plugin.Id}' was not disposed."));
    }

    /// <summary>
    /// AC3: DisposeAsync disposes all three outputs in a fan-out route.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_FanOutRouteWithThreeOutputs_AllAreDisposed()
    {
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      fanout: *\n" +
            "    }\n" +
            "\n" +
            "  fanout -> [ outa(), outb(), outc() ]\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();
        var (registry, outputInstances) = BuildTestRegistry(inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        var service = new GlueyHostedService(flowPath, registry, logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(3, outputInstances.Count); // Precondition

        // Act
        await service.DisposeAsync();

        // Assert
        Assert.All(outputInstances, plugin =>
            Assert.True(plugin.WasDisposed, $"Plugin '{plugin.Id}' was not disposed."));
    }

    #endregion

    #region AC4 — Typecheck and error handling

    /// <summary>
    /// AC4: When a route's DestinationSteps contain no registered output plugins,
    /// CreateRoutePipelineAsync throws InvalidOperationException.
    /// "json.parse" is a transform, not an output, so the route has no output.
    /// </summary>
    [Fact]
    public async Task CreateRoutePipelineAsync_RouteWithNoOutputPlugin_ThrowsInvalidOperationException()
    {
        // The route destination only lists a transform (json.parse), not an output.
        var flowContent =
            "flow testflow v1.0 {\n" +
            "  from testinput(\"/\") {}\n" +
            "\n" +
            "  | route {\n" +
            "      dest: *\n" +
            "    }\n" +
            "\n" +
            "  dest -> json.parse(payload)\n" +
            "}\n";

        var flowPath = WriteTempFlow(flowContent);
        var inputPlugin = new TrackingInputPlugin();

        // Registry with only input — no outputs registered for "dest" pipeline
        var registry = new PluginRegistry();
        registry.RegisterInput("testinput", () => inputPlugin);

        var logger = NullLogger<GlueyHostedService>.Instance;
        await using var service = new GlueyHostedService(flowPath, registry, logger);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartAsync(CancellationToken.None));

        Assert.Contains("no output destination", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}

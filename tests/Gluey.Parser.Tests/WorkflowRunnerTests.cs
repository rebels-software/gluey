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

namespace Gluey.Parser.Tests;

#region Mock Implementations

/// <summary>
/// Mock input plugin that yields a configurable list of messages via IAsyncEnumerable.
/// </summary>
internal sealed class MockInputPlugin : IInputPlugin
{
    private readonly List<Message> _messages;

    public MockInputPlugin(params Message[] messages)
    {
        _messages = new List<Message>(messages);
    }

    public string Type => "mock-input";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async IAsyncEnumerable<Message> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var message in _messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return message;
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock input plugin that cancels the provided CancellationTokenSource after yielding
/// a specified number of messages, then continues to yield more.
/// Used to test graceful cancellation mid-stream.
/// </summary>
internal sealed class CancellableInputPlugin : IInputPlugin
{
    private readonly List<Message> _messages;
    private readonly CancellationTokenSource _cts;
    private readonly int _cancelAfter;

    /// <summary>
    /// How many messages were actually yielded before cancellation took effect.
    /// </summary>
    public int YieldedCount { get; private set; }

    public CancellableInputPlugin(CancellationTokenSource cts, int cancelAfter, params Message[] messages)
    {
        _messages = new List<Message>(messages);
        _cts = cts;
        _cancelAfter = cancelAfter;
    }

    public string Type => "mock-cancellable-input";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async IAsyncEnumerable<Message> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _messages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i == _cancelAfter)
            {
                // Cancel the token after yielding the specified number of messages
                await _cts.CancelAsync();
            }

            YieldedCount++;
            yield return _messages[i];
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock transform plugin that applies a configurable function to each message.
/// Returns null to signal filtering.
/// </summary>
internal sealed class MockTransformPlugin : ITransformPlugin
{
    private readonly Func<Message, Message?> _processor;

    public MockTransformPlugin(Func<Message, Message?> processor)
    {
        _processor = processor;
    }

    public string Type => "mock-transform";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_processor(message));
    }
}

/// <summary>
/// Mock transform plugin that throws a specified exception when ProcessAsync is called.
/// </summary>
internal sealed class ThrowingTransformPlugin : ITransformPlugin
{
    private readonly Exception _exception;

    public ThrowingTransformPlugin(Exception exception)
    {
        _exception = exception;
    }

    public string Type => "mock-throwing-transform";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        throw _exception;
    }
}

/// <summary>
/// Mock output plugin that collects all written messages in a list for assertion.
/// </summary>
internal sealed class MockOutputPlugin : IOutputPlugin
{
    public List<Message> WrittenMessages { get; } = new();

    public string Type => "mock-output";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        WrittenMessages.Add(message);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

#endregion

/// <summary>
/// Tests for WorkflowRunner pipeline execution including message flow,
/// filtering, transform chaining, routing, cancellation, and error handling.
/// </summary>
public class WorkflowRunnerTests
{
    /// <summary>
    /// Helper to create a simple test message with a JSON payload.
    /// </summary>
    private static Message CreateTestMessage(string json = """{"value": 42}""", Dictionary<string, string>? metadata = null)
    {
        return new Message(
            JsonDocument.Parse(json),
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
    }

    #region Pipeline Flow Tests

    [Fact]
    public async Task MessageFlowsThroughPipeline()
    {
        // Arrange
        var inputMessage = CreateTestMessage();
        var input = new MockInputPlugin(inputMessage);
        var passThrough = new MockTransformPlugin(m => m);
        var output = new MockOutputPlugin();

        var runner = new WorkflowRunner(input, new List<ITransformPlugin> { passThrough }, output);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(output.WrittenMessages);
        Assert.Equal(inputMessage, output.WrittenMessages[0]);
    }

    [Fact]
    public async Task TransformReturningNullFiltersMessage()
    {
        // Arrange
        var inputMessage = CreateTestMessage();
        var input = new MockInputPlugin(inputMessage);
        var filterAll = new MockTransformPlugin(_ => null);
        var output = new MockOutputPlugin();

        var runner = new WorkflowRunner(input, new List<ITransformPlugin> { filterAll }, output);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - message was filtered, nothing written to output
        Assert.Empty(output.WrittenMessages);
    }

    [Fact]
    public async Task MultipleTransformsAppliedInSequence()
    {
        // Arrange - three transforms each modify the payload
        var inputMessage = CreateTestMessage("""{"step": 0}""");
        var input = new MockInputPlugin(inputMessage);

        var transform1 = new MockTransformPlugin(m =>
            m.WithPayload(JsonDocument.Parse("""{"step": 1}""")));
        var transform2 = new MockTransformPlugin(m =>
            m.WithPayload(JsonDocument.Parse("""{"step": 2}""")));
        var transform3 = new MockTransformPlugin(m =>
            m.WithPayload(JsonDocument.Parse("""{"step": 3}""")));

        var output = new MockOutputPlugin();
        var transforms = new List<ITransformPlugin> { transform1, transform2, transform3 };

        var runner = new WorkflowRunner(input, transforms, output);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - output receives the final result from transform3
        Assert.Single(output.WrittenMessages);
        var finalPayload = output.WrittenMessages[0].Payload.RootElement.GetProperty("step").GetInt32();
        Assert.Equal(3, finalPayload);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task CancellationTokenStopsProcessing()
    {
        // Arrange - input that emits 10 messages but cancels after 3
        var messages = Enumerable.Range(0, 10)
            .Select(i => CreateTestMessage($$$"""{"index": {{{i}}}}"""))
            .ToArray();

        using var cts = new CancellationTokenSource();
        var input = new CancellableInputPlugin(cts, cancelAfter: 3, messages);
        var output = new MockOutputPlugin();

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), output);

        // Act - RunAsync should complete gracefully despite mid-stream cancellation
        await runner.RunAsync(cts.Token);

        // Assert - not all messages were processed, and no exception was thrown
        Assert.True(output.WrittenMessages.Count < 10,
            "Expected cancellation to stop processing before all messages were delivered.");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ErrorInTransformPropagatesException()
    {
        // Arrange
        var inputMessage = CreateTestMessage();
        var input = new MockInputPlugin(inputMessage);
        var throwingTransform = new ThrowingTransformPlugin(new InvalidOperationException("Transform failed"));
        var output = new MockOutputPlugin();

        var runner = new WorkflowRunner(input, new List<ITransformPlugin> { throwingTransform }, output);

        // Act & Assert - the exception propagates since WorkflowRunner doesn't catch transform exceptions
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.RunAsync(CancellationToken.None));
        Assert.Equal("Transform failed", ex.Message);

        // No messages should have reached the output
        Assert.Empty(output.WrittenMessages);
    }

    #endregion

    #region Routing Tests

    [Fact]
    public async Task RoutingDirectsToCorrectOutput()
    {
        // Arrange - message with "_route" metadata pointing to "alert"
        var metadata = new Dictionary<string, string> { { "_route", "alert" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var defaultOutput = new MockOutputPlugin();
        var alertOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "alert", new RoutePipeline(alertOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), defaultOutput, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - message went to alert route, not default
        Assert.Empty(defaultOutput.WrittenMessages);
        Assert.Single(alertOutput.WrittenMessages);
        Assert.Equal(inputMessage, alertOutput.WrittenMessages[0]);
    }

    [Fact]
    public async Task DefaultOutputUsedWhenNoRouteMatch()
    {
        // Arrange - message without route metadata
        var inputMessage = CreateTestMessage();
        var input = new MockInputPlugin(inputMessage);

        var defaultOutput = new MockOutputPlugin();
        var alertOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "alert", new RoutePipeline(alertOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), defaultOutput, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - message went to default output
        Assert.Single(defaultOutput.WrittenMessages);
        Assert.Empty(alertOutput.WrittenMessages);
    }

    [Fact]
    public async Task MessageDroppedWhenNoRouteAndNoDefault()
    {
        // Arrange - message without route metadata, no default output
        var inputMessage = CreateTestMessage();
        var input = new MockInputPlugin(inputMessage);

        var alertOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "alert", new RoutePipeline(alertOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - message was dropped (no route match and no default)
        Assert.Empty(alertOutput.WrittenMessages);
    }

    [Fact]
    public async Task RouteTransformsAppliedBeforeOutput()
    {
        // Arrange - message routed to "enrich" which has a transform that modifies payload
        var metadata = new Dictionary<string, string> { { "_route", "enrich" } };
        var inputMessage = CreateTestMessage("""{"enriched": false}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var enrichTransform = new MockTransformPlugin(m =>
            m.WithPayload(JsonDocument.Parse("""{"enriched": true}""")));

        var enrichOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "enrich", new RoutePipeline(new List<ITransformPlugin> { enrichTransform }, enrichOutput) }
        };

        // No default output - only route outputs
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - the route transform was applied before writing
        Assert.Single(enrichOutput.WrittenMessages);
        var enrichedValue = enrichOutput.WrittenMessages[0].Payload.RootElement.GetProperty("enriched").GetBoolean();
        Assert.True(enrichedValue);
    }

    #endregion

    #region Fan-Out Tests (US-065)

    /// <summary>
    /// AC1 + AC2: RoutePipeline.Outputs is IReadOnlyList and fan-out uses Task.WhenAll (parallel).
    /// Verifies message is delivered to all outputs in a route with multiple outputs.
    /// </summary>
    [Fact]
    public async Task FanOutDeliversMessageToAllOutputs()
    {
        // Arrange - message routed to "broadcast" which has 3 outputs
        var metadata = new Dictionary<string, string> { { "_route", "broadcast" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();
        var output3 = new MockOutputPlugin();

        // AC1: RoutePipeline.Outputs is IReadOnlyList<IOutputPlugin>
        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2, output3 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);

        // Verify the Outputs property type is IReadOnlyList<IOutputPlugin>
        Assert.IsAssignableFrom<IReadOnlyList<IOutputPlugin>>(routePipeline.Outputs);
        Assert.Equal(3, routePipeline.Outputs.Count);

        var routeOutputs = new Dictionary<string, RoutePipeline> { { "broadcast", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - AC2: all outputs received the message (fan-out)
        Assert.Single(output1.WrittenMessages);
        Assert.Single(output2.WrittenMessages);
        Assert.Single(output3.WrittenMessages);
        Assert.Equal(inputMessage, output1.WrittenMessages[0]);
        Assert.Equal(inputMessage, output2.WrittenMessages[0]);
        Assert.Equal(inputMessage, output3.WrittenMessages[0]);
    }

    /// <summary>
    /// AC3: An error in one output does not stop the others from completing.
    /// </summary>
    [Fact]
    public async Task FanOutContinuesWhenOneOutputFails()
    {
        // Arrange - message routed to "fanout" with one failing and two healthy outputs
        var metadata = new Dictionary<string, string> { { "_route", "fanout" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var goodOutput1 = new MockOutputPlugin();
        var goodOutput2 = new MockOutputPlugin();
        var failingOutput = new ThrowingOutputPlugin(new InvalidOperationException("Output failure"));

        // Fan-out: [goodOutput1, failingOutput, goodOutput2]
        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin>
            { goodOutput1, failingOutput, goodOutput2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "fanout", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act - should not throw even though one output throws
        await runner.RunAsync(CancellationToken.None);

        // Assert - AC3: the two healthy outputs still received the message
        Assert.Single(goodOutput1.WrittenMessages);
        Assert.Single(goodOutput2.WrittenMessages);
    }

    /// <summary>
    /// AC1 backward compat: Single-output RoutePipeline constructor wraps IOutputPlugin in list.
    /// </summary>
    [Fact]
    public void RoutePipelineSingleOutputConstructorWrapsInList()
    {
        var output = new MockOutputPlugin();
        var pipeline = new RoutePipeline(output);

        Assert.IsAssignableFrom<IReadOnlyList<IOutputPlugin>>(pipeline.Outputs);
        Assert.Single(pipeline.Outputs);
        Assert.Same(output, pipeline.Outputs[0]);
    }

    /// <summary>
    /// AC1 backward compat: transforms + single output constructor wraps IOutputPlugin in list.
    /// </summary>
    [Fact]
    public void RoutePipelineTransformsAndSingleOutputConstructorWrapsInList()
    {
        var output = new MockOutputPlugin();
        var transforms = new List<ITransformPlugin> { new MockTransformPlugin(m => m) };
        var pipeline = new RoutePipeline(transforms, output);

        Assert.IsAssignableFrom<IReadOnlyList<IOutputPlugin>>(pipeline.Outputs);
        Assert.Single(pipeline.Outputs);
        Assert.Same(output, pipeline.Outputs[0]);
        Assert.Same(transforms, pipeline.Transforms);
    }

    /// <summary>
    /// Validates RoutePipeline rejects empty outputs list.
    /// </summary>
    [Fact]
    public void RoutePipelineRequiresAtLeastOneOutput()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new RoutePipeline(new List<ITransformPlugin>(), new List<IOutputPlugin>()));
        Assert.Contains("At least one output", ex.Message);
    }

    #endregion
}

/// <summary>
/// Mock output plugin that throws a specified exception when WriteAsync is called.
/// Used to test fan-out error isolation (AC3).
/// </summary>
internal sealed class ThrowingOutputPlugin : IOutputPlugin
{
    private readonly Exception _exception;

    public ThrowingOutputPlugin(Exception exception)
    {
        _exception = exception;
    }

    public string Type => "mock-throwing-output";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task WriteAsync(Message message, CancellationToken cancellationToken = default)
        => Task.FromException(_exception);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

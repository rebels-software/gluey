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

    #region Fan-Out Behavior Tests (US-067)

    // --- AC1: Single message sent to multiple outputs ---

    /// <summary>
    /// AC1: Verifies that a single message fans out to exactly 2 outputs (minimum fan-out edge case).
    /// </summary>
    [Fact]
    public async Task FanOutDeliversMessageToTwoOutputs()
    {
        // Arrange - route with 2 outputs (minimum fan-out)
        var metadata = new Dictionary<string, string> { { "_route", "duo" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "duo", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - both outputs received the message
        Assert.Single(output1.WrittenMessages);
        Assert.Single(output2.WrittenMessages);
    }

    /// <summary>
    /// AC1: Verifies that multiple messages each fan out to all outputs in a route pipeline.
    /// </summary>
    [Fact]
    public async Task FanOutDeliversMultipleMessagesToAllOutputs()
    {
        // Arrange - 3 messages routed to "multi" with 2 outputs
        var messages = Enumerable.Range(0, 3)
            .Select(i => CreateTestMessage(
                $$$"""{"index": {{{i}}}}""",
                new Dictionary<string, string> { { "_route", "multi" } }))
            .ToArray();

        var input = new MockInputPlugin(messages);
        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "multi", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - both outputs received all 3 messages
        Assert.Equal(3, output1.WrittenMessages.Count);
        Assert.Equal(3, output2.WrittenMessages.Count);
    }

    // --- AC2: Error in one output does not affect others ---

    /// <summary>
    /// AC2: When the first output throws, the remaining outputs still receive the message.
    /// </summary>
    [Fact]
    public async Task FanOutFirstOutputFailsOthersStillReceive()
    {
        // Arrange - first output fails, second and third are healthy
        var metadata = new Dictionary<string, string> { { "_route", "err-first" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var failingOutput = new ThrowingOutputPlugin(new InvalidOperationException("First output exploded"));
        var goodOutput2 = new MockOutputPlugin();
        var goodOutput3 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { failingOutput, goodOutput2, goodOutput3 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "err-first", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act - should not throw
        await runner.RunAsync(CancellationToken.None);

        // Assert - healthy outputs still received the message
        Assert.Single(goodOutput2.WrittenMessages);
        Assert.Single(goodOutput3.WrittenMessages);
    }

    /// <summary>
    /// AC2: When the middle output throws, the first and last outputs still receive the message.
    /// </summary>
    [Fact]
    public async Task FanOutMiddleOutputFailsOthersStillReceive()
    {
        // Arrange - middle output fails, first and last are healthy
        var metadata = new Dictionary<string, string> { { "_route", "err-mid" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var goodOutput1 = new MockOutputPlugin();
        var failingOutput = new ThrowingOutputPlugin(new IOException("Middle output failed"));
        var goodOutput3 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { goodOutput1, failingOutput, goodOutput3 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "err-mid", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert
        Assert.Single(goodOutput1.WrittenMessages);
        Assert.Single(goodOutput3.WrittenMessages);
    }

    /// <summary>
    /// AC2: When multiple outputs fail, the runner does not crash and healthy outputs still receive.
    /// </summary>
    [Fact]
    public async Task FanOutMultipleOutputsFailRunnerDoesNotCrash()
    {
        // Arrange - 2 out of 3 outputs fail
        var metadata = new Dictionary<string, string> { { "_route", "err-multi" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var failingOutput1 = new ThrowingOutputPlugin(new InvalidOperationException("Failure 1"));
        var failingOutput2 = new ThrowingOutputPlugin(new TimeoutException("Failure 2"));
        var goodOutput = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { failingOutput1, failingOutput2, goodOutput };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "err-multi", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act - should not throw despite 2 of 3 outputs failing
        await runner.RunAsync(CancellationToken.None);

        // Assert - the surviving output received the message
        Assert.Single(goodOutput.WrittenMessages);
    }

    // --- AC3: All outputs receive same message content ---

    /// <summary>
    /// AC3: All fan-out outputs receive messages with identical payload bytes.
    /// </summary>
    [Fact]
    public async Task FanOutAllOutputsReceiveIdenticalPayload()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "_route", "payload-check" } };
        var inputMessage = CreateTestMessage("""{"sensor": "temp", "value": 23.5}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();
        var output3 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2, output3 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "payload-check", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - all outputs received identical payload (compare via GetRawText())
        var expectedPayload = inputMessage.Payload.RootElement.GetRawText();
        Assert.Equal(expectedPayload, output1.WrittenMessages[0].Payload.RootElement.GetRawText());
        Assert.Equal(expectedPayload, output2.WrittenMessages[0].Payload.RootElement.GetRawText());
        Assert.Equal(expectedPayload, output3.WrittenMessages[0].Payload.RootElement.GetRawText());
    }

    /// <summary>
    /// AC3: All fan-out outputs receive messages with identical metadata.
    /// </summary>
    [Fact]
    public async Task FanOutAllOutputsReceiveIdenticalMetadata()
    {
        // Arrange - message with multiple metadata entries
        var metadata = new Dictionary<string, string>
        {
            { "_route", "meta-check" },
            { "source", "sensor-01" },
            { "region", "eu-west" }
        };
        var inputMessage = CreateTestMessage("""{"value": 99}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "meta-check", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - both outputs received identical metadata
        var msg1 = output1.WrittenMessages[0];
        var msg2 = output2.WrittenMessages[0];

        Assert.Equal(msg1.Metadata.Count, msg2.Metadata.Count);
        foreach (var kvp in msg1.Metadata)
        {
            Assert.True(msg2.Metadata.ContainsKey(kvp.Key), $"Missing metadata key: {kvp.Key}");
            Assert.Equal(kvp.Value, msg2.Metadata[kvp.Key]);
        }
    }

    // --- AC4: Parallel execution (timing verification) ---

    /// <summary>
    /// AC4: Verifies that fan-out outputs execute in parallel using Task.WhenAll.
    /// Three outputs each with 100ms delay should complete in ~100-150ms if parallel,
    /// not ~300ms+ if sequential.
    /// </summary>
    [Fact]
    public async Task FanOutExecutesOutputsInParallel()
    {
        // Arrange - 3 delayed outputs with 100ms delay each
        var metadata = new Dictionary<string, string> { { "_route", "parallel" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var delay = TimeSpan.FromMilliseconds(100);
        var delayedOutput1 = new DelayedOutputPlugin(delay);
        var delayedOutput2 = new DelayedOutputPlugin(delay);
        var delayedOutput3 = new DelayedOutputPlugin(delay);

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin>
            { delayedOutput1, delayedOutput2, delayedOutput3 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "parallel", routePipeline } };
        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act - measure elapsed time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await runner.RunAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert - if parallel: ~100ms. If sequential: ~300ms. Use 250ms as threshold.
        Assert.True(stopwatch.ElapsedMilliseconds < 250,
            $"Fan-out took {stopwatch.ElapsedMilliseconds}ms, expected < 250ms for parallel execution.");

        // Also verify all outputs received the message
        Assert.Single(delayedOutput1.WrittenMessages);
        Assert.Single(delayedOutput2.WrittenMessages);
        Assert.Single(delayedOutput3.WrittenMessages);
    }

    #endregion

    #region Multi-Route Tests (mode: all)

    /// <summary>
    /// When _route metadata contains comma-separated route names, message is delivered
    /// to ALL matching route pipelines.
    /// </summary>
    [Fact]
    public async Task MultiRoute_DeliversToAllMatchingPipelines()
    {
        // Arrange - message with comma-separated routes
        var metadata = new Dictionary<string, string> { { "_route", "ok,nok" } };
        var inputMessage = CreateTestMessage("""{"value": 1}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var okOutput = new MockOutputPlugin();
        var nokOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "ok", new RoutePipeline(okOutput) },
            { "nok", new RoutePipeline(nokOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - both outputs received the message
        Assert.Single(okOutput.WrittenMessages);
        Assert.Single(nokOutput.WrittenMessages);
    }

    /// <summary>
    /// When _route metadata has comma-separated names and some don't match,
    /// only the matching ones receive the message.
    /// </summary>
    [Fact]
    public async Task MultiRoute_OnlyMatchingPipelinesReceive()
    {
        // Arrange - "ok" matches, "missing" does not exist
        var metadata = new Dictionary<string, string> { { "_route", "ok,missing" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var okOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "ok", new RoutePipeline(okOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - only the matched pipeline received the message
        Assert.Single(okOutput.WrittenMessages);
    }

    /// <summary>
    /// When _route metadata has comma-separated names and NONE match any pipeline,
    /// and there's no default output, the message is dropped.
    /// </summary>
    [Fact]
    public async Task MultiRoute_NoMatchesFallsToDefault()
    {
        // Arrange - "x,y" don't match any route; default output exists
        var metadata = new Dictionary<string, string> { { "_route", "x,y" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var defaultOutput = new MockOutputPlugin();
        var okOutput = new MockOutputPlugin();
        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "ok", new RoutePipeline(okOutput) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), defaultOutput, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - falls to default
        Assert.Single(defaultOutput.WrittenMessages);
        Assert.Empty(okOutput.WrittenMessages);
    }

    /// <summary>
    /// Multi-route fan-out applies route-specific transforms before writing to outputs.
    /// </summary>
    [Fact]
    public async Task MultiRoute_AppliesRouteTransformsBeforeOutput()
    {
        // Arrange - both routes have transforms that modify the payload
        var metadata = new Dictionary<string, string> { { "_route", "enrich1,enrich2" } };
        var inputMessage = CreateTestMessage("""{"status": "raw"}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var enrichTransform1 = new MockTransformPlugin(m =>
            m.WithPayload(System.Text.Json.JsonDocument.Parse("""{"status": "enriched1"}""")));
        var enrichTransform2 = new MockTransformPlugin(m =>
            m.WithPayload(System.Text.Json.JsonDocument.Parse("""{"status": "enriched2"}""")));

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        var routeOutputs = new Dictionary<string, RoutePipeline>
        {
            { "enrich1", new RoutePipeline(new List<ITransformPlugin> { enrichTransform1 }, output1) },
            { "enrich2", new RoutePipeline(new List<ITransformPlugin> { enrichTransform2 }, output2) }
        };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert - each route's transform was applied independently
        Assert.Single(output1.WrittenMessages);
        Assert.Single(output2.WrittenMessages);
        Assert.Equal("enriched1", output1.WrittenMessages[0].Payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("enriched2", output2.WrittenMessages[0].Payload.RootElement.GetProperty("status").GetString());
    }

    /// <summary>
    /// Single route value (no comma) still uses the existing single-route behavior.
    /// </summary>
    [Fact]
    public async Task SingleRoute_StillWorksAfterMultiRouteChanges()
    {
        // Arrange - single route, no commas
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

        // Assert - only alert route received it, not default
        Assert.Empty(defaultOutput.WrittenMessages);
        Assert.Single(alertOutput.WrittenMessages);
    }

    #endregion

    #region Output Plugin Config Regression Tests (parenthesized connection string / URL)

    // Regression: | sql("Host=...") stored the connection string as "field" in Config,
    // but the old MergeOutputConfig only looked for "target". The fix checks both "target"
    // and "field" before writing to "url". These tests pin the parser's storage key ("field")
    // and verify that the merging logic produces the expected "url" key.

    /// <summary>
    /// Helper that replicates the MergeOutputConfig logic from WorkflowManager
    /// so tests can exercise the same behaviour without accessing the private method.
    /// </summary>
    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> MergeOutputConfig(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> stepConfig)
    {
        var config = new Dictionary<string, System.Text.Json.JsonElement>(stepConfig);

        if (!config.ContainsKey("url"))
        {
            System.Text.Json.JsonElement? source = null;
            if (config.TryGetValue("target", out var targetElement)
                && targetElement.ValueKind == System.Text.Json.JsonValueKind.String)
                source = targetElement;
            else if (config.TryGetValue("field", out var fieldElement)
                && fieldElement.ValueKind == System.Text.Json.JsonValueKind.String)
                source = fieldElement;

            if (source.HasValue)
                config["url"] = source.Value;
        }

        return config;
    }

    [Fact]
    public void SqlOutputAsLastPipelineStep_ParserStoresConnectionStringAsFieldKey()
    {
        // Arrange - gflow with sql() as the final (output) pipeline step
        const string gflow = """
            flow test-sql v1.0 {
              from http("/webhook") { port: 9999 }
              | sql("Host=postgres;Port=5432;Database=test;Username=user;Password=pass") {
                  table: "readings"
                  columns: { id: "id" }
                }
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        // Assert - the last pipeline step is parsed as "sql"
        var lastStep = flow.PipelineSteps[^1];
        Assert.Equal("sql", lastStep.Type);

        // The parser stores the parenthesized arg under "field"
        Assert.True(lastStep.Config.ContainsKey("field"),
            "Parser must store the parenthesized argument as 'field' in Config");
        Assert.Equal(
            "Host=postgres;Port=5432;Database=test;Username=user;Password=pass",
            lastStep.Config["field"].GetString());
    }

    [Fact]
    public void SqlOutputAsLastPipelineStep_MergeOutputConfigProducesUrlKey()
    {
        // Arrange - same gflow as above
        const string gflow = """
            flow test-sql v1.0 {
              from http("/webhook") { port: 9999 }
              | sql("Host=postgres;Port=5432;Database=test;Username=user;Password=pass") {
                  table: "readings"
                  columns: { id: "id" }
                }
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();
        var lastStep = flow.PipelineSteps[^1];

        // Act - apply the same config-merging logic that WorkflowManager uses
        var mergedConfig = MergeOutputConfig(lastStep.Config);

        // Assert - the merged config must expose the connection string as "url"
        Assert.True(mergedConfig.ContainsKey("url"),
            "MergeOutputConfig must promote 'field' value to 'url' when 'target' is absent");
        Assert.Equal(
            "Host=postgres;Port=5432;Database=test;Username=user;Password=pass",
            mergedConfig["url"].GetString());
    }

    [Fact]
    public void MqttOutputAsLastPipelineStep_ParserStoresBrokerUrlAsFieldKey()
    {
        // Arrange - gflow with mqtt() as the final pipeline step
        const string gflow = """
            flow test-mqtt v1.0 {
              from http("/webhook") { port: 9998 }
              | mqtt("mqtt://broker:1883")
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        var lastStep = flow.PipelineSteps[^1];
        Assert.Equal("mqtt", lastStep.Type);

        Assert.True(lastStep.Config.ContainsKey("field"),
            "Parser must store the parenthesized argument as 'field' in Config");
        Assert.Equal("mqtt://broker:1883", lastStep.Config["field"].GetString());
    }

    [Fact]
    public void MqttOutputAsLastPipelineStep_MergeOutputConfigProducesUrlKey()
    {
        // Arrange
        const string gflow = """
            flow test-mqtt v1.0 {
              from http("/webhook") { port: 9998 }
              | mqtt("mqtt://broker:1883")
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();
        var lastStep = flow.PipelineSteps[^1];

        // Act
        var mergedConfig = MergeOutputConfig(lastStep.Config);

        // Assert
        Assert.True(mergedConfig.ContainsKey("url"),
            "MergeOutputConfig must promote 'field' value to 'url' when 'target' is absent");
        Assert.Equal("mqtt://broker:1883", mergedConfig["url"].GetString());
    }

    [Fact]
    public void HttpOutputAsLastPipelineStep_ParserStoresWebhookUrlAsFieldKey()
    {
        // Arrange - gflow with http() as the final pipeline step
        const string gflow = """
            flow test-http v1.0 {
              from http("/webhook") { port: 9997 }
              | http("https://example.com/webhook")
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        var lastStep = flow.PipelineSteps[^1];
        Assert.Equal("http", lastStep.Type);

        Assert.True(lastStep.Config.ContainsKey("field"),
            "Parser must store the parenthesized argument as 'field' in Config");
        Assert.Equal("https://example.com/webhook", lastStep.Config["field"].GetString());
    }

    [Fact]
    public void HttpOutputAsLastPipelineStep_MergeOutputConfigProducesUrlKey()
    {
        // Arrange
        const string gflow = """
            flow test-http v1.0 {
              from http("/webhook") { port: 9997 }
              | http("https://example.com/webhook")
            }
            """;

        var lexer = new Gluey.Parser.Lexer(gflow);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();
        var lastStep = flow.PipelineSteps[^1];

        // Act
        var mergedConfig = MergeOutputConfig(lastStep.Config);

        // Assert
        Assert.True(mergedConfig.ContainsKey("url"),
            "MergeOutputConfig must promote 'field' value to 'url' when 'target' is absent");
        Assert.Equal("https://example.com/webhook", mergedConfig["url"].GetString());
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

/// <summary>
/// Mock output plugin that introduces a configurable delay in WriteAsync.
/// Used to verify parallel fan-out execution via timing assertions.
/// </summary>
internal sealed class DelayedOutputPlugin : IOutputPlugin
{
    private readonly TimeSpan _delay;

    public List<Message> WrittenMessages { get; } = new();

    public DelayedOutputPlugin(TimeSpan delay)
    {
        _delay = delay;
    }

    public string Type => "mock-delayed-output";

    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delay, cancellationToken);
        WrittenMessages.Add(message);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

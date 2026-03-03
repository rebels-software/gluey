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
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Gluey.Runtime;
using GlueyParser = Gluey.Parser;

namespace Gluey.Parser.Tests;

/// <summary>
/// End-to-end verification tests for fan-out behavior (US-068).
/// Tests the full path from DSL parsing through WorkflowRunner delivery to multiple outputs.
/// </summary>
public class FanOutVerificationTests
{
    /// <summary>
    /// Helper to create a simple test message with a JSON payload and optional metadata.
    /// </summary>
    private static Message CreateTestMessage(string json = """{"value": 42}""", Dictionary<string, string>? metadata = null)
    {
        return new Message(
            JsonDocument.Parse(json),
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
    }

    #region AC1 — Parse test-fanout.gflow and verify 2 destination steps

    /// <summary>
    /// AC1: The test-fanout.gflow file parses correctly with the expected structure.
    /// Verifies the parser handles route -> [console(), console()] fan-out syntax.
    /// </summary>
    [Fact]
    public void ParseTestFanoutGflow_HasCorrectStructure()
    {
        // Arrange — read the actual .gflow file from disk
        var gflowPath = Path.Combine(FindSamplesDirectory(), "test-fanout.gflow");
        var source = File.ReadAllText(gflowPath);

        // Act — parse using the real lexer and parser
        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        // Assert — flow metadata
        Assert.Equal("fanout-test", flow.Name);
        Assert.Equal("1.0", flow.Version);

        // Assert — input is http on /webhook
        Assert.Equal("http", flow.Input.Type);
        Assert.Equal("/webhook", flow.Input.Url);

        // Assert — pipeline has json.parse + route
        Assert.Equal(2, flow.PipelineSteps.Count);
        Assert.Equal("json.parse", flow.PipelineSteps[0].Type);
        Assert.Equal("route", flow.PipelineSteps[1].Type);

        // Assert — single route named "all" with catch-all condition
        Assert.Single(flow.Routes);
        var route = flow.Routes[0];
        Assert.Equal("all", route.Name);
        Assert.Equal("*", route.Condition);
        Assert.True(route.IsCatchAll);
    }

    /// <summary>
    /// AC1: The "all" route in test-fanout.gflow has exactly 2 destination steps (two console outputs).
    /// </summary>
    [Fact]
    public void ParseTestFanoutGflow_HasTwoDestinationSteps()
    {
        // Arrange
        var gflowPath = Path.Combine(FindSamplesDirectory(), "test-fanout.gflow");
        var source = File.ReadAllText(gflowPath);

        // Act
        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        // Assert — 2 destination steps, both console
        var route = flow.Routes[0];
        Assert.Equal(2, route.DestinationSteps.Count);
        Assert.Equal("console", route.DestinationSteps[0].Type);
        Assert.Equal("console", route.DestinationSteps[1].Type);
    }

    #endregion

    #region AC2 — WorkflowRunner with 2 mock outputs delivers message to BOTH

    /// <summary>
    /// AC2: A WorkflowRunner with 2 outputs in a route pipeline delivers a single
    /// message to both outputs. This simulates the full fan-out path from input
    /// through routing to parallel output delivery.
    /// </summary>
    [Fact]
    public async Task FanOut_SingleMessageDeliveredToBothOutputs()
    {
        // Arrange — message with _route metadata pointing to the route name
        var metadata = new Dictionary<string, string> { { "_route", "all" } };
        var inputMessage = CreateTestMessage("""{"sensor": "temp", "value": 23.5}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        // Build a RoutePipeline with 2 outputs (simulating the parsed fan-out)
        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "all", routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert — both outputs received the message
        Assert.Single(output1.WrittenMessages);
        Assert.Single(output2.WrittenMessages);

        // Assert — both received identical payload
        var expectedPayload = inputMessage.Payload.RootElement.GetRawText();
        Assert.Equal(expectedPayload, output1.WrittenMessages[0].Payload.RootElement.GetRawText());
        Assert.Equal(expectedPayload, output2.WrittenMessages[0].Payload.RootElement.GetRawText());
    }

    /// <summary>
    /// AC2: Multiple messages each fan out to both outputs.
    /// </summary>
    [Fact]
    public async Task FanOut_MultipleMessagesDeliveredToBothOutputs()
    {
        // Arrange — 5 messages, all routed to "all"
        var messages = Enumerable.Range(0, 5)
            .Select(i => CreateTestMessage(
                $$$"""{"index": {{{i}}}}""",
                new Dictionary<string, string> { { "_route", "all" } }))
            .ToArray();

        var input = new MockInputPlugin(messages);
        var output1 = new MockOutputPlugin();
        var output2 = new MockOutputPlugin();

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { output1, output2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "all", routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert — both outputs received all 5 messages
        Assert.Equal(5, output1.WrittenMessages.Count);
        Assert.Equal(5, output2.WrittenMessages.Count);
    }

    #endregion

    #region AC3 — Error in one output does not prevent other from receiving message

    /// <summary>
    /// AC3: When one output throws an exception, the other output still receives the message.
    /// This verifies SafeWriteAsync error isolation in the fan-out path.
    /// </summary>
    [Fact]
    public async Task FanOut_ErrorInOneOutputDoesNotBlockOther()
    {
        // Arrange — message routed to "all" with one throwing output
        var metadata = new Dictionary<string, string> { { "_route", "all" } };
        var inputMessage = CreateTestMessage("""{"critical": true}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var healthyOutput = new MockOutputPlugin();
        var failingOutput = new ThrowingOutputPlugin(new InvalidOperationException("Output crashed"));

        // Fan-out: [healthyOutput, failingOutput]
        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { healthyOutput, failingOutput };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "all", routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act — should not throw despite the failing output
        await runner.RunAsync(CancellationToken.None);

        // Assert — healthy output still received the message
        Assert.Single(healthyOutput.WrittenMessages);
        Assert.Equal(
            inputMessage.Payload.RootElement.GetRawText(),
            healthyOutput.WrittenMessages[0].Payload.RootElement.GetRawText());
    }

    /// <summary>
    /// AC3: When the first output throws, the second still receives the message (order independence).
    /// </summary>
    [Fact]
    public async Task FanOut_FirstOutputFailsSecondStillReceives()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "_route", "all" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var failingOutput = new ThrowingOutputPlugin(new IOException("Network error"));
        var healthyOutput = new MockOutputPlugin();

        // Fan-out: [failingOutput, healthyOutput] — failing output is first
        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { failingOutput, healthyOutput };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "all", routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act
        await runner.RunAsync(CancellationToken.None);

        // Assert — second output received the message
        Assert.Single(healthyOutput.WrittenMessages);
    }

    /// <summary>
    /// AC3: When both outputs throw, the runner does not crash.
    /// </summary>
    [Fact]
    public async Task FanOut_AllOutputsFailRunnerDoesNotCrash()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "_route", "all" } };
        var inputMessage = CreateTestMessage(metadata: metadata);
        var input = new MockInputPlugin(inputMessage);

        var failingOutput1 = new ThrowingOutputPlugin(new InvalidOperationException("Fail 1"));
        var failingOutput2 = new ThrowingOutputPlugin(new TimeoutException("Fail 2"));

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { failingOutput1, failingOutput2 };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { "all", routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Act & Assert — should complete without throwing
        var exception = await Record.ExceptionAsync(() => runner.RunAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    #endregion

    #region End-to-End: Parse + Build Runner + Deliver

    /// <summary>
    /// End-to-end: Parses the test-fanout.gflow file, builds a WorkflowRunner from
    /// the parsed structure with mock plugins, and verifies message delivery to both outputs.
    /// This ties the parser output to the runtime fan-out behavior.
    /// </summary>
    [Fact]
    public async Task EndToEnd_ParsedFanoutFlowDeliversToBothOutputs()
    {
        // Step 1: Parse the .gflow file
        var gflowPath = Path.Combine(FindSamplesDirectory(), "test-fanout.gflow");
        var source = File.ReadAllText(gflowPath);

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        // Verify parsed structure has fan-out
        Assert.Single(flow.Routes);
        var route = flow.Routes[0];
        Assert.Equal(2, route.DestinationSteps.Count);

        // Step 2: Build a WorkflowRunner from the parsed route structure
        var metadata = new Dictionary<string, string> { { "_route", route.Name } };
        var inputMessage = CreateTestMessage("""{"device": "sensor-01", "temp": 22.5}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        // Create one mock output per destination step (the parser found 2 console steps)
        var mockOutputs = route.DestinationSteps
            .Select(_ => new MockOutputPlugin())
            .ToList();

        IReadOnlyList<IOutputPlugin> outputs = mockOutputs;
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { route.Name, routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Step 3: Run the pipeline
        await runner.RunAsync(CancellationToken.None);

        // Step 4: Assert all outputs received the message
        Assert.All(mockOutputs, output =>
        {
            Assert.Single(output.WrittenMessages);
            Assert.Equal(
                inputMessage.Payload.RootElement.GetRawText(),
                output.WrittenMessages[0].Payload.RootElement.GetRawText());
        });
    }

    /// <summary>
    /// End-to-end: Parses test-fanout.gflow, builds runner with one failing output,
    /// and verifies the healthy output still receives the message despite the error.
    /// </summary>
    [Fact]
    public async Task EndToEnd_ParsedFanoutFlowWithErrorIsolation()
    {
        // Step 1: Parse the .gflow file
        var gflowPath = Path.Combine(FindSamplesDirectory(), "test-fanout.gflow");
        var source = File.ReadAllText(gflowPath);

        var lexer = new GlueyParser.Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new GlueyParser.Parser(tokens);
        var flow = parser.Parse();

        var route = flow.Routes[0];
        Assert.Equal(2, route.DestinationSteps.Count);

        // Step 2: Build runner with one healthy + one failing output
        var metadata = new Dictionary<string, string> { { "_route", route.Name } };
        var inputMessage = CreateTestMessage("""{"device": "sensor-02", "temp": 95.0}""", metadata);
        var input = new MockInputPlugin(inputMessage);

        var healthyOutput = new MockOutputPlugin();
        var failingOutput = new ThrowingOutputPlugin(new InvalidOperationException("Output down"));

        IReadOnlyList<IOutputPlugin> outputs = new List<IOutputPlugin> { healthyOutput, failingOutput };
        var routePipeline = new RoutePipeline(new List<ITransformPlugin>(), outputs);
        var routeOutputs = new Dictionary<string, RoutePipeline> { { route.Name, routePipeline } };

        var runner = new WorkflowRunner(input, new List<ITransformPlugin>(), null, routeOutputs);

        // Step 3: Run — should not throw
        await runner.RunAsync(CancellationToken.None);

        // Step 4: Healthy output got the message
        Assert.Single(healthyOutput.WrittenMessages);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Finds the samples/ directory relative to the test binary output.
    /// Walks up from the test output directory to find the engine/samples folder.
    /// </summary>
    private static string FindSamplesDirectory()
    {
        // Start from the test assembly location and walk up to find "samples"
        var dir = AppContext.BaseDirectory;

        // Walk up until we find the engine root (contains "samples" directory)
        for (var i = 0; i < 10; i++)
        {
            var samplesPath = Path.Combine(dir, "samples");
            if (Directory.Exists(samplesPath))
            {
                return samplesPath;
            }
            dir = Path.GetDirectoryName(dir)!;
        }

        throw new DirectoryNotFoundException(
            "Could not find 'samples' directory. Ensure the test is run from the engine directory.");
    }

    #endregion
}

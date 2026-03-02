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

using Gluey.Runtime.Logging;
using Microsoft.Extensions.Logging;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for LogBuffer thread-safe ring buffer and subscription behavior.
/// </summary>
public class LogBufferTests
{
    private static LogEntry MakeEntry(string workflowName, string message, LogLevel level = LogLevel.Information)
    {
        return new LogEntry(DateTimeOffset.UtcNow, workflowName, level, message);
    }

    // ------------------------------------------------------------------ //
    // AC-1: GetLogs returns entries for a workflow
    // ------------------------------------------------------------------ //

    [Fact]
    public void GetLogs_ReturnsEntries_ForMatchingWorkflow()
    {
        var buffer = new LogBuffer();
        buffer.Add(MakeEntry("my-flow", "hello"));
        buffer.Add(MakeEntry("my-flow", "world"));

        var logs = buffer.GetLogs("my-flow");

        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Message == "hello");
        Assert.Contains(logs, l => l.Message == "world");
    }

    [Fact]
    public void GetLogs_ReturnsEmpty_ForUnknownWorkflow()
    {
        var buffer = new LogBuffer();
        buffer.Add(MakeEntry("flow-a", "msg"));

        var logs = buffer.GetLogs("flow-b");

        Assert.Empty(logs);
    }

    [Fact]
    public void GetLogs_IsCaseInsensitive()
    {
        var buffer = new LogBuffer();
        buffer.Add(MakeEntry("MyFlow", "msg"));

        var logsLower = buffer.GetLogs("myflow");
        var logsUpper = buffer.GetLogs("MYFLOW");

        Assert.Single(logsLower);
        Assert.Single(logsUpper);
    }

    [Fact]
    public void GetLogs_ReturnsOldestFirst()
    {
        var buffer = new LogBuffer();
        for (int i = 1; i <= 5; i++)
            buffer.Add(MakeEntry("flow", $"msg-{i}"));

        var logs = buffer.GetLogs("flow");

        Assert.Equal("msg-1", logs[0].Message);
        Assert.Equal("msg-5", logs[^1].Message);
    }

    // ------------------------------------------------------------------ //
    // AC-2: Ring buffer caps at 1000 entries per workflow
    // ------------------------------------------------------------------ //

    [Fact]
    public void Add_TrimsToMaxEntries_WhenExceeding1000()
    {
        var buffer = new LogBuffer();

        // Add 1005 entries; buffer should trim to 1000
        for (int i = 1; i <= 1005; i++)
            buffer.Add(MakeEntry("flow", $"msg-{i}"));

        var logs = buffer.GetLogs("flow", 2000); // request more than max

        Assert.Equal(1000, logs.Count);
        // The first 5 entries should have been dropped; oldest retained should be msg-6
        Assert.Equal("msg-6", logs[0].Message);
        Assert.Equal("msg-1005", logs[^1].Message);
    }

    // ------------------------------------------------------------------ //
    // AC-3: GetLogs respects the count parameter
    // ------------------------------------------------------------------ //

    [Fact]
    public void GetLogs_RespectsCountParameter()
    {
        var buffer = new LogBuffer();
        for (int i = 1; i <= 50; i++)
            buffer.Add(MakeEntry("flow", $"msg-{i}"));

        var logs = buffer.GetLogs("flow", 10);

        Assert.Equal(10, logs.Count);
        // Should return the 10 most recent: msg-41 through msg-50
        Assert.Equal("msg-41", logs[0].Message);
        Assert.Equal("msg-50", logs[^1].Message);
    }

    [Fact]
    public void GetLogs_ReturnsAll_WhenCountExceedsBuffer()
    {
        var buffer = new LogBuffer();
        for (int i = 1; i <= 5; i++)
            buffer.Add(MakeEntry("flow", $"msg-{i}"));

        var logs = buffer.GetLogs("flow", 100);

        Assert.Equal(5, logs.Count);
    }

    // ------------------------------------------------------------------ //
    // AC-4: Subscribe delivers entries to live subscribers
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Subscribe_ReceivesEntriesAddedAfterSubscription()
    {
        var buffer = new LogBuffer();
        var received = new List<LogEntry>();

        using var cts = new CancellationTokenSource();

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in buffer.Subscribe("flow", cts.Token))
                {
                    received.Add(entry);
                    if (received.Count >= 3)
                        cts.Cancel();
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        // Brief delay to ensure subscription is registered
        await Task.Delay(50);

        buffer.Add(MakeEntry("flow", "a"));
        buffer.Add(MakeEntry("flow", "b"));
        buffer.Add(MakeEntry("flow", "c"));

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, received.Count);
        Assert.Equal("a", received[0].Message);
        Assert.Equal("b", received[1].Message);
        Assert.Equal("c", received[2].Message);
    }

    [Fact]
    public async Task Subscribe_DoesNotReceiveEntriesForOtherWorkflows()
    {
        var buffer = new LogBuffer();
        var received = new List<LogEntry>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in buffer.Subscribe("flow-a", cts.Token))
                {
                    received.Add(entry);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        buffer.Add(MakeEntry("flow-b", "other workflow"));

        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(received);
    }

    [Fact]
    public async Task Subscribe_MultipleSubscribers_AllReceiveEntries()
    {
        var buffer = new LogBuffer();
        var received1 = new List<string>();
        var received2 = new List<string>();

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        var t1 = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in buffer.Subscribe("flow", cts1.Token))
                {
                    received1.Add(e.Message);
                    if (received1.Count >= 2) cts1.Cancel();
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in buffer.Subscribe("flow", cts2.Token))
                {
                    received2.Add(e.Message);
                    if (received2.Count >= 2) cts2.Cancel();
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        await Task.Delay(50);

        buffer.Add(MakeEntry("flow", "msg-1"));
        buffer.Add(MakeEntry("flow", "msg-2"));

        await Task.WhenAll(t1.WaitAsync(TimeSpan.FromSeconds(5)), t2.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, received1.Count);
        Assert.Equal(2, received2.Count);
    }

    // ------------------------------------------------------------------ //
    // AC-5: Subscribe cleans up on cancellation (no memory leak)
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Subscribe_CleansUpSubscriber_OnCancellation()
    {
        var buffer = new LogBuffer();

        using var cts = new CancellationTokenSource();

        // Start a subscriber then cancel immediately
        var subscribeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in buffer.Subscribe("flow", cts.Token))
                {
                    // consume
                }
            }
            catch (OperationCanceledException) { /* expected */ }
        });

        await Task.Delay(30);
        cts.Cancel();
        await subscribeTask.WaitAsync(TimeSpan.FromSeconds(5));

        // After cancellation, adding to the buffer should not throw
        // (subscriber channel is cleaned up)
        buffer.Add(MakeEntry("flow", "after cancel"));

        // Verify the entry is still stored in the ring buffer
        var logs = buffer.GetLogs("flow");
        Assert.Single(logs);
    }

    // ------------------------------------------------------------------ //
    // AC-6: BufferedLogger correctly creates LogEntry records
    // ------------------------------------------------------------------ //

    [Fact]
    public void BufferedLogger_CreatesLogEntry_WithCorrectFields()
    {
        var buffer = new LogBuffer();
        var provider = new BufferedLogProvider(buffer, "test-workflow");
        var logger = provider.CreateLogger("SomeCategory");

        var before = DateTimeOffset.UtcNow;
        logger.LogInformation("Test message {Value}", 42);
        var after = DateTimeOffset.UtcNow;

        var logs = buffer.GetLogs("test-workflow");

        Assert.Single(logs);
        var entry = logs[0];
        Assert.Equal("test-workflow", entry.WorkflowName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("Test message", entry.Message);
        Assert.InRange(entry.Timestamp, before, after);
    }

    [Fact]
    public void BufferedLogger_AppendsException_ToMessage()
    {
        var buffer = new LogBuffer();
        var provider = new BufferedLogProvider(buffer, "flow");
        var logger = provider.CreateLogger("Cat");

        var ex = new InvalidOperationException("test error");
        logger.LogError(ex, "Something failed");

        var logs = buffer.GetLogs("flow");
        Assert.Single(logs);
        Assert.Contains("Something failed", logs[0].Message);
        Assert.Contains("test error", logs[0].Message);
    }

    [Fact]
    public void BufferedLogger_SkipsNoneLevel()
    {
        var buffer = new LogBuffer();
        var provider = new BufferedLogProvider(buffer, "flow");
        var logger = (BufferedLogger)provider.CreateLogger("Cat");

        Assert.False(logger.IsEnabled(LogLevel.None));
        // Calling Log with None level: no entry should be added
        logger.Log(LogLevel.None, new EventId(), "state", null, (s, _) => s);

        Assert.Empty(buffer.GetLogs("flow"));
    }

    [Fact]
    public void BufferedLogger_SkipsEmptyMessage()
    {
        var buffer = new LogBuffer();
        var provider = new BufferedLogProvider(buffer, "flow");
        var logger = provider.CreateLogger("Cat");

        // Log with empty formatter output
        ((ILogger)logger).Log(LogLevel.Information, new EventId(), "state", null, (_, _) => string.Empty);

        Assert.Empty(buffer.GetLogs("flow"));
    }

    // ------------------------------------------------------------------ //
    // AC-7: LogBuffer separates entries per workflow
    // ------------------------------------------------------------------ //

    [Fact]
    public void LogBuffer_StoresSeparateQueues_PerWorkflow()
    {
        var buffer = new LogBuffer();

        buffer.Add(MakeEntry("flow-a", "msg-a"));
        buffer.Add(MakeEntry("flow-b", "msg-b-1"));
        buffer.Add(MakeEntry("flow-b", "msg-b-2"));

        var logsA = buffer.GetLogs("flow-a");
        var logsB = buffer.GetLogs("flow-b");

        Assert.Single(logsA);
        Assert.Equal(2, logsB.Count);
        Assert.Equal("msg-a", logsA[0].Message);
    }
}

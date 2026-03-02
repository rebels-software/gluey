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

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Gluey.Runtime.Logging;

/// <summary>
/// A single log entry captured from a workflow.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string WorkflowName,
    LogLevel Level,
    string Message);

/// <summary>
/// Thread-safe in-memory ring buffer that stores recent log entries per workflow.
/// Supports both snapshot retrieval and live streaming via Channel&lt;T&gt;.
/// </summary>
public sealed class LogBuffer
{
    /// <summary>
    /// Maximum number of log entries retained per workflow.
    /// </summary>
    private const int MaxEntriesPerWorkflow = 1000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<LogEntry>> _logs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<Channel<LogEntry>>> _subscribers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a log entry to the buffer and notifies all active subscribers for that workflow.
    /// Trims the queue to <see cref="MaxEntriesPerWorkflow"/> if it exceeds the limit.
    /// </summary>
    /// <param name="entry">The log entry to add.</param>
    public void Add(LogEntry entry)
    {
        var queue = _logs.GetOrAdd(entry.WorkflowName, _ => new ConcurrentQueue<LogEntry>());
        queue.Enqueue(entry);

        // Trim to max size
        while (queue.Count > MaxEntriesPerWorkflow)
        {
            queue.TryDequeue(out _);
        }

        // Notify subscribers
        if (_subscribers.TryGetValue(entry.WorkflowName, out var channels))
        {
            foreach (var channel in channels)
            {
                // TryWrite is non-blocking; if the channel is full or completed, skip
                channel.Writer.TryWrite(entry);
            }
        }
    }

    /// <summary>
    /// Returns the most recent log entries for a workflow.
    /// </summary>
    /// <param name="workflowName">The workflow name to retrieve logs for.</param>
    /// <param name="count">Maximum number of entries to return. Defaults to 100.</param>
    /// <returns>A list of recent log entries, oldest first.</returns>
    public IReadOnlyList<LogEntry> GetLogs(string workflowName, int count = 100)
    {
        if (!_logs.TryGetValue(workflowName, out var queue))
        {
            return [];
        }

        var snapshot = queue.ToArray();
        var skip = Math.Max(0, snapshot.Length - count);
        return snapshot.Skip(skip).ToList();
    }

    /// <summary>
    /// Subscribes to live log entries for a workflow. Returns an async enumerable
    /// that yields entries as they arrive. The subscription is cancelled when
    /// the provided cancellation token is triggered.
    /// </summary>
    /// <param name="workflowName">The workflow name to subscribe to.</param>
    /// <param name="cancellationToken">Token to cancel the subscription.</param>
    /// <returns>An async enumerable of log entries.</returns>
    public async IAsyncEnumerable<LogEntry> Subscribe(
        string workflowName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var channels = _subscribers.GetOrAdd(workflowName, _ => new ConcurrentBag<Channel<LogEntry>>());
        channels.Add(channel);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            // Remove the channel from subscribers
            // ConcurrentBag doesn't have Remove, so we replace the bag without this channel
            if (_subscribers.TryGetValue(workflowName, out var currentChannels))
            {
                var remaining = new ConcurrentBag<Channel<LogEntry>>(
                    currentChannels.Where(c => c != channel));
                _subscribers.TryUpdate(workflowName, remaining, currentChannels);
            }

            channel.Writer.TryComplete();
        }
    }
}

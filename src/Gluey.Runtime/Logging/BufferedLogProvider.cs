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

using Microsoft.Extensions.Logging;

namespace Gluey.Runtime.Logging;

/// <summary>
/// A logging provider that writes log entries to a <see cref="LogBuffer"/>.
/// Each provider instance is associated with a specific workflow name.
/// </summary>
public sealed class BufferedLogProvider : ILoggerProvider
{
    private readonly LogBuffer _logBuffer;
    private readonly string _workflowName;

    /// <summary>
    /// Creates a new BufferedLogProvider.
    /// </summary>
    /// <param name="logBuffer">The shared log buffer to write entries to.</param>
    /// <param name="workflowName">The workflow name to tag log entries with.</param>
    public BufferedLogProvider(LogBuffer logBuffer, string workflowName)
    {
        _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
        _workflowName = workflowName ?? throw new ArgumentNullException(nameof(workflowName));
    }

    /// <summary>
    /// Creates a new <see cref="BufferedLogger"/> for the given category.
    /// </summary>
    /// <param name="categoryName">The logger category name.</param>
    /// <returns>A new logger instance.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return new BufferedLogger(_logBuffer, _workflowName);
    }

    /// <summary>
    /// Disposes the provider. No-op since the LogBuffer is shared and managed externally.
    /// </summary>
    public void Dispose()
    {
        // LogBuffer is a shared singleton — nothing to dispose here
    }
}

/// <summary>
/// A logger that writes entries to a <see cref="LogBuffer"/> for in-memory capture.
/// </summary>
public sealed class BufferedLogger : ILogger
{
    private readonly LogBuffer _logBuffer;
    private readonly string _workflowName;

    /// <summary>
    /// Creates a new BufferedLogger.
    /// </summary>
    /// <param name="logBuffer">The shared log buffer.</param>
    /// <param name="workflowName">The workflow name to tag entries with.</param>
    public BufferedLogger(LogBuffer logBuffer, string workflowName)
    {
        _logBuffer = logBuffer;
        _workflowName = workflowName;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message))
            return;

        if (exception is not null)
        {
            message = $"{message} {exception}";
        }

        var entry = new LogEntry(
            DateTimeOffset.UtcNow,
            _workflowName,
            logLevel,
            message);

        _logBuffer.Add(entry);
    }
}

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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Gluey.Runtime.Logging;

/// <summary>
/// Custom console formatter that outputs clean Docker-style logs.
/// Format: YYYY-MM-DD HH:mm:ss | workflow-name | message
/// </summary>
public sealed class GlueyLogFormatter : ConsoleFormatter
{
    /// <summary>
    /// The registered name of this formatter.
    /// </summary>
    public const string FormatterName = "gluey";

    private readonly IOptionsMonitor<GlueyLogFormatterOptions> _options;

    public GlueyLogFormatter(IOptionsMonitor<GlueyLogFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
            return;

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var workflowName = _options.CurrentValue.WorkflowName ?? "gluey";

        textWriter.WriteLine($"{timestamp} | {workflowName} | {message}");

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine($"{timestamp} | {workflowName} | {logEntry.Exception}");
        }
    }
}

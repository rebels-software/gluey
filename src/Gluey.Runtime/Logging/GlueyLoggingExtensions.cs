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
using Microsoft.Extensions.Logging.Console;

namespace Gluey.Runtime.Logging;

/// <summary>
/// Extension methods for registering the Gluey log formatter.
/// </summary>
public static class GlueyLoggingExtensions
{
    /// <summary>
    /// Adds the Gluey console formatter with clean Docker-style log output.
    /// By default, suppresses Microsoft.* and System.* logs (Warning+ only) and allows
    /// all Gluey.* logs at Information level. When verbose is true, all logs at
    /// Information level and above are shown.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="workflowName">Optional workflow name to display in logs. Defaults to "gluey".</param>
    /// <param name="verbose">When true, show full .NET framework logs at Information level.</param>
    /// <param name="logBuffer">Optional LogBuffer to capture logs in memory for API retrieval.</param>
    /// <returns>The logging builder for chaining.</returns>
    public static ILoggingBuilder AddGlueyConsole(this ILoggingBuilder builder, string? workflowName = null, bool verbose = false, LogBuffer? logBuffer = null)
    {
        builder.ClearProviders();

        builder.AddConsole(options =>
        {
            options.FormatterName = GlueyLogFormatter.FormatterName;
        });

        builder.AddConsoleFormatter<GlueyLogFormatter, GlueyLogFormatterOptions>(options =>
        {
            if (workflowName is not null)
            {
                options.WorkflowName = workflowName;
            }
        });

        // Add buffered log provider for in-memory capture (used by daemon API)
        if (logBuffer is not null && workflowName is not null)
        {
            builder.AddProvider(new BufferedLogProvider(logBuffer, workflowName));
        }

        if (verbose)
        {
            // Show all logs at Information level and above
            builder.SetMinimumLevel(LogLevel.Information);
        }
        else
        {
            // Default minimum is Warning — suppresses all framework logs
            builder.SetMinimumLevel(LogLevel.Warning);

            // Only Gluey.* loggers get through at Information level
            builder.AddFilter("Gluey", LogLevel.Information);
        }

        return builder;
    }
}

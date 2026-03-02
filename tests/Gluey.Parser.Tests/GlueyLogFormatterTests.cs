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

using System.Text.RegularExpressions;
using Gluey.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for GlueyLogFormatter output format, verbose/non-verbose mode, and edge cases.
/// </summary>
public class GlueyLogFormatterTests
{
    // ------------------------------------------------------------------ //
    // Helper: create a GlueyLogFormatter backed by simple options
    // ------------------------------------------------------------------ //

    private static GlueyLogFormatter CreateFormatter(string workflowName = "gluey")
    {
        var options = new GlueyLogFormatterOptions { WorkflowName = workflowName };
        var monitor = new TestOptionsMonitor<GlueyLogFormatterOptions>(options);
        return new GlueyLogFormatter(monitor);
    }

    private static LogEntry<string> MakeLogEntry(
        string message,
        LogLevel level = LogLevel.Information,
        Exception? exception = null,
        Func<string, Exception?, string>? formatter = null)
    {
        formatter ??= (state, _) => state;
        return new LogEntry<string>(level, "TestCategory", new EventId(0), message, exception, formatter);
    }

    // ------------------------------------------------------------------ //
    // AC-1: GlueyLogFormatter produces correct format
    // ------------------------------------------------------------------ //

    [Fact]
    public void Write_ProducesCorrectFormat()
    {
        var fmt = CreateFormatter("my-workflow");
        using var sw = new StringWriter();

        var entry = MakeLogEntry("Hello world");
        fmt.Write(in entry, null, sw);

        var output = sw.ToString();

        // Format: YYYY-MM-DD HH:mm:ss | workflow-name | message\n
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \| my-workflow \| Hello world\n$", output);
    }

    // ------------------------------------------------------------------ //
    // AC-2: Workflow name appears in log output
    // ------------------------------------------------------------------ //

    [Fact]
    public void Write_WorkflowNameAppearsInOutput()
    {
        var fmt = CreateFormatter("sensor-pipeline");
        using var sw = new StringWriter();

        var entry = MakeLogEntry("test message");
        fmt.Write(in entry, null, sw);

        var output = sw.ToString();

        Assert.Contains("| sensor-pipeline |", output);
    }

    // ------------------------------------------------------------------ //
    // AC-3: Timestamp format is correct
    // ------------------------------------------------------------------ //

    [Fact]
    public void Write_TimestampFormatIsCorrect()
    {
        var fmt = CreateFormatter();
        using var sw = new StringWriter();

        var entry = MakeLogEntry("test");
        fmt.Write(in entry, null, sw);

        var output = sw.ToString();

        // Extract the timestamp portion (everything before the first " | ")
        var timestampStr = output.Split(" | ")[0];

        // Verify it parses as a valid datetime in yyyy-MM-dd HH:mm:ss format
        Assert.True(
            DateTimeOffset.TryParseExact(
                timestampStr,
                "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _),
            $"Timestamp '{timestampStr}' does not match yyyy-MM-dd HH:mm:ss format");
    }

    // ------------------------------------------------------------------ //
    // AC-4: Verbose mode includes all log levels
    // ------------------------------------------------------------------ //

    [Fact]
    public void AddGlueyConsole_VerboseTrue_FrameworkLoggerIsEnabledAtInformation()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddGlueyConsole(verbose: true));

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILoggerFactory>();

        var frameworkLogger = factory.CreateLogger("Microsoft.AspNetCore");

        Assert.True(frameworkLogger.IsEnabled(LogLevel.Information));
    }

    // ------------------------------------------------------------------ //
    // AC-5: Non-verbose mode suppresses framework logs
    // ------------------------------------------------------------------ //

    [Fact]
    public void AddGlueyConsole_VerboseFalse_FrameworkLoggerSuppressedButGlueyEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddGlueyConsole(verbose: false));

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILoggerFactory>();

        var frameworkLogger = factory.CreateLogger("Microsoft.AspNetCore");
        var glueyLogger = factory.CreateLogger("Gluey.Runtime.SomeService");

        Assert.False(frameworkLogger.IsEnabled(LogLevel.Information));
        Assert.True(glueyLogger.IsEnabled(LogLevel.Information));
    }

    // ------------------------------------------------------------------ //
    // AC-6: Exception output writes a second line
    // ------------------------------------------------------------------ //

    [Fact]
    public void Write_WithException_WritesSecondLine()
    {
        var fmt = CreateFormatter("my-flow");
        using var sw = new StringWriter();

        var ex = new InvalidOperationException("something broke");
        var entry = MakeLogEntry("Operation failed", exception: ex);
        fmt.Write(in entry, null, sw);

        var output = sw.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("Operation failed", lines[0]);
        Assert.Contains("something broke", lines[1]);
    }

    // ------------------------------------------------------------------ //
    // AC-7: Null message is skipped
    // ------------------------------------------------------------------ //

    [Fact]
    public void Write_NullMessage_SkipsOutput()
    {
        var fmt = CreateFormatter();
        using var sw = new StringWriter();

        // Formatter returns null => Write should produce no output
        var entry = new LogEntry<string>(
            LogLevel.Information,
            "TestCategory",
            new EventId(0),
            "state",
            null,
            (_, _) => null!);

        fmt.Write(in entry, null, sw);

        Assert.Empty(sw.ToString());
    }

    // ------------------------------------------------------------------ //
    // Helper: minimal IOptionsMonitor<T> for unit tests
    // ------------------------------------------------------------------ //

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

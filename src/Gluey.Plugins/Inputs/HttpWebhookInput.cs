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
using System.Threading.Channels;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gluey.Plugins.Inputs;

/// <summary>
/// HTTP webhook input plugin that listens for POST requests and emits messages.
/// Supports application/json and application/octet-stream content types.
/// </summary>
public sealed class HttpWebhookInput : IInputPlugin
{
    private readonly Channel<Message> _channel;
    private readonly CancellationTokenSource _cts = new();
    private WebApplication? _app;
    private Task? _runTask;

    public string Type => "http";

    public HttpWebhookInput()
    {
        // Unbounded channel for message buffering
        _channel = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Initializes the HTTP webhook listener with configuration.
    /// </summary>
    /// <param name="config">Configuration containing 'port' (default: 8080) and 'path' (default: /webhook).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Extract port (default: 8080)
        int port = 8080;
        if (config.TryGetValue("port", out var portElement))
        {
            port = portElement.ValueKind == JsonValueKind.Number ? portElement.GetInt32() : 8080;
        }

        // Extract path (default: /webhook)
        string path = "/webhook";
        if (config.TryGetValue("path", out var pathElement))
        {
            path = pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString() ?? "/webhook" : "/webhook";
        }

        // Build Kestrel web application with custom URL
        var args = new[] { $"--urls=http://0.0.0.0:{port}" };
        var builder = WebApplication.CreateSlimBuilder(args);

        _app = builder.Build();

        // Define POST endpoint
        _app.MapPost(path, async (HttpContext context) =>
        {
            try
            {
                var contentType = context.Request.ContentType ?? "";
                JsonDocument payload;
                var metadata = new Dictionary<string, string>
                {
                    ["source"] = "http",
                    ["content_type"] = contentType,
                    ["path"] = context.Request.Path.ToString()
                };

                // Handle JSON content type
                if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    payload = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                }
                // Handle binary content type
                else if (contentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    // Read raw bytes and store in metadata
                    using var ms = new MemoryStream();
                    await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
                    var bytes = ms.ToArray();

                    metadata["raw_bytes"] = Convert.ToBase64String(bytes);
                    metadata["byte_length"] = bytes.Length.ToString();

                    // Create empty JSON payload (binary data is in metadata)
                    payload = JsonDocument.Parse("{}");
                }
                else
                {
                    // Unknown content type - treat as text/plain and wrap in JSON
                    using var reader = new StreamReader(context.Request.Body);
                    var body = await reader.ReadToEndAsync(context.RequestAborted);

                    var jsonObject = new { data = body };
                    payload = JsonDocument.Parse(JsonSerializer.Serialize(jsonObject));
                }

                // Create and enqueue message
                var message = Message.Create(payload, metadata);
                await _channel.Writer.WriteAsync(message, context.RequestAborted);

                // Return 200 OK
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("OK", context.RequestAborted);
            }
            catch (Exception)
            {
                // Return 500 on error
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal Server Error", context.RequestAborted);
            }
        });

        // Start the web application in the background
        _runTask = Task.Run(async () =>
        {
            try
            {
                await _app.RunAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception)
            {
                // Server stopped with error (ignore during shutdown)
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads messages from the HTTP webhook channel as an async enumerable.
    /// </summary>
    public async IAsyncEnumerable<Message> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Disposes the HTTP webhook server and completes the channel.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Signal cancellation to the web application
        await _cts.CancelAsync();

        // Complete the channel writer
        _channel.Writer.Complete();

        // Stop the web application
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        // Wait for the run task to complete
        if (_runTask != null)
        {
            try
            {
                await _runTask;
            }
            catch
            {
                // Ignore exceptions during shutdown
            }
        }

        _cts.Dispose();
    }
}

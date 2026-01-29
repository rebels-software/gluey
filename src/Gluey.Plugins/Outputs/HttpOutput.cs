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

using System.Net.Http.Json;
using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;

namespace Gluey.Plugins.Outputs;

/// <summary>
/// HTTP output plugin that POSTs messages as JSON to a configured URL.
/// Fails fast on HTTP errors (4xx/5xx) by throwing an exception.
/// </summary>
public sealed class HttpOutput : IOutputPlugin
{
    private readonly HttpClient _httpClient = new();
    private string _url = "";
    private bool _disposed;

    public string Type => "http";

    /// <summary>
    /// Initializes the HTTP output with configuration.
    /// </summary>
    /// <param name="config">Configuration containing url (required).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Extract URL (required) - check 'url' first, then 'target' (used by parser for parenthesized args)
        if (config.TryGetValue("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
        {
            _url = urlElement.GetString() ?? "";
        }
        else if (config.TryGetValue("target", out var targetElement) && targetElement.ValueKind == JsonValueKind.String)
        {
            _url = targetElement.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(_url))
        {
            throw new InvalidOperationException("HTTP output URL is required. Use 'url' config or http(\"url\") syntax.");
        }

        // Validate URL format
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new InvalidOperationException($"Invalid HTTP URL: '{_url}'. URL must be an absolute HTTP or HTTPS URL.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a message to the HTTP endpoint by POSTing it as JSON.
    /// Throws HttpRequestException on HTTP errors (4xx/5xx).
    /// </summary>
    public async Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        // POST the message payload as JSON with Content-Type: application/json
        var response = await _httpClient.PostAsJsonAsync(_url, message.Payload, cancellationToken);

        // Fail fast on HTTP errors (4xx/5xx)
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Disposes the HTTP output plugin and its HttpClient.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}

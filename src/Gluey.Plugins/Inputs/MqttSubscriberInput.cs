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

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace Gluey.Plugins.Inputs;

/// <summary>
/// MQTT subscriber input plugin that connects to an MQTT broker and subscribes to topics.
/// Supports TLS connections via mqtts:// scheme or tls: true config.
/// </summary>
public sealed class MqttSubscriberInput : IInputPlugin
{
    private readonly Channel<Message> _channel;
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _connectOptions;
    private MqttClientSubscribeOptions? _subscribeOptions;
    private bool _disposing;
    private bool _disposed;
    private int _reconnectAttempts;

    public string Type => "mqtt";

    public MqttSubscriberInput()
    {
        // Unbounded channel for message buffering
        _channel = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Initializes the MQTT subscriber with configuration.
    /// </summary>
    /// <param name="config">Configuration containing broker URL, topics, QoS, and TLS settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Extract broker URL (required)
        string brokerUrl = "";
        if (config.TryGetValue("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
        {
            brokerUrl = urlElement.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(brokerUrl))
        {
            throw new InvalidOperationException("MQTT broker URL is required. Use 'url' config or from mqtt(\"url\") syntax.");
        }

        // Parse broker URL
        var uri = new Uri(brokerUrl);
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : (uri.Scheme == "mqtts" ? 8883 : 1883);
        var useTls = uri.Scheme == "mqtts";

        // Check explicit TLS config
        if (config.TryGetValue("tls", out var tlsElement))
        {
            if (tlsElement.ValueKind == JsonValueKind.True)
            {
                useTls = true;
            }
            else if (tlsElement.ValueKind == JsonValueKind.False)
            {
                useTls = false;
            }
        }

        // Extract topics (required)
        var topics = new List<string>();
        if (config.TryGetValue("topics", out var topicsElement))
        {
            if (topicsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var topicElement in topicsElement.EnumerateArray())
                {
                    if (topicElement.ValueKind == JsonValueKind.String)
                    {
                        var topic = topicElement.GetString();
                        if (!string.IsNullOrEmpty(topic))
                        {
                            topics.Add(topic);
                        }
                    }
                }
            }
            else if (topicsElement.ValueKind == JsonValueKind.String)
            {
                var topic = topicsElement.GetString();
                if (!string.IsNullOrEmpty(topic))
                {
                    topics.Add(topic);
                }
            }
        }

        // Also check 'topic' singular
        if (config.TryGetValue("topic", out var singleTopicElement) && singleTopicElement.ValueKind == JsonValueKind.String)
        {
            var topic = singleTopicElement.GetString();
            if (!string.IsNullOrEmpty(topic) && !topics.Contains(topic))
            {
                topics.Add(topic);
            }
        }

        if (topics.Count == 0)
        {
            throw new InvalidOperationException("At least one MQTT topic is required. Use 'topics' or 'topic' config.");
        }

        // Extract QoS (default: 1)
        MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce;
        if (config.TryGetValue("qos", out var qosElement) && qosElement.ValueKind == JsonValueKind.Number)
        {
            var qosValue = qosElement.GetInt32();
            qos = qosValue switch
            {
                0 => MqttQualityOfServiceLevel.AtMostOnce,
                1 => MqttQualityOfServiceLevel.AtLeastOnce,
                2 => MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MqttQualityOfServiceLevel.AtLeastOnce
            };
        }

        // Extract client ID (optional)
        string clientId = $"gluey-{Guid.NewGuid():N}";
        if (config.TryGetValue("client_id", out var clientIdElement) && clientIdElement.ValueKind == JsonValueKind.String)
        {
            var customClientId = clientIdElement.GetString();
            if (!string.IsNullOrEmpty(customClientId))
            {
                clientId = customClientId;
            }
        }

        // Extract username (optional)
        string? username = null;
        if (config.TryGetValue("username", out var usernameElement) && usernameElement.ValueKind == JsonValueKind.String)
        {
            username = usernameElement.GetString();
        }

        // Extract password (optional)
        string? password = null;
        if (config.TryGetValue("password", out var passwordElement) && passwordElement.ValueKind == JsonValueKind.String)
        {
            password = passwordElement.GetString();
        }

        // Create MQTT client
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        // Build connection options
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId(clientId)
            .WithCleanSession(true);

        // Add TLS if required
        if (useTls)
        {
            optionsBuilder.WithTlsOptions(o =>
            {
                o.UseTls(true);
                // Allow self-signed certificates for development
                o.WithCertificateValidationHandler(_ => true);
            });
        }

        // Add authentication if provided
        if (!string.IsNullOrEmpty(username))
        {
            optionsBuilder.WithCredentials(username, password);
        }

        var options = optionsBuilder.Build();
        _connectOptions = options;

        // Set up disconnect handler for auto-reconnect
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        // Set up message handler before connecting
        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var payload = e.ApplicationMessage.Payload;
                var topic = e.ApplicationMessage.Topic;

                // Create metadata
                var metadata = new Dictionary<string, string>
                {
                    ["source"] = "mqtt",
                    ["topic"] = topic,
                    ["qos"] = ((int)e.ApplicationMessage.QualityOfServiceLevel).ToString(),
                    ["retain"] = e.ApplicationMessage.Retain.ToString().ToLowerInvariant()
                };

                JsonDocument jsonPayload;

                // Try to parse as JSON first
                if (!payload.IsEmpty)
                {
                    // Convert ReadOnlySequence<byte> to byte array for processing
                    var payloadBytes = payload.IsSingleSegment
                        ? payload.First.ToArray()
                        : payload.ToArray();

                    try
                    {
                        jsonPayload = JsonDocument.Parse(payloadBytes);
                    }
                    catch (JsonException)
                    {
                        // Not valid JSON - store as raw bytes in metadata
                        metadata["raw_bytes"] = Convert.ToBase64String(payloadBytes);
                        metadata["byte_length"] = payloadBytes.Length.ToString();
                        jsonPayload = JsonDocument.Parse("{}");
                    }
                }
                else
                {
                    // Empty payload
                    jsonPayload = JsonDocument.Parse("{}");
                }

                // Create and enqueue message
                var message = Message.Create(jsonPayload, metadata);
                await _channel.Writer.WriteAsync(message);
            }
            catch (Exception)
            {
                // Silently ignore message processing errors
            }
        };

        // Connect to broker
        Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Connecting to {host}:{port}");
        await _mqttClient.ConnectAsync(options, cancellationToken);
        Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Connected");

        // Subscribe to topics
        var subscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics)
        {
            subscribeOptionsBuilder.WithTopicFilter(topic, qos);
        }

        _subscribeOptions = subscribeOptionsBuilder.Build();
        await _mqttClient.SubscribeAsync(_subscribeOptions, cancellationToken);
    }

    /// <summary>
    /// Handles disconnection events by attempting to reconnect with exponential backoff.
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (_disposing) return;

        Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Disconnected from broker ({e.Reason})");

        while (!_disposing)
        {
            _reconnectAttempts++;
            var delay = Math.Min(1000 * (1 << Math.Min(_reconnectAttempts - 1, 4)), 30000);
            Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Reconnecting in {delay / 1000}s (attempt {_reconnectAttempts})");

            await Task.Delay(delay);
            if (_disposing) return;

            try
            {
                await _mqttClient!.ConnectAsync(_connectOptions!);

                // Re-subscribe to all topics (clean session loses subscriptions)
                if (_subscribeOptions != null)
                {
                    await _mqttClient.SubscribeAsync(_subscribeOptions);
                }

                _reconnectAttempts = 0;
                Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Reconnected successfully");
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Reconnect failed: {ex.Message}");
            }
        }

        Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] MQTT sub: Reconnect abandoned (disposing)");
    }

    /// <summary>
    /// Reads messages from the MQTT channel as an async enumerable.
    /// </summary>
    public async IAsyncEnumerable<Message> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Disposes the MQTT client and completes the channel.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposing = true;

        // Complete the channel writer
        _channel.Writer.Complete();

        // Disconnect and dispose MQTT client
        if (_mqttClient != null)
        {
            try
            {
                if (_mqttClient.IsConnected)
                {
                    await _mqttClient.DisconnectAsync();
                }
            }
            catch
            {
                // Ignore disconnection errors during shutdown
            }

            _mqttClient.Dispose();
        }
    }
}

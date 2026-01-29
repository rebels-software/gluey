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

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace Gluey.Plugins.Outputs;

/// <summary>
/// MQTT publisher output plugin that connects to an MQTT broker and publishes messages.
/// Supports TLS connections via mqtts:// scheme or tls: true config.
/// Supports topic interpolation with {{field}} syntax.
/// </summary>
public sealed class MqttPublisherOutput : IOutputPlugin
{
    private static readonly Regex InterpolationPattern = new(@"\{\{(\w+(?:\.\w+)*)\}\}", RegexOptions.Compiled);

    private IMqttClient? _mqttClient;
    private string _topic = "";
    private MqttQualityOfServiceLevel _qos = MqttQualityOfServiceLevel.AtLeastOnce;
    private bool _retain;
    private bool _disposed;

    public string Type => "mqtt";

    /// <summary>
    /// Initializes the MQTT publisher with configuration.
    /// </summary>
    /// <param name="config">Configuration containing broker URL, topic, QoS, and TLS settings.</param>
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
            throw new InvalidOperationException("MQTT broker URL is required. Use 'url' config or mqtt(\"url\") syntax.");
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

        // Extract topic (required)
        if (config.TryGetValue("topic", out var topicElement) && topicElement.ValueKind == JsonValueKind.String)
        {
            _topic = topicElement.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(_topic))
        {
            throw new InvalidOperationException("MQTT topic is required. Use 'topic' config.");
        }

        // Extract QoS (default: 1)
        if (config.TryGetValue("qos", out var qosElement) && qosElement.ValueKind == JsonValueKind.Number)
        {
            var qosValue = qosElement.GetInt32();
            _qos = qosValue switch
            {
                0 => MqttQualityOfServiceLevel.AtMostOnce,
                1 => MqttQualityOfServiceLevel.AtLeastOnce,
                2 => MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MqttQualityOfServiceLevel.AtLeastOnce
            };
        }

        // Extract retain flag (default: false)
        if (config.TryGetValue("retain", out var retainElement))
        {
            _retain = retainElement.ValueKind == JsonValueKind.True;
        }

        // Extract client ID (optional)
        string clientId = $"gluey-pub-{Guid.NewGuid():N}";
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

        // Connect to broker
        await _mqttClient.ConnectAsync(options, cancellationToken);
    }

    /// <summary>
    /// Writes a message to the MQTT topic.
    /// Supports topic interpolation with {{field}} syntax (e.g., "devices/{{device_id}}/data").
    /// </summary>
    public async Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        // Interpolate topic with message fields
        var resolvedTopic = InterpolateTopic(_topic, message);

        // Serialize payload to JSON bytes
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message.Payload));

        // Build and publish message
        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(resolvedTopic)
            .WithPayload(payloadBytes)
            .WithQualityOfServiceLevel(_qos)
            .WithRetainFlag(_retain)
            .Build();

        await _mqttClient.PublishAsync(mqttMessage, cancellationToken);
    }

    /// <summary>
    /// Interpolates {{field}} patterns in the topic with values from the message payload.
    /// Supports nested field access (e.g., {{device.id}}).
    /// </summary>
    private static string InterpolateTopic(string topic, Message message)
    {
        return InterpolationPattern.Replace(topic, match =>
        {
            var fieldPath = match.Groups[1].Value;
            var value = GetFieldValue(message.Payload.RootElement, fieldPath);
            return value ?? match.Value; // Keep original if field not found
        });
    }

    /// <summary>
    /// Gets a field value from a JsonElement, supporting nested paths (e.g., "device.location.id").
    /// </summary>
    private static string? GetFieldValue(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var next))
            {
                return null;
            }
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => current.GetRawText()
        };
    }

    /// <summary>
    /// Disposes the MQTT client and disconnects from the broker.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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

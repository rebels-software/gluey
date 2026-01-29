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

using Gluey.Core.Abstractions;
using Gluey.Plugins.Inputs;
using Gluey.Plugins.Outputs;
using Gluey.Plugins.Transforms;

namespace Gluey.Runtime;

/// <summary>
/// Registry for plugin factories. Creates plugin instances by type name.
/// </summary>
public sealed class PluginRegistry
{
    private readonly Dictionary<string, Func<IInputPlugin>> _inputFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<ITransformPlugin>> _transformFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IOutputPlugin>> _outputFactories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new PluginRegistry with all built-in plugins registered.
    /// </summary>
    public PluginRegistry()
    {
        RegisterBuiltInPlugins();
    }

    /// <summary>
    /// Registers all built-in plugins at startup.
    /// </summary>
    private void RegisterBuiltInPlugins()
    {
        // Input plugins
        RegisterInput("http", () => new HttpWebhookInput());
        RegisterInput("mqtt", () => new MqttSubscriberInput());

        // Transform plugins
        RegisterTransform("json.parse", () => new JsonParseTransform());
        RegisterTransform("filter", () => new FilterTransform());
        RegisterTransform("transform", () => new TransformTransform());
        RegisterTransform("decode.binary", () => new BinaryDecodeTransform());
        RegisterTransform("decode.base64", () => new Base64DecodeTransform());
        RegisterTransform("decode.hex", () => new HexDecodeTransform());
        RegisterTransform("route", () => new RouteTransform());

        // Output plugins
        RegisterOutput("console", () => new ConsoleOutput());
        // RegisterOutput("http", () => new HttpOutput());
        RegisterOutput("mqtt", () => new MqttPublisherOutput());
        // RegisterOutput("sql", () => new SqlOutput());
    }

    /// <summary>
    /// Registers an input plugin factory.
    /// </summary>
    /// <param name="type">The plugin type identifier (e.g., "http", "mqtt").</param>
    /// <param name="factory">Factory function that creates a new plugin instance.</param>
    public void RegisterInput(string type, Func<IInputPlugin> factory)
    {
        _inputFactories[type] = factory;
    }

    /// <summary>
    /// Registers a transform plugin factory.
    /// </summary>
    /// <param name="type">The plugin type identifier (e.g., "json.parse", "filter").</param>
    /// <param name="factory">Factory function that creates a new plugin instance.</param>
    public void RegisterTransform(string type, Func<ITransformPlugin> factory)
    {
        _transformFactories[type] = factory;
    }

    /// <summary>
    /// Registers an output plugin factory.
    /// </summary>
    /// <param name="type">The plugin type identifier (e.g., "console", "http").</param>
    /// <param name="factory">Factory function that creates a new plugin instance.</param>
    public void RegisterOutput(string type, Func<IOutputPlugin> factory)
    {
        _outputFactories[type] = factory;
    }

    /// <summary>
    /// Creates an input plugin instance by type name.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>A new instance of the input plugin.</returns>
    /// <exception cref="ArgumentException">Thrown when the plugin type is not registered.</exception>
    public IInputPlugin CreateInput(string type)
    {
        if (!_inputFactories.TryGetValue(type, out var factory))
        {
            var available = _inputFactories.Keys.Any()
                ? string.Join(", ", _inputFactories.Keys.OrderBy(k => k))
                : "(none registered)";
            throw new ArgumentException(
                $"Unknown input plugin type: '{type}'. Available input plugins: {available}",
                nameof(type));
        }
        return factory();
    }

    /// <summary>
    /// Creates a transform plugin instance by type name.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>A new instance of the transform plugin.</returns>
    /// <exception cref="ArgumentException">Thrown when the plugin type is not registered.</exception>
    public ITransformPlugin CreateTransform(string type)
    {
        if (!_transformFactories.TryGetValue(type, out var factory))
        {
            var available = _transformFactories.Keys.Any()
                ? string.Join(", ", _transformFactories.Keys.OrderBy(k => k))
                : "(none registered)";
            throw new ArgumentException(
                $"Unknown transform plugin type: '{type}'. Available transform plugins: {available}",
                nameof(type));
        }
        return factory();
    }

    /// <summary>
    /// Creates an output plugin instance by type name.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>A new instance of the output plugin.</returns>
    /// <exception cref="ArgumentException">Thrown when the plugin type is not registered.</exception>
    public IOutputPlugin CreateOutput(string type)
    {
        if (!_outputFactories.TryGetValue(type, out var factory))
        {
            var available = _outputFactories.Keys.Any()
                ? string.Join(", ", _outputFactories.Keys.OrderBy(k => k))
                : "(none registered)";
            throw new ArgumentException(
                $"Unknown output plugin type: '{type}'. Available output plugins: {available}",
                nameof(type));
        }
        return factory();
    }

    /// <summary>
    /// Gets all registered input plugin types.
    /// </summary>
    public IEnumerable<string> GetInputTypes() => _inputFactories.Keys;

    /// <summary>
    /// Gets all registered transform plugin types.
    /// </summary>
    public IEnumerable<string> GetTransformTypes() => _transformFactories.Keys;

    /// <summary>
    /// Gets all registered output plugin types.
    /// </summary>
    public IEnumerable<string> GetOutputTypes() => _outputFactories.Keys;

    /// <summary>
    /// Checks if a plugin type is registered as an output plugin.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>True if the type is registered as an output plugin, false otherwise.</returns>
    public bool IsOutputPlugin(string type) => _outputFactories.ContainsKey(type);

    /// <summary>
    /// Checks if a plugin type is registered as a transform plugin.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>True if the type is registered as a transform plugin, false otherwise.</returns>
    public bool IsTransformPlugin(string type) => _transformFactories.ContainsKey(type);

    /// <summary>
    /// Checks if a plugin type is registered as an input plugin.
    /// </summary>
    /// <param name="type">The plugin type identifier.</param>
    /// <returns>True if the type is registered as an input plugin, false otherwise.</returns>
    public bool IsInputPlugin(string type) => _inputFactories.ContainsKey(type);
}

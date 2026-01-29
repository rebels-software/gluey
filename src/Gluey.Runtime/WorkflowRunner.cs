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

namespace Gluey.Runtime;

/// <summary>
/// Executes the workflow pipeline: reads from input, applies transforms, writes to output.
/// This is a stub implementation; full implementation will be added in US-011.
/// </summary>
public sealed class WorkflowRunner
{
    private readonly IInputPlugin _input;
    private readonly IReadOnlyList<ITransformPlugin> _transforms;
    private readonly IOutputPlugin _output;

    /// <summary>
    /// Creates a new WorkflowRunner.
    /// </summary>
    /// <param name="input">The input plugin to read messages from.</param>
    /// <param name="transforms">The transform plugins to apply in sequence.</param>
    /// <param name="output">The output plugin to write messages to.</param>
    public WorkflowRunner(
        IInputPlugin input,
        IReadOnlyList<ITransformPlugin> transforms,
        IOutputPlugin output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Runs the workflow pipeline until cancellation is requested.
    /// Reads messages from input, applies transforms in sequence, and writes to output.
    /// </summary>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read messages from input plugin via IAsyncEnumerable
            await foreach (var message in _input.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Apply each transform in sequence
                var currentMessage = message;

                foreach (var transform in _transforms)
                {
                    // If transform returns null, message is dropped (filtered)
                    currentMessage = await transform.ProcessAsync(currentMessage, cancellationToken).ConfigureAwait(false);

                    if (currentMessage is null)
                    {
                        // Message was filtered - break out of transform loop
                        break;
                    }
                }

                // If message survived all transforms, send to output
                if (currentMessage is not null)
                {
                    await _output.WriteAsync(currentMessage, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested - graceful shutdown
        }
    }
}

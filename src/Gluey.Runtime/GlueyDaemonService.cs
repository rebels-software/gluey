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

using Gluey.Core.Models;
using Gluey.Runtime.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gluey.Runtime;

/// <summary>
/// Daemon service that hosts the HTTP API and WorkflowManager.
/// Extends BackgroundService to integrate with Microsoft.Extensions.Hosting.
/// Handles graceful startup (loading persisted state) and shutdown (saving state).
/// </summary>
public sealed class GlueyDaemonService : BackgroundService
{
    /// <summary>
    /// Default port for the daemon HTTP API.
    /// </summary>
    public const int DefaultPort = 6262;

    private readonly WorkflowManager _workflowManager;
    private readonly FileStateStore _stateStore;
    private readonly ILogger<GlueyDaemonService> _logger;
    private readonly int _port;
    private readonly bool _restartActiveWorkflows;
    private readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the WorkflowManager instance owned by this daemon.
    /// Used by DI to provide WorkflowManager to HTTP API endpoints.
    /// </summary>
    public WorkflowManager WorkflowManager => _workflowManager;

    /// <summary>
    /// Gets the FileStateStore instance owned by this daemon.
    /// </summary>
    public FileStateStore StateStore => _stateStore;

    /// <summary>
    /// Gets the port the daemon HTTP API is configured to listen on.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Creates a new GlueyDaemonService.
    /// </summary>
    /// <param name="workflowManager">The WorkflowManager instance to use.</param>
    /// <param name="stateStore">The state store for persisting workflow states.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    /// <param name="port">The port for the HTTP API. Defaults to 6262.</param>
    /// <param name="restartActiveWorkflows">
    /// Whether to automatically restart workflows that were Active before shutdown.
    /// Defaults to true.
    /// </param>
    public GlueyDaemonService(
        WorkflowManager workflowManager,
        FileStateStore stateStore,
        ILogger<GlueyDaemonService> logger,
        int port = DefaultPort,
        bool restartActiveWorkflows = true)
    {
        _workflowManager = workflowManager ?? throw new ArgumentNullException(nameof(workflowManager));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _port = port;
        _restartActiveWorkflows = restartActiveWorkflows;
    }

    /// <summary>
    /// Called when the daemon starts. Loads persisted workflow states and optionally restarts
    /// workflows that were Active before the previous shutdown.
    /// </summary>
    /// <param name="cancellationToken">Triggered when the host is stopping.</param>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Gluey daemon starting on port {Port}...", _port);

        try
        {
            // Load persisted workflow states on startup
            await LoadPersistedWorkflowsAsync(cancellationToken);

            // Save daemon config (port and pid) for CLI discovery
            await _stateStore.SaveDaemonConfigAsync(_port, Environment.ProcessId, cancellationToken);
            _logger.LogDebug("Saved daemon config: port={Port}, pid={Pid}", _port, Environment.ProcessId);

            _logger.LogInformation("Gluey daemon started. Loaded {Count} workflows.", _workflowManager.List().Count);

            // Keep the service running until cancellation
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown - will be handled by StopAsync
            _logger.LogDebug("Daemon ExecuteAsync cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in daemon service");
            throw;
        }
    }

    /// <summary>
    /// Called when the daemon is stopping. Saves all workflow states and stops running workflows.
    /// Handles SIGTERM/SIGINT gracefully.
    /// </summary>
    /// <param name="cancellationToken">Triggered when the shutdown timeout expires.</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Gluey daemon stopping...");

        try
        {
            // Save all workflow states before stopping
            await SaveAllWorkflowStatesAsync(cancellationToken);

            // Delete daemon config file since we're shutting down
            await _stateStore.DeleteDaemonConfigAsync(cancellationToken);
            _logger.LogDebug("Deleted daemon config file");

            // Dispose the WorkflowManager (stops all running workflows)
            await _workflowManager.DisposeAsync();
            _logger.LogInformation("Gluey daemon stopped gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during daemon shutdown");
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Loads all persisted workflow states from the state store.
    /// Re-loads each workflow into WorkflowManager and optionally restarts Active workflows.
    /// </summary>
    private async Task LoadPersistedWorkflowsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading persisted workflow states...");

        var persistedStates = await _stateStore.LoadAllWorkflowStatesAsync(cancellationToken);
        _logger.LogDebug("Found {Count} persisted workflow states", persistedStates.Count);

        var workflowsToRestart = new List<(Guid Id, string Name)>();

        foreach (var workflowInfo in persistedStates)
        {
            try
            {
                // Check if the .gflow file still exists
                if (!File.Exists(workflowInfo.FilePath))
                {
                    _logger.LogWarning(
                        "Workflow '{Name}' ({Id}) file not found at {FilePath}, removing from state store",
                        workflowInfo.Name, workflowInfo.Id, workflowInfo.FilePath);
                    await _stateStore.DeleteWorkflowStateAsync(workflowInfo.Id, cancellationToken);
                    continue;
                }

                // Re-load the workflow into WorkflowManager
                var id = await _workflowManager.LoadAsync(workflowInfo.FilePath, cancellationToken);
                _logger.LogDebug("Loaded workflow '{Name}' from {FilePath}", workflowInfo.Name, workflowInfo.FilePath);

                // Track if workflow was Active and should be restarted
                if (_restartActiveWorkflows && workflowInfo.Status == WorkflowStatus.Active)
                {
                    workflowsToRestart.Add((id, workflowInfo.Name));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to load workflow '{Name}' from {FilePath}",
                    workflowInfo.Name, workflowInfo.FilePath);
            }
        }

        // Restart workflows that were Active before shutdown
        if (workflowsToRestart.Count > 0)
        {
            _logger.LogInformation("Restarting {Count} previously active workflows...", workflowsToRestart.Count);

            foreach (var (id, name) in workflowsToRestart)
            {
                try
                {
                    await _workflowManager.StartAsync(id, cancellationToken);
                    _logger.LogInformation("Restarted workflow '{Name}' ({Id})", name, id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart workflow '{Name}' ({Id})", name, id);
                }
            }
        }
    }

    /// <summary>
    /// Saves all current workflow states to the state store.
    /// Called during graceful shutdown.
    /// </summary>
    private async Task SaveAllWorkflowStatesAsync(CancellationToken cancellationToken)
    {
        var workflows = _workflowManager.List();
        _logger.LogInformation("Saving {Count} workflow states...", workflows.Count);

        foreach (var workflowInfo in workflows)
        {
            try
            {
                await _stateStore.SaveWorkflowStateAsync(workflowInfo, cancellationToken);
                _logger.LogDebug("Saved state for workflow '{Name}' ({Id})", workflowInfo.Name, workflowInfo.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to save state for workflow '{Name}' ({Id})",
                    workflowInfo.Name, workflowInfo.Id);
            }
        }
    }
}

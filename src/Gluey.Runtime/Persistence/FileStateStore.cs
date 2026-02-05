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

using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;

namespace Gluey.Runtime.Persistence;

/// <summary>
/// File-based implementation of <see cref="IStateStore"/> that persists workflow state to disk.
/// Stores state in ~/.gluey/state/ directory (or $GLUEY_STATE_DIR if set).
/// </summary>
public sealed class FileStateStore : IStateStore
{
    private const string DaemonConfigFileName = "daemon.json";
    private const string WorkflowsFileName = "workflows.json";
    private const string StateDirectoryName = "state";
    private const string GlueyDirectoryName = ".gluey";

    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Creates a new FileStateStore using the default state directory.
    /// Uses $GLUEY_STATE_DIR environment variable if set, otherwise ~/.gluey/state/.
    /// </summary>
    public FileStateStore() : this(GetDefaultStateDirectory())
    {
    }

    /// <summary>
    /// Creates a new FileStateStore with a custom state directory.
    /// </summary>
    /// <param name="stateDirectory">The directory path for storing state files.</param>
    public FileStateStore(string stateDirectory)
    {
        _stateDirectory = stateDirectory ?? throw new ArgumentNullException(nameof(stateDirectory));
    }

    /// <summary>
    /// Gets the state directory path being used by this store.
    /// </summary>
    public string StateDirectory => _stateDirectory;

    /// <inheritdoc/>
    public async Task SaveWorkflowStateAsync(WorkflowInfo workflowInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowInfo);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStateDirectoryExists();

            // Save individual workflow state file
            var workflowFilePath = GetWorkflowFilePath(workflowInfo.Id);
            var json = JsonSerializer.Serialize(workflowInfo, JsonOptions);
            await File.WriteAllTextAsync(workflowFilePath, json, cancellationToken).ConfigureAwait(false);

            // Update workflows index
            await UpdateWorkflowIndexAsync(workflowInfo.Id, add: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowInfo?> LoadWorkflowStateAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        var workflowFilePath = GetWorkflowFilePath(workflowId);

        if (!File.Exists(workflowFilePath))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(workflowFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WorkflowInfo>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Corrupted file - return null
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowInfo>> LoadAllWorkflowStatesAsync(CancellationToken cancellationToken = default)
    {
        var workflowsFilePath = GetWorkflowsFilePath();

        if (!File.Exists(workflowsFilePath))
        {
            return [];
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workflowIds = await LoadWorkflowIndexAsync(cancellationToken).ConfigureAwait(false);
            var workflows = new List<WorkflowInfo>();

            foreach (var workflowId in workflowIds)
            {
                var workflowFilePath = GetWorkflowFilePath(workflowId);
                if (File.Exists(workflowFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(workflowFilePath, cancellationToken).ConfigureAwait(false);
                        var workflowInfo = JsonSerializer.Deserialize<WorkflowInfo>(json, JsonOptions);
                        if (workflowInfo != null)
                        {
                            workflows.Add(workflowInfo);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip corrupted files
                    }
                }
            }

            return workflows;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteWorkflowStateAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workflowFilePath = GetWorkflowFilePath(workflowId);

            if (!File.Exists(workflowFilePath))
            {
                return false;
            }

            File.Delete(workflowFilePath);

            // Update workflows index
            await UpdateWorkflowIndexAsync(workflowId, add: false, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Saves daemon configuration (port and pid) to daemon.json.
    /// </summary>
    /// <param name="port">The port the daemon is listening on.</param>
    /// <param name="pid">The process ID of the daemon.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task SaveDaemonConfigAsync(int port, int pid, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStateDirectoryExists();

            var daemonConfig = new DaemonConfig(port, pid);
            var json = JsonSerializer.Serialize(daemonConfig, JsonOptions);
            var daemonFilePath = GetDaemonConfigFilePath();
            await File.WriteAllTextAsync(daemonFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads daemon configuration from daemon.json.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A tuple of (port, pid) if found, or null if not found.</returns>
    public async Task<(int port, int pid)?> LoadDaemonConfigAsync(CancellationToken cancellationToken = default)
    {
        var daemonFilePath = GetDaemonConfigFilePath();

        if (!File.Exists(daemonFilePath))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(daemonFilePath, cancellationToken).ConfigureAwait(false);
            var daemonConfig = JsonSerializer.Deserialize<DaemonConfig>(json, JsonOptions);
            return daemonConfig != null ? (daemonConfig.Port, daemonConfig.Pid) : null;
        }
        catch (JsonException)
        {
            // Corrupted file - return null
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes the daemon configuration file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public Task DeleteDaemonConfigAsync(CancellationToken cancellationToken = default)
    {
        var daemonFilePath = GetDaemonConfigFilePath();

        if (File.Exists(daemonFilePath))
        {
            File.Delete(daemonFilePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether the daemon is alive, stale (dead process), or not configured.
    /// </summary>
    public async Task<DaemonLiveness> CheckDaemonAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadDaemonConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config == null)
        {
            return new DaemonLiveness(DaemonStatus.NotConfigured, 0, 0);
        }

        var (port, pid) = config.Value;

        // Check if the PID is still alive
        bool processAlive;
        try
        {
            var process = Process.GetProcessById(pid);
            processAlive = !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process does not exist
            processAlive = false;
        }

        if (!processAlive)
        {
            return new DaemonLiveness(DaemonStatus.Stale, port, pid);
        }

        // PID is alive — verify it's actually a Gluey daemon via health check
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await httpClient.GetAsync($"http://localhost:{port}/api/health", cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new DaemonLiveness(DaemonStatus.Alive, port, pid);
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        // PID exists but health check failed — treat as stale
        return new DaemonLiveness(DaemonStatus.Stale, port, pid);
    }

    private static string GetDefaultStateDirectory()
    {
        // Check for environment variable first
        var envDir = Environment.GetEnvironmentVariable("GLUEY_STATE_DIR");
        if (!string.IsNullOrEmpty(envDir))
        {
            return envDir;
        }

        // Default to ~/.gluey/state/
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, GlueyDirectoryName, StateDirectoryName);
    }

    private void EnsureStateDirectoryExists()
    {
        if (!Directory.Exists(_stateDirectory))
        {
            Directory.CreateDirectory(_stateDirectory);
        }
    }

    private string GetDaemonConfigFilePath() => Path.Combine(_stateDirectory, DaemonConfigFileName);

    private string GetWorkflowsFilePath() => Path.Combine(_stateDirectory, WorkflowsFileName);

    private string GetWorkflowFilePath(Guid workflowId) => Path.Combine(_stateDirectory, $"{workflowId}.json");

    private async Task<List<Guid>> LoadWorkflowIndexAsync(CancellationToken cancellationToken)
    {
        var workflowsFilePath = GetWorkflowsFilePath();

        if (!File.Exists(workflowsFilePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(workflowsFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task UpdateWorkflowIndexAsync(Guid workflowId, bool add, CancellationToken cancellationToken)
    {
        var workflowIds = await LoadWorkflowIndexAsync(cancellationToken).ConfigureAwait(false);

        if (add)
        {
            if (!workflowIds.Contains(workflowId))
            {
                workflowIds.Add(workflowId);
            }
        }
        else
        {
            workflowIds.Remove(workflowId);
        }

        var json = JsonSerializer.Serialize(workflowIds, JsonOptions);
        var workflowsFilePath = GetWorkflowsFilePath();
        await File.WriteAllTextAsync(workflowsFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Internal record for daemon configuration serialization.
    /// </summary>
    private sealed record DaemonConfig(int Port, int Pid);
}

/// <summary>
/// Daemon liveness status.
/// </summary>
public enum DaemonStatus
{
    NotConfigured,
    Stale,
    Alive
}

/// <summary>
/// Result of a daemon liveness check.
/// </summary>
public sealed record DaemonLiveness(DaemonStatus Status, int Port, int Pid);

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

using System.Text.Json;
using System.Text.Json.Serialization;
using Gluey.Core.Models;
using Gluey.Runtime.Persistence;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for FileStateStore persistence operations.
/// Tests file-based state storage, concurrent access, and error handling.
/// </summary>
public class FileStateStoreTests : IAsyncLifetime
{
    private string _testDirectory = null!;
    private FileStateStore _store = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public Task InitializeAsync()
    {
        // Create a unique temporary directory for each test
        _testDirectory = Path.Combine(Path.GetTempPath(), "gluey-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _store = new FileStateStore(_testDirectory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        return Task.CompletedTask;
    }

    #region SaveWorkflowStateAsync Tests

    [Fact]
    public async Task SaveWorkflowStateAsync_CreatesJsonFile()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");

        // Act
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Assert
        var workflowFilePath = Path.Combine(_testDirectory, $"{workflowInfo.Id}.json");
        Assert.True(File.Exists(workflowFilePath), "Workflow JSON file should exist");

        // Verify content
        var json = await File.ReadAllTextAsync(workflowFilePath);
        var loaded = JsonSerializer.Deserialize<WorkflowInfo>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(workflowInfo.Id, loaded.Id);
        Assert.Equal(workflowInfo.Name, loaded.Name);
        Assert.Equal(workflowInfo.Version, loaded.Version);
        Assert.Equal(workflowInfo.FilePath, loaded.FilePath);
    }

    [Fact]
    public async Task SaveWorkflowStateAsync_CreatesIndexFile()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");

        // Act
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Assert
        var indexFilePath = Path.Combine(_testDirectory, "workflows.json");
        Assert.True(File.Exists(indexFilePath), "Index file should exist");

        // Verify index contains workflow ID
        var json = await File.ReadAllTextAsync(indexFilePath);
        var ids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);

        Assert.NotNull(ids);
        Assert.Contains(workflowInfo.Id, ids);
    }

    [Fact]
    public async Task SaveWorkflowStateAsync_UpdatesExistingWorkflow()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Act - update with new status
        var updatedInfo = workflowInfo.WithStatus(WorkflowStatus.Active);
        await _store.SaveWorkflowStateAsync(updatedInfo);

        // Assert
        var loaded = await _store.LoadWorkflowStateAsync(workflowInfo.Id);
        Assert.NotNull(loaded);
        Assert.Equal(WorkflowStatus.Active, loaded.Status);

        // Verify index only contains ID once
        var indexFilePath = Path.Combine(_testDirectory, "workflows.json");
        var json = await File.ReadAllTextAsync(indexFilePath);
        var ids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);

        Assert.NotNull(ids);
        Assert.Equal(1, ids.Count(id => id == workflowInfo.Id));
    }

    [Fact]
    public async Task SaveWorkflowStateAsync_WithNullWorkflow_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.SaveWorkflowStateAsync(null!));
    }

    #endregion

    #region LoadWorkflowStateAsync Tests

    [Fact]
    public async Task LoadWorkflowStateAsync_ReadsExistingState()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Act
        var loaded = await _store.LoadWorkflowStateAsync(workflowInfo.Id);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(workflowInfo.Id, loaded.Id);
        Assert.Equal(workflowInfo.Name, loaded.Name);
        Assert.Equal(workflowInfo.Version, loaded.Version);
        Assert.Equal(workflowInfo.FilePath, loaded.FilePath);
        Assert.Equal(workflowInfo.Status, loaded.Status);
        Assert.Equal(workflowInfo.MessageCount, loaded.MessageCount);
    }

    [Fact]
    public async Task LoadWorkflowStateAsync_ReturnsNullForNonExistentState()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var loaded = await _store.LoadWorkflowStateAsync(nonExistentId);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadWorkflowStateAsync_ReturnsNullForCorruptedJson()
    {
        // Arrange
        var workflowId = Guid.NewGuid();
        var workflowFilePath = Path.Combine(_testDirectory, $"{workflowId}.json");
        await File.WriteAllTextAsync(workflowFilePath, "{ invalid json content }");

        // Act
        var loaded = await _store.LoadWorkflowStateAsync(workflowId);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadWorkflowStateAsync_FromNonExistentDirectory_ReturnsNull()
    {
        // Arrange - create store with non-existent directory
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "gluey-tests", Guid.NewGuid().ToString());
        var store = new FileStateStore(nonExistentDir);

        // Act
        var loaded = await store.LoadWorkflowStateAsync(Guid.NewGuid());

        // Assert
        Assert.Null(loaded);
    }

    #endregion

    #region LoadAllWorkflowStatesAsync Tests

    [Fact]
    public async Task LoadAllWorkflowStatesAsync_ReturnsAllStates()
    {
        // Arrange
        var workflow1 = WorkflowInfo.Create("workflow-1", "1.0", "/path/to/1.gflow");
        var workflow2 = WorkflowInfo.Create("workflow-2", "2.0", "/path/to/2.gflow");
        var workflow3 = WorkflowInfo.Create("workflow-3", "3.0", "/path/to/3.gflow");

        await _store.SaveWorkflowStateAsync(workflow1);
        await _store.SaveWorkflowStateAsync(workflow2);
        await _store.SaveWorkflowStateAsync(workflow3);

        // Act
        var allStates = await _store.LoadAllWorkflowStatesAsync();

        // Assert
        Assert.NotNull(allStates);
        Assert.Equal(3, allStates.Count);
        Assert.Contains(allStates, w => w.Id == workflow1.Id);
        Assert.Contains(allStates, w => w.Id == workflow2.Id);
        Assert.Contains(allStates, w => w.Id == workflow3.Id);
    }

    [Fact]
    public async Task LoadAllWorkflowStatesAsync_ReturnsEmptyForEmptyStore()
    {
        // Act
        var allStates = await _store.LoadAllWorkflowStatesAsync();

        // Assert
        Assert.NotNull(allStates);
        Assert.Empty(allStates);
    }

    [Fact]
    public async Task LoadAllWorkflowStatesAsync_SkipsCorruptedFiles()
    {
        // Arrange
        var workflow1 = WorkflowInfo.Create("workflow-1", "1.0", "/path/to/1.gflow");
        var workflow2 = WorkflowInfo.Create("workflow-2", "2.0", "/path/to/2.gflow");

        await _store.SaveWorkflowStateAsync(workflow1);
        await _store.SaveWorkflowStateAsync(workflow2);

        // Corrupt workflow2's file
        var workflow2FilePath = Path.Combine(_testDirectory, $"{workflow2.Id}.json");
        await File.WriteAllTextAsync(workflow2FilePath, "{ corrupted json }");

        // Act
        var allStates = await _store.LoadAllWorkflowStatesAsync();

        // Assert - should only return workflow1
        Assert.NotNull(allStates);
        Assert.Single(allStates);
        Assert.Contains(allStates, w => w.Id == workflow1.Id);
        Assert.DoesNotContain(allStates, w => w.Id == workflow2.Id);
    }

    [Fact]
    public async Task LoadAllWorkflowStatesAsync_FromNonExistentDirectory_ReturnsEmpty()
    {
        // Arrange - create store with non-existent directory
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "gluey-tests", Guid.NewGuid().ToString());
        var store = new FileStateStore(nonExistentDir);

        // Act
        var allStates = await store.LoadAllWorkflowStatesAsync();

        // Assert
        Assert.NotNull(allStates);
        Assert.Empty(allStates);
    }

    [Fact]
    public async Task LoadAllWorkflowStatesAsync_SkipsMissingFiles()
    {
        // Arrange
        var workflow1 = WorkflowInfo.Create("workflow-1", "1.0", "/path/to/1.gflow");
        var workflow2 = WorkflowInfo.Create("workflow-2", "2.0", "/path/to/2.gflow");

        await _store.SaveWorkflowStateAsync(workflow1);
        await _store.SaveWorkflowStateAsync(workflow2);

        // Delete workflow2's file but leave it in index
        var workflow2FilePath = Path.Combine(_testDirectory, $"{workflow2.Id}.json");
        File.Delete(workflow2FilePath);

        // Act
        var allStates = await _store.LoadAllWorkflowStatesAsync();

        // Assert - should only return workflow1
        Assert.NotNull(allStates);
        Assert.Single(allStates);
        Assert.Contains(allStates, w => w.Id == workflow1.Id);
    }

    #endregion

    #region DeleteWorkflowStateAsync Tests

    [Fact]
    public async Task DeleteWorkflowStateAsync_RemovesStateFile()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        var workflowFilePath = Path.Combine(_testDirectory, $"{workflowInfo.Id}.json");
        Assert.True(File.Exists(workflowFilePath), "File should exist before delete");

        // Act
        var result = await _store.DeleteWorkflowStateAsync(workflowInfo.Id);

        // Assert
        Assert.True(result, "Delete should return true");
        Assert.False(File.Exists(workflowFilePath), "File should be deleted");
    }

    [Fact]
    public async Task DeleteWorkflowStateAsync_RemovesFromIndex()
    {
        // Arrange
        var workflow1 = WorkflowInfo.Create("workflow-1", "1.0", "/path/to/1.gflow");
        var workflow2 = WorkflowInfo.Create("workflow-2", "2.0", "/path/to/2.gflow");

        await _store.SaveWorkflowStateAsync(workflow1);
        await _store.SaveWorkflowStateAsync(workflow2);

        // Act
        await _store.DeleteWorkflowStateAsync(workflow1.Id);

        // Assert - check index
        var indexFilePath = Path.Combine(_testDirectory, "workflows.json");
        var json = await File.ReadAllTextAsync(indexFilePath);
        var ids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);

        Assert.NotNull(ids);
        Assert.DoesNotContain(workflow1.Id, ids);
        Assert.Contains(workflow2.Id, ids);
    }

    [Fact]
    public async Task DeleteWorkflowStateAsync_ReturnsFalseForNonExistentWorkflow()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _store.DeleteWorkflowStateAsync(nonExistentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteWorkflowStateAsync_ReturnsFalseWhenFileDoesNotExist()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Manually delete the file
        var workflowFilePath = Path.Combine(_testDirectory, $"{workflowInfo.Id}.json");
        File.Delete(workflowFilePath);

        // Act
        var result = await _store.DeleteWorkflowStateAsync(workflowInfo.Id);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConcurrentAccess_MultipleSaves_NoDataCorruption()
    {
        // Arrange
        var workflows = Enumerable.Range(0, 10)
            .Select(i => WorkflowInfo.Create($"workflow-{i}", "1.0", $"/path/to/{i}.gflow"))
            .ToList();

        // Act - save all workflows concurrently
        var saveTasks = workflows.Select(w => _store.SaveWorkflowStateAsync(w));
        await Task.WhenAll(saveTasks);

        // Assert - all workflows should be saved correctly
        var allStates = await _store.LoadAllWorkflowStatesAsync();
        Assert.Equal(10, allStates.Count);

        foreach (var workflow in workflows)
        {
            var loaded = await _store.LoadWorkflowStateAsync(workflow.Id);
            Assert.NotNull(loaded);
            Assert.Equal(workflow.Id, loaded.Id);
            Assert.Equal(workflow.Name, loaded.Name);
        }
    }

    [Fact]
    public async Task ConcurrentAccess_MixedOperations_NoExceptions()
    {
        // Arrange
        var workflows = Enumerable.Range(0, 5)
            .Select(i => WorkflowInfo.Create($"workflow-{i}", "1.0", $"/path/to/{i}.gflow"))
            .ToList();

        // Save initial workflows
        foreach (var workflow in workflows)
        {
            await _store.SaveWorkflowStateAsync(workflow);
        }

        // Act - perform mixed operations concurrently
        var tasks = new List<Task>();

        // Concurrent reads
        tasks.AddRange(workflows.Select(w => _store.LoadWorkflowStateAsync(w.Id)));

        // Concurrent updates
        tasks.AddRange(workflows.Take(3).Select(w =>
            _store.SaveWorkflowStateAsync(w.WithStatus(WorkflowStatus.Active))));

        // Concurrent deletes
        tasks.AddRange(workflows.Skip(3).Select(w => _store.DeleteWorkflowStateAsync(w.Id)));

        // Concurrent LoadAll
        tasks.Add(_store.LoadAllWorkflowStatesAsync());

        // Assert - no exceptions should be thrown
        await Task.WhenAll(tasks);

        // Verify final state
        var allStates = await _store.LoadAllWorkflowStatesAsync();
        Assert.Equal(3, allStates.Count); // 5 - 2 deleted = 3
    }

    [Fact]
    public async Task ConcurrentAccess_SameWorkflowUpdates_LastWriteWins()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Act - update same workflow concurrently with different statuses
        var tasks = new[]
        {
            _store.SaveWorkflowStateAsync(workflowInfo.WithStatus(WorkflowStatus.Active)),
            _store.SaveWorkflowStateAsync(workflowInfo.WithStatus(WorkflowStatus.Paused)),
            _store.SaveWorkflowStateAsync(workflowInfo.WithStatus(WorkflowStatus.Stopped))
        };

        await Task.WhenAll(tasks);

        // Assert - workflow should have one of the statuses (last write wins)
        var loaded = await _store.LoadWorkflowStateAsync(workflowInfo.Id);
        Assert.NotNull(loaded);
        Assert.Contains(loaded.Status, new[]
        {
            WorkflowStatus.Active,
            WorkflowStatus.Paused,
            WorkflowStatus.Stopped
        });
    }

    [Fact]
    public async Task ConcurrentAccess_MultipleReads_ReturnConsistentData()
    {
        // Arrange
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");
        await _store.SaveWorkflowStateAsync(workflowInfo);

        // Act - read same workflow concurrently
        var readTasks = Enumerable.Range(0, 20)
            .Select(_ => _store.LoadWorkflowStateAsync(workflowInfo.Id))
            .ToArray();

        var results = await Task.WhenAll(readTasks);

        // Assert - all reads should return the same data
        Assert.All(results, loaded =>
        {
            Assert.NotNull(loaded);
            Assert.Equal(workflowInfo.Id, loaded.Id);
            Assert.Equal(workflowInfo.Name, loaded.Name);
        });
    }

    #endregion

    #region StateDirectory Tests

    [Fact]
    public void Constructor_WithCustomDirectory_SetsStateDirectory()
    {
        // Arrange
        var customDir = Path.Combine(Path.GetTempPath(), "custom-gluey");

        // Act
        var store = new FileStateStore(customDir);

        // Assert
        Assert.Equal(customDir, store.StateDirectory);
    }

    [Fact]
    public void Constructor_WithNullDirectory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileStateStore(null!));
    }

    [Fact]
    public async Task SaveWorkflowStateAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var newDir = Path.Combine(Path.GetTempPath(), "gluey-tests", Guid.NewGuid().ToString());
        var store = new FileStateStore(newDir);
        var workflowInfo = WorkflowInfo.Create("test-workflow", "1.0", "/path/to/test.gflow");

        Assert.False(Directory.Exists(newDir), "Directory should not exist initially");

        try
        {
            // Act
            await store.SaveWorkflowStateAsync(workflowInfo);

            // Assert
            Assert.True(Directory.Exists(newDir), "Directory should be created");
            var workflowFilePath = Path.Combine(newDir, $"{workflowInfo.Id}.json");
            Assert.True(File.Exists(workflowFilePath), "Workflow file should exist");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    #endregion

    #region DaemonConfig Tests (Bonus Coverage)

    [Fact]
    public async Task SaveDaemonConfigAsync_CreatesDaemonJsonFile()
    {
        // Arrange
        var port = 5000;
        var pid = 12345;

        // Act
        await _store.SaveDaemonConfigAsync(port, pid);

        // Assert
        var daemonFilePath = Path.Combine(_testDirectory, "daemon.json");
        Assert.True(File.Exists(daemonFilePath), "daemon.json should exist");
    }

    [Fact]
    public async Task LoadDaemonConfigAsync_ReturnsConfigWhenExists()
    {
        // Arrange
        var port = 5000;
        var pid = 12345;
        await _store.SaveDaemonConfigAsync(port, pid);

        // Act
        var config = await _store.LoadDaemonConfigAsync();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(port, config.Value.port);
        Assert.Equal(pid, config.Value.pid);
    }

    [Fact]
    public async Task LoadDaemonConfigAsync_ReturnsNullWhenNotExists()
    {
        // Act
        var config = await _store.LoadDaemonConfigAsync();

        // Assert
        Assert.Null(config);
    }

    [Fact]
    public async Task DeleteDaemonConfigAsync_RemovesDaemonFile()
    {
        // Arrange
        await _store.SaveDaemonConfigAsync(5000, 12345);
        var daemonFilePath = Path.Combine(_testDirectory, "daemon.json");
        Assert.True(File.Exists(daemonFilePath));

        // Act
        await _store.DeleteDaemonConfigAsync();

        // Assert
        Assert.False(File.Exists(daemonFilePath));
    }

    #endregion
}

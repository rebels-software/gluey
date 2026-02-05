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
using Gluey.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for WorkflowManager lifecycle operations.
/// Tests workflow state transitions, validation, and multi-workflow management.
/// These tests are run sequentially to avoid port conflicts when starting HTTP servers.
/// </summary>
[Collection("WorkflowManager Sequential Tests")]
public class WorkflowManagerTests : IAsyncLifetime
{
    private WorkflowManager? _manager;
    private readonly string _sampleFlowPath;

    public WorkflowManagerTests()
    {
        // Use absolute path to the hello-world sample
        _sampleFlowPath = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(typeof(WorkflowManagerTests).Assembly.Location)!,
                "..", "..", "..", "..", "..",
                "samples", "01-hello-world.gflow"));
    }

    public Task InitializeAsync()
    {
        // Create a fresh manager for each test
        var pluginRegistry = new PluginRegistry();
        var logger = NullLogger<WorkflowManager>.Instance;
        _manager = new WorkflowManager(pluginRegistry, logger);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_manager != null)
        {
            await _manager.DisposeAsync();
        }
    }

    #region LoadAsync Tests

    [Fact]
    public async Task LoadAsync_CreatesWorkflowWithDraftStatus()
    {
        // Act
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Assert
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Draft, workflow.Status);
        Assert.Equal("hello-world", workflow.Name);
        Assert.Equal("1.0", workflow.Version);
        Assert.Equal(_sampleFlowPath, workflow.FilePath);
        Assert.Null(workflow.StartedAt);
        Assert.Equal(0, workflow.MessageCount);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidFilePath_ThrowsException()
    {
        // Arrange
        var invalidPath = "/nonexistent/path/invalid.gflow";

        // Act & Assert - can throw FileNotFoundException or DirectoryNotFoundException
        await Assert.ThrowsAnyAsync<IOException>(
            async () => await _manager!.LoadAsync(invalidPath));
    }

    [Fact]
    public async Task LoadAsync_WithEmptyFilePath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager!.LoadAsync(string.Empty));
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_TransitionsDraftToActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Act
        await _manager.StartAsync(id);

        // Allow brief time for workflow to initialize
        await Task.Delay(100);

        // Assert
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Active, workflow.Status);
        Assert.NotNull(workflow.StartedAt);

        // Cleanup
        await _manager.StopAsync(id);
        await Task.Delay(100); // Allow time to fully stop
    }

    [Fact]
    public async Task StartAsync_TransitionsStoppedToActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);
        await _manager.StopAsync(id);
        await Task.Delay(100);

        // Verify it's stopped
        var stoppedWorkflow = _manager.Get(id);
        Assert.Equal(WorkflowStatus.Stopped, stoppedWorkflow!.Status);

        // Act
        await _manager.StartAsync(id);
        await Task.Delay(100);

        // Assert
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Active, workflow.Status);

        // Cleanup
        await _manager.StopAsync(id);
        await Task.Delay(100);
    }

    [Fact]
    public async Task StartAsync_NoOpWhenAlreadyActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);
        var workflowBefore = _manager.Get(id);

        // Act - start again
        await _manager.StartAsync(id);

        // Assert - still active, no error
        var workflowAfter = _manager.Get(id);
        Assert.NotNull(workflowAfter);
        Assert.Equal(WorkflowStatus.Active, workflowAfter.Status);
        Assert.Equal(workflowBefore!.StartedAt, workflowAfter.StartedAt);

        // Cleanup
        await _manager.StopAsync(id);
        await Task.Delay(100);
    }

    [Fact]
    public async Task StartAsync_WithUnknownId_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager!.StartAsync(unknownId));
        Assert.Contains("not found", ex.Message);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_TransitionsActiveToStopped()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);

        // Act
        await _manager.StopAsync(id);
        await Task.Delay(100);

        // Assert
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Stopped, workflow.Status);
    }

    [Fact]
    public async Task StopAsync_NoOpWhenNotActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Act - stop without starting
        await _manager!.StopAsync(id);

        // Assert - still in Draft, no error
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Draft, workflow.Status);
    }

    [Fact]
    public async Task StopAsync_WithUnknownId_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager!.StopAsync(unknownId));
        Assert.Contains("not found", ex.Message);
    }

    #endregion

    #region PauseAsync Tests

    [Fact]
    public async Task PauseAsync_TransitionsActiveToPaused()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);

        // Act
        await _manager.PauseAsync(id);
        await Task.Delay(100);

        // Assert
        var workflow = _manager.Get(id);
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowStatus.Paused, workflow.Status);
    }

    [Fact]
    public async Task PauseAsync_ThrowsWhenNotActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager.PauseAsync(id));
        Assert.Contains("Cannot pause", ex.Message);
        Assert.Contains("not running", ex.Message);
    }

    [Fact]
    public async Task PauseAsync_ThrowsWhenStopped()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);
        await _manager.StopAsync(id);
        await Task.Delay(100);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager.PauseAsync(id));
        Assert.Contains("Cannot pause", ex.Message);
    }

    [Fact]
    public async Task PauseAsync_WithUnknownId_ThrowsInvalidOperationException()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager!.PauseAsync(unknownId));
        Assert.Contains("not found", ex.Message);
    }

    #endregion

    #region UnloadAsync Tests

    [Fact]
    public async Task UnloadAsync_RemovesWorkflowCompletely()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        var workflowBefore = _manager.Get(id);
        Assert.NotNull(workflowBefore);

        // Act
        await _manager.UnloadAsync(id);

        // Assert
        var workflowAfter = _manager.Get(id);
        Assert.Null(workflowAfter);
        Assert.DoesNotContain(_manager.List(), w => w.Id == id);
    }

    [Fact]
    public async Task UnloadAsync_StopsActiveWorkflow()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Active, _manager.Get(id)!.Status);

        // Act
        await _manager.UnloadAsync(id);
        await Task.Delay(100);

        // Assert - workflow is removed
        var workflow = _manager.Get(id);
        Assert.Null(workflow);
    }

    [Fact]
    public async Task UnloadAsync_WithUnknownId_DoesNotThrow()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act & Assert - should not throw
        await _manager!.UnloadAsync(unknownId);
    }

    #endregion

    #region Multiple Workflows Tests

    [Fact]
    public async Task MultipleWorkflows_ManagedIndependently()
    {
        // Arrange - load two workflows
        var id1 = await _manager!.LoadAsync(_sampleFlowPath);
        var id2 = await _manager.LoadAsync(_sampleFlowPath);

        Assert.NotEqual(id1, id2);

        // Act - start first workflow
        await _manager.StartAsync(id1);
        await Task.Delay(100);

        // Assert - workflows have independent states
        var workflow1 = _manager.Get(id1);
        var workflow2 = _manager.Get(id2);

        Assert.NotNull(workflow1);
        Assert.NotNull(workflow2);
        Assert.Equal(WorkflowStatus.Active, workflow1.Status);
        Assert.Equal(WorkflowStatus.Draft, workflow2.Status);

        // Act - stop first, start second
        await _manager.StopAsync(id1);
        await Task.Delay(100);
        await _manager.StartAsync(id2);
        await Task.Delay(100);

        // Assert - states updated independently
        workflow1 = _manager.Get(id1);
        workflow2 = _manager.Get(id2);

        Assert.Equal(WorkflowStatus.Stopped, workflow1!.Status);
        Assert.Equal(WorkflowStatus.Active, workflow2!.Status);

        // Cleanup
        await _manager.StopAsync(id2);
        await Task.Delay(100);
    }

    [Fact]
    public async Task MultipleWorkflows_UnloadDoesNotAffectOthers()
    {
        // Arrange - load three workflows
        var id1 = await _manager!.LoadAsync(_sampleFlowPath);
        var id2 = await _manager.LoadAsync(_sampleFlowPath);
        var id3 = await _manager.LoadAsync(_sampleFlowPath);

        // Act - unload the middle one
        await _manager.UnloadAsync(id2);

        // Assert - other workflows still exist
        var workflow1 = _manager.Get(id1);
        var workflow2 = _manager.Get(id2);
        var workflow3 = _manager.Get(id3);

        Assert.NotNull(workflow1);
        Assert.Null(workflow2);
        Assert.NotNull(workflow3);

        var allWorkflows = _manager.List();
        Assert.Equal(2, allWorkflows.Count);
        Assert.Contains(allWorkflows, w => w.Id == id1);
        Assert.Contains(allWorkflows, w => w.Id == id3);
    }

    #endregion

    #region List and Get Tests

    [Fact]
    public async Task List_ReturnsAllWorkflows()
    {
        // Arrange - load multiple workflows
        var id1 = await _manager!.LoadAsync(_sampleFlowPath);
        var id2 = await _manager.LoadAsync(_sampleFlowPath);
        var id3 = await _manager.LoadAsync(_sampleFlowPath);

        // Act
        var workflows = _manager.List();

        // Assert
        Assert.Equal(3, workflows.Count);
        Assert.Contains(workflows, w => w.Id == id1);
        Assert.Contains(workflows, w => w.Id == id2);
        Assert.Contains(workflows, w => w.Id == id3);
    }

    [Fact]
    public void List_ReturnsEmptyList_WhenNoWorkflows()
    {
        // Act
        var workflows = _manager!.List();

        // Assert
        Assert.NotNull(workflows);
        Assert.Empty(workflows);
    }

    [Fact]
    public async Task Get_ReturnsWorkflowById()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Act
        var workflow = _manager.Get(id);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(id, workflow.Id);
        Assert.Equal("hello-world", workflow.Name);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownId()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var workflow = _manager!.Get(unknownId);

        // Assert
        Assert.Null(workflow);
    }

    #endregion

    #region State Transition Tests

    [Fact]
    public async Task StateTransition_DraftToActiveToPausedToActive()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Draft -> Active
        await _manager.StartAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Active, _manager.Get(id)!.Status);

        // Active -> Paused
        await _manager.PauseAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Paused, _manager.Get(id)!.Status);

        // Paused -> Active (resume)
        await _manager.StartAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Active, _manager.Get(id)!.Status);

        // Cleanup
        await _manager.StopAsync(id);
        await Task.Delay(100);
    }

    [Fact]
    public async Task StateTransition_DraftToActiveToStopped()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);

        // Draft -> Active
        await _manager.StartAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Active, _manager.Get(id)!.Status);

        // Active -> Stopped
        await _manager.StopAsync(id);
        await Task.Delay(100);
        Assert.Equal(WorkflowStatus.Stopped, _manager.Get(id)!.Status);
    }

    [Fact]
    public async Task StateTransition_PausedCannotBeStoppedDirectly()
    {
        // Arrange
        var id = await _manager!.LoadAsync(_sampleFlowPath);
        await _manager.StartAsync(id);
        await Task.Delay(100);
        await _manager.PauseAsync(id);
        await Task.Delay(100);

        // Act - stop a paused workflow (should be no-op)
        await _manager.StopAsync(id);
        await Task.Delay(100);

        // Assert - still paused since StopAsync only acts on Active workflows
        var workflow = _manager.Get(id);
        Assert.Equal(WorkflowStatus.Paused, workflow!.Status);
    }

    #endregion
}

/// <summary>
/// Collection definition to force WorkflowManager tests to run sequentially.
/// This prevents port conflicts when multiple tests start HTTP servers.
/// </summary>
[CollectionDefinition("WorkflowManager Sequential Tests", DisableParallelization = true)]
public class WorkflowManagerTestCollection
{
}

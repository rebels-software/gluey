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

namespace Gluey.Core.Abstractions;

/// <summary>
/// Interface for persisting and retrieving workflow state.
/// Implementations handle storage of workflow information for daemon mode.
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Saves the state of a workflow.
    /// </summary>
    /// <param name="workflowInfo">The workflow information to persist.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task SaveWorkflowStateAsync(WorkflowInfo workflowInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the state of a specific workflow by its ID.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The workflow information, or null if not found.</returns>
    Task<WorkflowInfo?> LoadWorkflowStateAsync(Guid workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the state of all known workflows.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A collection of all persisted workflow information.</returns>
    Task<IReadOnlyList<WorkflowInfo>> LoadAllWorkflowStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the persisted state of a workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>True if the workflow state was deleted, false if it was not found.</returns>
    Task<bool> DeleteWorkflowStateAsync(Guid workflowId, CancellationToken cancellationToken = default);
}

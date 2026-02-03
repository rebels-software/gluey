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

namespace Gluey.Core.Models;

/// <summary>
/// Represents the operational status of a workflow.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>
    /// Workflow is loaded but not yet started.
    /// </summary>
    Draft,

    /// <summary>
    /// Workflow was explicitly stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Workflow is running and processing messages.
    /// </summary>
    Active,

    /// <summary>
    /// Workflow is temporarily suspended.
    /// </summary>
    Paused,

    /// <summary>
    /// Workflow encountered an error and stopped.
    /// </summary>
    Error
}

/// <summary>
/// Represents runtime information about a workflow instance.
/// </summary>
/// <param name="Id">Unique identifier for this workflow instance.</param>
/// <param name="Name">The workflow name from the .gflow file.</param>
/// <param name="Version">The workflow version from the .gflow file.</param>
/// <param name="FilePath">Path to the .gflow file.</param>
/// <param name="Status">Current operational status.</param>
/// <param name="StartedAt">When the workflow was last started, or null if never started.</param>
/// <param name="LastError">Most recent error message, or null if no error.</param>
/// <param name="MessageCount">Number of messages processed since last start.</param>
public sealed record WorkflowInfo(
    Guid Id,
    string Name,
    string Version,
    string FilePath,
    WorkflowStatus Status,
    DateTimeOffset? StartedAt,
    string? LastError,
    long MessageCount)
{
    /// <summary>
    /// Creates a new WorkflowInfo in Draft status.
    /// </summary>
    public static WorkflowInfo Create(string name, string version, string filePath)
    {
        return new WorkflowInfo(
            Guid.NewGuid(),
            name,
            version,
            filePath,
            WorkflowStatus.Draft,
            StartedAt: null,
            LastError: null,
            MessageCount: 0);
    }

    /// <summary>
    /// Creates a new WorkflowInfo with updated status.
    /// </summary>
    public WorkflowInfo WithStatus(WorkflowStatus newStatus)
    {
        return this with { Status = newStatus };
    }

    /// <summary>
    /// Creates a new WorkflowInfo marking it as started.
    /// </summary>
    public WorkflowInfo WithStarted()
    {
        return this with
        {
            Status = WorkflowStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            LastError = null,
            MessageCount = 0
        };
    }

    /// <summary>
    /// Creates a new WorkflowInfo with an error.
    /// </summary>
    public WorkflowInfo WithError(string errorMessage)
    {
        return this with
        {
            Status = WorkflowStatus.Error,
            LastError = errorMessage
        };
    }

    /// <summary>
    /// Creates a new WorkflowInfo with incremented message count.
    /// </summary>
    public WorkflowInfo WithMessageProcessed()
    {
        return this with { MessageCount = MessageCount + 1 };
    }
}

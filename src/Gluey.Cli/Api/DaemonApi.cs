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
using Gluey.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Gluey.Cli.Api;

/// <summary>
/// Request DTO for loading a workflow.
/// </summary>
public sealed record LoadWorkflowRequest(string FilePath);

/// <summary>
/// Response DTO containing a workflow ID.
/// </summary>
public sealed record WorkflowIdResponse(Guid Id);

/// <summary>
/// HTTP API endpoints for the Gluey daemon.
/// Provides CRUD operations for workflows on port 6262.
/// </summary>
public static class DaemonApi
{
    /// <summary>
    /// JSON serialization options for API responses.
    /// Uses camelCase naming and string enum conversion.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Maps workflow CRUD endpoints to the WebApplication.
    /// </summary>
    /// <param name="app">The WebApplication to configure.</param>
    /// <returns>The configured WebApplication.</returns>
    public static WebApplication MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflows");

        group.MapPost("/", LoadWorkflow);
        group.MapDelete("/{id:guid}", UnloadWorkflow);
        group.MapGet("/", ListWorkflows);
        group.MapGet("/{id:guid}", GetWorkflow);

        return app;
    }

    /// <summary>
    /// POST /api/workflows - Load a workflow from a .gflow file.
    /// </summary>
    private static async Task<IResult> LoadWorkflow(
        HttpContext context,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await context.Request.ReadFromJsonAsync<LoadWorkflowRequest>(
                JsonOptions, cancellationToken);

            if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
            {
                return Results.BadRequest(new { error = "filePath is required" });
            }

            var id = await manager.LoadAsync(request.FilePath, cancellationToken);
            var response = new WorkflowIdResponse(id);

            return Results.Created($"/api/workflows/{id}", response);
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { error = $"File not found: {ex.FileName}" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/workflows/{id} - Unload a workflow.
    /// </summary>
    private static async Task<IResult> UnloadWorkflow(
        Guid id,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        await manager.UnloadAsync(id, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// GET /api/workflows - List all loaded workflows.
    /// </summary>
    private static IResult ListWorkflows(WorkflowManager manager)
    {
        var workflows = manager.List();
        return Results.Json(workflows, JsonOptions);
    }

    /// <summary>
    /// GET /api/workflows/{id} - Get a single workflow by ID.
    /// </summary>
    private static IResult GetWorkflow(Guid id, WorkflowManager manager)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        return Results.Json(workflow, JsonOptions);
    }
}

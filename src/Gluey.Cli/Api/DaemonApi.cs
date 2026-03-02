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
using Gluey.Runtime.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

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
/// Response DTO for health check endpoint.
/// </summary>
public sealed record HealthResponse(string Status, int Workflows, string Uptime);

/// <summary>
/// HTTP API endpoints for the Gluey daemon.
/// Provides CRUD and control operations for workflows on port 6262.
/// </summary>
public static class DaemonApi
{
    /// <summary>
    /// Timestamp when the daemon started. Used for uptime calculation.
    /// </summary>
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

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
    /// Maps workflow CRUD and control endpoints to the WebApplication.
    /// </summary>
    /// <param name="app">The WebApplication to configure.</param>
    /// <returns>The configured WebApplication.</returns>
    public static WebApplication MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workflows");

        // CRUD endpoints
        group.MapPost("/", LoadWorkflow);
        group.MapDelete("/{id:guid}", UnloadWorkflow);
        group.MapGet("/", ListWorkflows);
        group.MapGet("/{id:guid}", GetWorkflow);

        // Control endpoints
        group.MapPost("/{id:guid}/start", StartWorkflow);
        group.MapPost("/{id:guid}/stop", StopWorkflow);
        group.MapPost("/{id:guid}/pause", PauseWorkflow);
        group.MapPost("/{id:guid}/reload", ReloadWorkflow);

        // Log endpoints
        group.MapGet("/{id:guid}/logs", GetWorkflowLogs);
        group.MapGet("/{id:guid}/logs/stream", StreamWorkflowLogs);

        return app;
    }

    /// <summary>
    /// Maps daemon-level endpoints (health, shutdown) to the WebApplication.
    /// </summary>
    /// <param name="app">The WebApplication to configure.</param>
    /// <returns>The configured WebApplication.</returns>
    public static WebApplication MapDaemonEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", GetHealth);
        app.MapPost("/api/shutdown", Shutdown);

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

    /// <summary>
    /// POST /api/workflows/{id}/start - Start a workflow.
    /// </summary>
    private static async Task<IResult> StartWorkflow(
        Guid id,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        try
        {
            await manager.StartAsync(id, cancellationToken);
            var updatedWorkflow = manager.Get(id);
            return Results.Json(updatedWorkflow, JsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/workflows/{id}/stop - Stop a workflow.
    /// </summary>
    private static async Task<IResult> StopWorkflow(
        Guid id,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        try
        {
            await manager.StopAsync(id, cancellationToken);
            var updatedWorkflow = manager.Get(id);
            return Results.Json(updatedWorkflow, JsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/workflows/{id}/pause - Pause a workflow.
    /// </summary>
    private static async Task<IResult> PauseWorkflow(
        Guid id,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        try
        {
            await manager.PauseAsync(id, cancellationToken);
            var updatedWorkflow = manager.Get(id);
            return Results.Json(updatedWorkflow, JsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/workflows/{id}/reload - Hot reload a workflow.
    /// </summary>
    private static async Task<IResult> ReloadWorkflow(
        Guid id,
        WorkflowManager manager,
        CancellationToken cancellationToken)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        try
        {
            await manager.ReloadAsync(id, cancellationToken);
            var updatedWorkflow = manager.Get(id);
            return Results.Json(updatedWorkflow, JsonOptions);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { error = $"File not found: {ex.FileName}" });
        }
    }

    /// <summary>
    /// GET /api/health - Returns daemon health status.
    /// </summary>
    private static IResult GetHealth(WorkflowManager manager)
    {
        var uptime = DateTimeOffset.UtcNow - StartTime;
        var uptimeFormatted = FormatUptime(uptime);
        var workflowCount = manager.List().Count;

        var response = new HealthResponse("healthy", workflowCount, uptimeFormatted);
        return Results.Json(response, JsonOptions);
    }

    /// <summary>
    /// POST /api/shutdown - Graceful daemon shutdown.
    /// </summary>
    private static IResult Shutdown(IHostApplicationLifetime lifetime)
    {
        // Request graceful shutdown
        lifetime.StopApplication();

        return Results.Ok(new { message = "Shutdown initiated" });
    }

    /// <summary>
    /// GET /api/workflows/{id}/logs - Get recent log entries for a workflow.
    /// </summary>
    private static IResult GetWorkflowLogs(
        Guid id,
        WorkflowManager manager,
        LogBuffer logBuffer,
        HttpContext context)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            return Results.NotFound(new { error = $"Workflow {id} not found" });
        }

        // Parse optional ?lines= query parameter (default 100)
        var linesParam = context.Request.Query["lines"].FirstOrDefault();
        var lines = 100;
        if (linesParam is not null && int.TryParse(linesParam, out var parsedLines) && parsedLines > 0)
        {
            lines = parsedLines;
        }

        var logs = logBuffer.GetLogs(workflow.Name, lines);
        return Results.Json(logs, JsonOptions);
    }

    /// <summary>
    /// GET /api/workflows/{id}/logs/stream - Stream log entries via Server-Sent Events.
    /// </summary>
    private static async Task StreamWorkflowLogs(
        Guid id,
        WorkflowManager manager,
        LogBuffer logBuffer,
        HttpContext context)
    {
        var workflow = manager.Get(id);
        if (workflow == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = $"Workflow {id} not found" }, JsonOptions);
            return;
        }

        // Set SSE headers
        context.Response.Headers["Content-Type"] = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";

        var ct = context.RequestAborted;

        try
        {
            await foreach (var entry in logBuffer.Subscribe(workflow.Name, ct))
            {
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                await context.Response.WriteAsync($"data: {json}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable uptime string.
    /// </summary>
    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        if (uptime.TotalMinutes >= 1)
        {
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        }
        return $"{uptime.Seconds}s";
    }
}

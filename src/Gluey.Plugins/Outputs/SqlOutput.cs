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

using System.Data.Common;
using System.Text;
using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Gluey.Plugins.Outputs;

/// <summary>
/// SQL output plugin that inserts messages into PostgreSQL or SQL Server databases.
/// Auto-detects the database dialect from the connection string prefix.
/// </summary>
public sealed class SqlOutput : IOutputPlugin
{
    private enum SqlDialect
    {
        PostgreSQL,
        SqlServer
    }

    private string _connectionString = "";
    private string _table = "";
    private List<ColumnMapping> _columns = [];
    private SqlDialect _dialect;
    private bool _disposed;

    public string Type => "sql";

    /// <summary>
    /// Initializes the SQL output with configuration.
    /// </summary>
    /// <param name="config">Configuration containing connection_string, table, and columns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Extract connection string (required)
        if (config.TryGetValue("connection_string", out var connStrElement) && connStrElement.ValueKind == JsonValueKind.String)
        {
            _connectionString = connStrElement.GetString() ?? "";
        }
        // Also check 'url' as alternative config key
        else if (config.TryGetValue("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String)
        {
            _connectionString = urlElement.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(_connectionString))
        {
            throw new InvalidOperationException("SQL connection string is required. Use 'connection_string' or 'url' config.");
        }

        // Auto-detect dialect from connection string prefix
        _dialect = DetectDialect(_connectionString);

        // Extract table name (required)
        if (config.TryGetValue("table", out var tableElement) && tableElement.ValueKind == JsonValueKind.String)
        {
            _table = tableElement.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(_table))
        {
            throw new InvalidOperationException("SQL table name is required. Use 'table' config.");
        }

        // Extract column mappings (required)
        if (config.TryGetValue("columns", out var columnsElement))
        {
            _columns = ParseColumnMappings(columnsElement);
        }

        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("SQL column mappings are required. Use 'columns' config with column: field pairs.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a message to the SQL database by inserting a row.
    /// On failure, logs the error and continues (does not throw).
    /// </summary>
    public async Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = BuildInsertSql();

            // Add parameters from message payload
            foreach (var column in _columns)
            {
                var value = GetFieldValue(message.Payload.RootElement, column.Field);
                var param = command.CreateParameter();
                param.ParameterName = GetParameterName(column.Column);
                param.Value = value ?? DBNull.Value;
                command.Parameters.Add(param);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Rethrow so the caller (WorkflowRunner) can log the error through ILogger.
            // WorkflowRunner wraps output writes in try-catch and continues gracefully.
            throw new InvalidOperationException(
                $"Insert failed for table '{_table}' ({_dialect}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disposes the SQL output plugin. Connections are created per-write, so no cleanup needed.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        // Connections are created and disposed per-write, no persistent connection to close
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Detects the SQL dialect from the connection string prefix.
    /// </summary>
    private static SqlDialect DetectDialect(string connectionString)
    {
        // Check for PostgreSQL patterns
        if (connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) && connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return SqlDialect.PostgreSQL;
        }

        // Check for SQL Server patterns
        if (connectionString.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase) && !connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("sqlserver://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("mssql://", StringComparison.OrdinalIgnoreCase))
        {
            return SqlDialect.SqlServer;
        }

        // Default to PostgreSQL if ambiguous
        return SqlDialect.PostgreSQL;
    }

    /// <summary>
    /// Creates a database connection based on the detected dialect.
    /// </summary>
    private DbConnection CreateConnection()
    {
        return _dialect switch
        {
            SqlDialect.PostgreSQL => new NpgsqlConnection(_connectionString),
            SqlDialect.SqlServer => new SqlConnection(_connectionString),
            _ => throw new InvalidOperationException($"Unsupported SQL dialect: {_dialect}")
        };
    }

    /// <summary>
    /// Builds the INSERT SQL statement for the configured table and columns.
    /// </summary>
    private string BuildInsertSql()
    {
        var columnNames = string.Join(", ", _columns.Select(c => QuoteIdentifier(c.Column)));
        var paramNames = string.Join(", ", _columns.Select(c => GetParameterPlaceholder(c.Column)));

        return $"INSERT INTO {QuoteIdentifier(_table)} ({columnNames}) VALUES ({paramNames})";
    }

    /// <summary>
    /// Quotes an identifier (table or column name) based on the SQL dialect.
    /// </summary>
    private string QuoteIdentifier(string identifier)
    {
        return _dialect switch
        {
            SqlDialect.PostgreSQL => $"\"{identifier}\"",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => identifier
        };
    }

    /// <summary>
    /// Gets the parameter placeholder for a column based on the SQL dialect.
    /// </summary>
    private string GetParameterPlaceholder(string column)
    {
        return _dialect switch
        {
            SqlDialect.PostgreSQL => $"@{column}",
            SqlDialect.SqlServer => $"@{column}",
            _ => $"@{column}"
        };
    }

    /// <summary>
    /// Gets the parameter name for a column (prefixed with @).
    /// </summary>
    private static string GetParameterName(string column)
    {
        return $"@{column}";
    }

    /// <summary>
    /// Parses column mappings from the config element.
    /// Supports object format: { column_name: "field_name", ... }
    /// </summary>
    private static List<ColumnMapping> ParseColumnMappings(JsonElement element)
    {
        var mappings = new List<ColumnMapping>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var column = property.Name;
                var field = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? column
                    : column;

                mappings.Add(new ColumnMapping(column, field));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            // Support array format: [{ column: "col", field: "field" }, ...]
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    string? column = null;
                    string? field = null;

                    if (item.TryGetProperty("column", out var colProp) && colProp.ValueKind == JsonValueKind.String)
                    {
                        column = colProp.GetString();
                    }
                    if (item.TryGetProperty("field", out var fieldProp) && fieldProp.ValueKind == JsonValueKind.String)
                    {
                        field = fieldProp.GetString();
                    }

                    if (!string.IsNullOrEmpty(column))
                    {
                        mappings.Add(new ColumnMapping(column, field ?? column));
                    }
                }
            }
        }

        return mappings;
    }

    /// <summary>
    /// Gets a field value from a JsonElement, supporting nested paths (e.g., "device.location.id").
    /// Returns the value as the appropriate CLR type for SQL parameters.
    /// </summary>
    private static object? GetFieldValue(JsonElement element, string path)
    {
        var parts = path.Split('.');
        var current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var next))
            {
                return null;
            }
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => ConvertStringValue(current.GetString()),
            JsonValueKind.Number when current.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number when current.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number when current.TryGetDouble(out var doubleVal) => doubleVal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object or JsonValueKind.Array => current.GetRawText(), // Store as JSON string
            _ => current.GetRawText()
        };
    }

    /// <summary>
    /// Converts string values to appropriate CLR types.
    /// Detects ISO 8601 datetime strings and converts them to DateTimeOffset for proper SQL timestamp handling.
    /// </summary>
    private static object? ConvertStringValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // Try to parse as ISO 8601 datetime (e.g., "2026-01-29T10:30:45.123Z" or "2026-01-29T10:30:45.123+00:00")
        // This allows now() function results to be properly inserted into TIMESTAMP/TIMESTAMPTZ columns
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dateTimeOffset))
        {
            // Check if it looks like a datetime (has T separator and contains time components)
            // This prevents regular strings like "hello" from being misinterpreted
            var tIndex = value.IndexOf('T');
            if (tIndex > 0 && (value.EndsWith('Z') || value.Contains('+') || value.IndexOf('-', tIndex) >= 0))
            {
                return dateTimeOffset;
            }
        }

        return value;
    }

    /// <summary>
    /// Represents a mapping from a database column to a message field.
    /// </summary>
    private sealed record ColumnMapping(string Column, string Field);
}

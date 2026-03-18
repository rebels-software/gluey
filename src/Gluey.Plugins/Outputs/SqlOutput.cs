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
using System.Text.RegularExpressions;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Gluey.Plugins.Outputs;

/// <summary>
/// SQL output plugin that inserts, upserts, or calls stored procedures on PostgreSQL, SQL Server, or SQLite databases.
/// Auto-detects the database dialect from the connection string.
/// </summary>
public sealed class SqlOutput : IOutputPlugin
{
    internal enum SqlDialect
    {
        PostgreSQL,
        SqlServer,
        SQLite
    }

    private string _connectionString = "";
    private string _table = "";
    private List<ColumnMapping> _columns = [];
    private List<UpsertKeyMapping> _upsertKeys = [];
    private string? _procedure;
    private List<ColumnMapping> _procedureParams = [];
    private SqlDialect _dialect;
    private bool _disposed;

    public string Type => "sql";

    /// <summary>
    /// Represents a mapping from a database column to a message field.
    /// </summary>
    internal sealed record ColumnMapping(string Column, string Field);

    /// <summary>
    /// Represents an upsert key mapping from a database column to a message field.
    /// </summary>
    internal sealed record UpsertKeyMapping(string Column, string Field);

    /// <summary>
    /// Initializes the SQL output with configuration.
    /// </summary>
    /// <param name="config">Configuration containing connection_string, table/columns or procedure/params, and optionally upsert.</param>
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

        // Check for procedure mode
        var hasProcedure = config.TryGetValue("procedure", out var procedureElement) &&
                           procedureElement.ValueKind == JsonValueKind.String;
        var hasTable = config.TryGetValue("table", out var tableElement) &&
                       tableElement.ValueKind == JsonValueKind.String;

        // Validate mutual exclusion: procedure and table cannot both be present
        if (hasProcedure && hasTable)
        {
            throw new InvalidOperationException(
                "Cannot specify both 'procedure' and 'table'. Use 'procedure' with 'params' for stored procedures, or 'table' with 'columns' for inserts/upserts.");
        }

        if (hasProcedure)
        {
            // Stored procedure mode
            if (_dialect == SqlDialect.SQLite)
            {
                throw new InvalidOperationException("Stored procedures are not supported with SQLite.");
            }

            _procedure = procedureElement.GetString() ?? "";
            if (string.IsNullOrEmpty(_procedure))
            {
                throw new InvalidOperationException("Procedure name cannot be empty.");
            }

            // Parse params (required for procedure mode)
            if (config.TryGetValue("params", out var paramsElement))
            {
                _procedureParams = ParseColumnMappings(paramsElement);
            }

            if (_procedureParams.Count == 0)
            {
                throw new InvalidOperationException(
                    "Stored procedure requires 'params' config with parameter: field pairs.");
            }
        }
        else
        {
            // Table mode (original behavior)
            if (hasTable)
            {
                _table = tableElement.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(_table))
            {
                throw new InvalidOperationException("SQL table name is required. Use 'table' config.");
            }

            // Extract column mappings (required for table mode)
            if (config.TryGetValue("columns", out var columnsElement))
            {
                _columns = ParseColumnMappings(columnsElement);
            }

            if (_columns.Count == 0)
            {
                throw new InvalidOperationException("SQL column mappings are required. Use 'columns' config with column: field pairs.");
            }

            // Extract upsert key mappings (optional, table mode only)
            if (config.TryGetValue("upsert", out var upsertElement))
            {
                _upsertKeys = ParseUpsertKeys(upsertElement);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a message to the SQL database by inserting, upserting, or calling a stored procedure.
    /// On failure, rethrows so the caller (WorkflowRunner) can log the error.
    /// </summary>
    public async Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();

            if (_procedure != null)
            {
                // Stored procedure mode
                command.CommandText = BuildProcedureCallSql();

                foreach (var mapping in _procedureParams)
                {
                    var value = GetFieldValue(message.Payload.RootElement, mapping.Field);
                    var param = command.CreateParameter();
                    param.ParameterName = GetParameterName(mapping.Column);
                    param.Value = value ?? DBNull.Value;
                    command.Parameters.Add(param);
                }
            }
            else
            {
                // Table mode (insert or upsert)
                command.CommandText = _upsertKeys.Count > 0
                    ? BuildUpsertSql()
                    : BuildInsertSql();

                foreach (var column in _columns)
                {
                    var value = GetFieldValue(message.Payload.RootElement, column.Field);
                    var param = command.CreateParameter();
                    param.ParameterName = GetParameterName(column.Column);
                    param.Value = value ?? DBNull.Value;
                    command.Parameters.Add(param);
                }
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Rethrow so the caller (WorkflowRunner) can log the error through ILogger.
            // WorkflowRunner wraps output writes in try-catch and continues gracefully.
            if (_procedure != null)
            {
                throw new InvalidOperationException(
                    $"Procedure call failed for '{_procedure}' ({_dialect}): {ex.Message}", ex);
            }
            var operation = _upsertKeys.Count > 0 ? "Upsert" : "Insert";
            throw new InvalidOperationException(
                $"{operation} failed for table '{_table}' ({_dialect}): {ex.Message}", ex);
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
    /// Returns the SQL that WriteAsync would execute. For testing purposes.
    /// </summary>
    internal string GetSqlForTesting()
    {
        if (_procedure != null)
        {
            return BuildProcedureCallSql();
        }
        return _upsertKeys.Count > 0
            ? BuildUpsertSql()
            : BuildInsertSql();
    }

    /// <summary>
    /// Returns the detected dialect. For testing purposes.
    /// </summary>
    internal SqlDialect GetDialectForTesting() => _dialect;

    /// <summary>
    /// Returns a human-readable description of the output target (procedure name or table name).
    /// </summary>
    public override string ToString() =>
        _procedure != null ? $"sql(procedure: {_procedure})" : $"sql(table: {_table})";

    /// <summary>
    /// Returns the parsed upsert keys. For testing purposes.
    /// </summary>
    internal IReadOnlyList<UpsertKeyMapping> GetUpsertKeysForTesting() => _upsertKeys;

    /// <summary>
    /// Returns the parsed procedure params. For testing purposes.
    /// </summary>
    internal IReadOnlyList<ColumnMapping> GetProcedureParamsForTesting() => _procedureParams;

    /// <summary>
    /// Detects the SQL dialect from the connection string.
    /// SQLite is detected by file-based Data Source values or Filename= prefix.
    /// </summary>
    internal static SqlDialect DetectDialect(string connectionString)
    {
        // Check for SQLite patterns first (before SQL Server, since both use Data Source=)
        if (IsSqliteConnectionString(connectionString))
        {
            return SqlDialect.SQLite;
        }

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
    /// Determines if a connection string is for SQLite.
    /// SQLite uses file-based Data Source values (ending in .db, .sqlite, .sqlite3)
    /// or the Filename= prefix.
    /// </summary>
    private static bool IsSqliteConnectionString(string connectionString)
    {
        // Filename= is SQLite-only
        if (connectionString.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for Data Source= with a file-like value
        if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the value after Data Source=
            var valueStart = "Data Source=".Length;
            var semicolonIndex = connectionString.IndexOf(';', valueStart);
            var dataSourceValue = semicolonIndex >= 0
                ? connectionString[valueStart..semicolonIndex].Trim()
                : connectionString[valueStart..].Trim();

            // SQLite in-memory databases
            if (dataSourceValue.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check for file extensions typical of SQLite
            if (dataSourceValue.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                dataSourceValue.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                dataSourceValue.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
            SqlDialect.SQLite => new SqliteConnection(_connectionString),
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
    /// Builds the upsert SQL statement based on the detected dialect.
    /// PostgreSQL/SQLite use ON CONFLICT ... DO UPDATE SET.
    /// SQL Server uses MERGE ... WHEN MATCHED ... WHEN NOT MATCHED.
    /// </summary>
    private string BuildUpsertSql()
    {
        // Get the upsert key column names
        var upsertKeyColumns = new HashSet<string>(_upsertKeys.Select(k => k.Column), StringComparer.OrdinalIgnoreCase);

        // Get columns that should be updated (all columns except upsert keys)
        var updateColumns = _columns.Where(c => !upsertKeyColumns.Contains(c.Column)).ToList();

        // If all columns are upsert keys, there's nothing to update — just do a plain INSERT
        if (updateColumns.Count == 0)
        {
            return BuildInsertSql();
        }

        return _dialect switch
        {
            SqlDialect.PostgreSQL or SqlDialect.SQLite => BuildPostgreSqlUpsertSql(upsertKeyColumns, updateColumns),
            SqlDialect.SqlServer => BuildSqlServerUpsertSql(upsertKeyColumns, updateColumns),
            _ => throw new InvalidOperationException($"Unsupported SQL dialect for upsert: {_dialect}")
        };
    }

    /// <summary>
    /// Builds the SQL to call a stored procedure based on the detected dialect.
    /// PostgreSQL: CALL "procedure_name"(@Param1, @Param2)
    /// SQL Server: EXEC [schema].[procedure_name] @Param1, @Param2
    /// </summary>
    private string BuildProcedureCallSql()
    {
        var paramList = string.Join(", ", _procedureParams.Select(p => GetParameterPlaceholder(p.Column)));

        return _dialect switch
        {
            SqlDialect.PostgreSQL => $"CALL {QuoteIdentifier(_procedure!)}({paramList})",
            SqlDialect.SqlServer => BuildSqlServerProcedureCallSql(paramList),
            _ => throw new InvalidOperationException($"Stored procedures are not supported with {_dialect}.")
        };
    }

    /// <summary>
    /// Builds SQL Server EXEC statement. Supports schema-qualified names (e.g., dbo.ProcName).
    /// </summary>
    private string BuildSqlServerProcedureCallSql(string paramList)
    {
        // Split on '.' and quote each part for schema-qualified names
        var parts = _procedure!.Split('.');
        var quotedName = string.Join(".", parts.Select(p => QuoteIdentifier(p)));
        return $"EXEC {quotedName} {paramList}";
    }

    /// <summary>
    /// Builds PostgreSQL/SQLite upsert using ON CONFLICT ... DO UPDATE SET syntax.
    /// </summary>
    private string BuildPostgreSqlUpsertSql(HashSet<string> upsertKeyColumns, List<ColumnMapping> updateColumns)
    {
        var sb = new StringBuilder();

        // INSERT INTO "table" ("col1", "col2", ...) VALUES (@col1, @col2, ...)
        var columnNames = string.Join(", ", _columns.Select(c => QuoteIdentifier(c.Column)));
        var paramNames = string.Join(", ", _columns.Select(c => GetParameterPlaceholder(c.Column)));
        sb.Append($"INSERT INTO {QuoteIdentifier(_table)} ({columnNames}) VALUES ({paramNames})");

        // ON CONFLICT ("key1", "key2")
        var conflictKeys = string.Join(", ", _upsertKeys.Select(k => QuoteIdentifier(k.Column)));
        sb.Append($" ON CONFLICT ({conflictKeys})");

        // DO UPDATE SET "col" = EXCLUDED."col", ...
        var setClauses = string.Join(", ", updateColumns.Select(c =>
            $"{QuoteIdentifier(c.Column)} = EXCLUDED.{QuoteIdentifier(c.Column)}"));
        sb.Append($" DO UPDATE SET {setClauses}");

        return sb.ToString();
    }

    /// <summary>
    /// Builds SQL Server upsert using MERGE syntax.
    /// </summary>
    private string BuildSqlServerUpsertSql(HashSet<string> upsertKeyColumns, List<ColumnMapping> updateColumns)
    {
        var sb = new StringBuilder();

        var allColumnNames = string.Join(", ", _columns.Select(c => QuoteIdentifier(c.Column)));
        var allParamNames = string.Join(", ", _columns.Select(c => GetParameterPlaceholder(c.Column)));

        // MERGE INTO [table] AS target
        sb.Append($"MERGE INTO {QuoteIdentifier(_table)} AS target");

        // USING (VALUES (@col1, @col2, ...)) AS source ([col1], [col2], ...)
        sb.Append($" USING (VALUES ({allParamNames})) AS source ({allColumnNames})");

        // ON target.[key1] = source.[key1] AND target.[key2] = source.[key2]
        var onClauses = string.Join(" AND ", _upsertKeys.Select(k =>
            $"target.{QuoteIdentifier(k.Column)} = source.{QuoteIdentifier(k.Column)}"));
        sb.Append($" ON {onClauses}");

        // WHEN MATCHED THEN UPDATE SET target.[col] = source.[col], ...
        var setClauses = string.Join(", ", updateColumns.Select(c =>
            $"target.{QuoteIdentifier(c.Column)} = source.{QuoteIdentifier(c.Column)}"));
        sb.Append($" WHEN MATCHED THEN UPDATE SET {setClauses}");

        // WHEN NOT MATCHED THEN INSERT ([col1], [col2], ...) VALUES (source.[col1], source.[col2], ...)
        var sourceValues = string.Join(", ", _columns.Select(c => $"source.{QuoteIdentifier(c.Column)}"));
        sb.Append($" WHEN NOT MATCHED THEN INSERT ({allColumnNames}) VALUES ({sourceValues});");

        return sb.ToString();
    }

    /// <summary>
    /// Quotes an identifier (table or column name) based on the SQL dialect.
    /// </summary>
    private string QuoteIdentifier(string identifier)
    {
        return _dialect switch
        {
            SqlDialect.PostgreSQL => $"\"{identifier}\"",
            SqlDialect.SQLite => $"\"{identifier}\"",
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
            SqlDialect.SQLite => $"@{column}",
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
    /// Parses upsert key mappings from the config element.
    /// Supports array format: ["column_name", { db_column: "payload_field" }, ...]
    /// String items map to same-name (column = field).
    /// Object items map first property key:value to column:field.
    /// </summary>
    private List<UpsertKeyMapping> ParseUpsertKeys(JsonElement element)
    {
        var keys = new List<UpsertKeyMapping>();

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Upsert config must be an array. Use 'upsert: [\"column\"]' format.");
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    keys.Add(new UpsertKeyMapping(name, name));
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                // First property: key = column name, value = field name
                foreach (var prop in item.EnumerateObject())
                {
                    var column = prop.Name;
                    var field = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? column
                        : column;
                    keys.Add(new UpsertKeyMapping(column, field));
                    break; // Only use the first property
                }
            }
        }

        if (keys.Count == 0)
        {
            throw new InvalidOperationException("Upsert config array must not be empty. Specify at least one key column.");
        }

        // Validate that all upsert key columns exist in the columns mapping
        var columnNames = new HashSet<string>(_columns.Select(c => c.Column), StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!columnNames.Contains(key.Column))
            {
                throw new InvalidOperationException(
                    $"Upsert key column '{key.Column}' not found in columns mapping. " +
                    $"Available columns: {string.Join(", ", columnNames)}");
            }
        }

        return keys;
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
}

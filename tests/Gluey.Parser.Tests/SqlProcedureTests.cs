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
using Gluey.Plugins.Outputs;

namespace Gluey.Parser.Tests;

/// <summary>
/// Tests for stored procedure calling support in SqlOutput.
/// </summary>
public class SqlProcedureTests
{
    private static Dictionary<string, JsonElement> BuildConfig(object config)
    {
        var json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, JsonElement>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }
        return result;
    }

    // ===== Procedure config parsing =====

    [Fact]
    public async Task ProcedureParsing_SimpleName_ParsesCorrectly()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Port=5432;Database=test;Username=user;Password=pass",
            procedure = "insert_production_result",
            @params = new { MachineId = "machine_id", Date = "date" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var procedureParams = output.GetProcedureParamsForTesting();
        Assert.Equal(2, procedureParams.Count);
        Assert.Equal("MachineId", procedureParams[0].Column);
        Assert.Equal("machine_id", procedureParams[0].Field);
        Assert.Equal("Date", procedureParams[1].Column);
        Assert.Equal("date", procedureParams[1].Field);
    }

    [Fact]
    public async Task ProcedureParsing_SchemaQualifiedName_ParsesCorrectly()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=compass;Initial Catalog=Production",
            procedure = "dbo.InsertProductionResult",
            @params = new { MachineId = "machine_id", Date = "date" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        Assert.Equal(SqlOutput.SqlDialect.SqlServer, output.GetDialectForTesting());
        var procedureParams = output.GetProcedureParamsForTesting();
        Assert.Equal(2, procedureParams.Count);
    }

    // ===== PostgreSQL CALL generation =====

    [Fact]
    public async Task PostgreSql_ProcedureCall_GeneratesCallSyntax()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=postgres;Port=5432;Database=compassbasic;Username=compass;Password=compass",
            procedure = "insert_production_result",
            @params = new { MachineId = "machine_id", Date = "date" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "CALL \"insert_production_result\"(@MachineId, @Date)",
            sql);
    }

    // ===== SQL Server EXEC generation =====

    [Fact]
    public async Task SqlServer_ProcedureCall_SimpleName_GeneratesExecSyntax()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=compass;Initial Catalog=Production",
            procedure = "InsertProductionResult",
            @params = new { MachineId = "machine_id", Date = "date" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "EXEC [InsertProductionResult] @MachineId, @Date",
            sql);
    }

    [Fact]
    public async Task SqlServer_ProcedureCall_SchemaQualified_GeneratesExecWithSchemaQuoting()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=compass;Initial Catalog=Production",
            procedure = "dbo.InsertProductionResult",
            @params = new { MachineId = "machine_id", Date = "date" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "EXEC [dbo].[InsertProductionResult] @MachineId, @Date",
            sql);
    }

    // ===== Validation errors =====

    [Fact]
    public async Task SQLite_WithProcedure_ThrowsError()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=local.db",
            procedure = "my_proc",
            @params = new { Id = "id" }
        });

        var output = new SqlOutput();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync(config));
        Assert.Contains("not supported with SQLite", ex.Message);
    }

    [Fact]
    public async Task ProcedureAndTable_MutualExclusion_ThrowsError()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            procedure = "my_proc",
            table = "my_table",
            @params = new { Id = "id" },
            columns = new { id = "id" }
        });

        var output = new SqlOutput();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync(config));
        Assert.Contains("Cannot specify both", ex.Message);
    }

    [Fact]
    public async Task ProcedureWithoutParams_ThrowsError()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Port=5432;Database=test",
            procedure = "my_proc"
        });

        var output = new SqlOutput();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync(config));
        Assert.Contains("requires 'params'", ex.Message);
    }

    // ===== Parameter binding verification =====

    [Fact]
    public async Task PostgreSql_ProcedureParams_AppearInSql()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Port=5432;Database=test;Username=user;Password=pass",
            procedure = "process_data",
            @params = new { DeviceId = "device_id", Temperature = "temp_f", Timestamp = "processed_at" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Contains("@DeviceId", sql);
        Assert.Contains("@Temperature", sql);
        Assert.Contains("@Timestamp", sql);
        Assert.StartsWith("CALL", sql);
    }

    [Fact]
    public async Task SqlServer_ProcedureParams_AppearInSql()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=myserver;Initial Catalog=mydb",
            procedure = "process_data",
            @params = new { DeviceId = "device_id", Temperature = "temp_f", Timestamp = "processed_at" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Contains("@DeviceId", sql);
        Assert.Contains("@Temperature", sql);
        Assert.Contains("@Timestamp", sql);
        Assert.StartsWith("EXEC", sql);
    }

    // ===== Backward compatibility =====

    [Fact]
    public async Task TableMode_StillWorks_NoProcedureField()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", temperature = "temp_f" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "INSERT INTO \"devices\" (\"device_id\", \"temperature\") VALUES (@device_id, @temperature)",
            sql);
        Assert.Empty(output.GetProcedureParamsForTesting());
    }

    [Fact]
    public async Task TableModeWithUpsert_StillWorks_NoProcedureField()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", temperature = "temp_f" },
            upsert = new[] { "device_id" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("DO UPDATE SET", sql);
        Assert.Empty(output.GetProcedureParamsForTesting());
    }
}

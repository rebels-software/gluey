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
/// Tests for SQL upsert support and SQLite dialect detection in SqlOutput.
/// </summary>
public class SqlUpsertTests
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

    // ===== SQLite dialect detection =====

    [Theory]
    [InlineData("Data Source=local.db", "SQLite")]
    [InlineData("Data Source=/path/to/data.sqlite", "SQLite")]
    [InlineData("Data Source=mydata.sqlite3", "SQLite")]
    [InlineData("Data Source=:memory:", "SQLite")]
    [InlineData("Data Source=local.db;Mode=ReadWriteCreate", "SQLite")]
    [InlineData("Filename=local.db", "SQLite")]
    [InlineData("Filename=mydata", "SQLite")]
    [InlineData("Data Source=myserver;Initial Catalog=mydb", "SqlServer")]
    [InlineData("Data Source=myserver", "SqlServer")]
    [InlineData("Host=postgres;Port=5432;Database=iot", "PostgreSQL")]
    [InlineData("postgresql://user:pass@localhost/mydb", "PostgreSQL")]
    public void DetectDialect_CorrectlyIdentifiesDialect(string connectionString, string expectedDialect)
    {
        var dialect = SqlOutput.DetectDialect(connectionString);
        Assert.Equal(Enum.Parse<SqlOutput.SqlDialect>(expectedDialect), dialect);
    }

    // ===== Upsert config parsing =====

    [Fact]
    public async Task UpsertParsing_StringAndObjectAndMixed_ParsesCorrectly()
    {
        // Test mixed: ["device_id", { db_region: "device_region" }]
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", db_region = "device_region", temperature = "temp_f" },
            upsert = new object[] { "device_id", new { db_region = "device_region" } }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var keys = output.GetUpsertKeysForTesting();
        Assert.Equal(2, keys.Count);
        Assert.Equal("device_id", keys[0].Column);
        Assert.Equal("device_id", keys[0].Field);   // string -> same-name
        Assert.Equal("db_region", keys[1].Column);
        Assert.Equal("device_region", keys[1].Field); // object -> different-name
    }

    [Fact]
    public async Task UpsertParsing_EmptyArray_Throws()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id" },
            upsert = Array.Empty<string>()
        });

        var output = new SqlOutput();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync(config));
        Assert.Contains("must not be empty", ex.Message);
    }

    [Fact]
    public async Task UpsertParsing_KeyNotInColumns_Throws()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { temperature = "temp_f" },
            upsert = new[] { "device_id" }
        });

        var output = new SqlOutput();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => output.InitializeAsync(config));
        Assert.Contains("device_id", ex.Message);
        Assert.Contains("not found in columns", ex.Message);
    }

    // ===== Upsert SQL generation per dialect =====

    [Fact]
    public async Task PostgreSqlUpsert_SingleKey_GeneratesOnConflict()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", temperature = "temp_f", last_seen = "processed_at" },
            upsert = new[] { "device_id" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "INSERT INTO \"devices\" (\"device_id\", \"temperature\", \"last_seen\") " +
            "VALUES (@device_id, @temperature, @last_seen) " +
            "ON CONFLICT (\"device_id\") " +
            "DO UPDATE SET \"temperature\" = EXCLUDED.\"temperature\", \"last_seen\" = EXCLUDED.\"last_seen\"",
            sql);
    }

    [Fact]
    public async Task PostgreSqlUpsert_CompositeKey_ExcludesKeysFromUpdateSet()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", region = "region", temperature = "temp_f" },
            upsert = new[] { "device_id", "region" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Contains("ON CONFLICT (\"device_id\", \"region\")", sql);
        Assert.Contains("DO UPDATE SET \"temperature\" = EXCLUDED.\"temperature\"", sql);
        // Keys must NOT appear in the UPDATE SET clause
        Assert.DoesNotContain("EXCLUDED.\"device_id\"", sql);
        Assert.DoesNotContain("EXCLUDED.\"region\"", sql);
    }

    [Fact]
    public async Task SQLiteUpsert_UsesSameSyntaxAsPostgreSQL()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=local.db",
            table = "devices",
            columns = new { device_id = "device_id", temperature = "temp_f" },
            upsert = new[] { "device_id" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        Assert.Equal(SqlOutput.SqlDialect.SQLite, output.GetDialectForTesting());
        var sql = output.GetSqlForTesting();
        Assert.Contains("ON CONFLICT (\"device_id\")", sql);
        Assert.Contains("DO UPDATE SET \"temperature\" = EXCLUDED.\"temperature\"", sql);
    }

    [Fact]
    public async Task SqlServerUpsert_SingleKey_GeneratesMerge()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=myserver;Initial Catalog=mydb",
            table = "devices",
            columns = new { device_id = "device_id", temperature = "temp_f", last_seen = "processed_at" },
            upsert = new[] { "device_id" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Equal(
            "MERGE INTO [devices] AS target " +
            "USING (VALUES (@device_id, @temperature, @last_seen)) AS source ([device_id], [temperature], [last_seen]) " +
            "ON target.[device_id] = source.[device_id] " +
            "WHEN MATCHED THEN UPDATE SET target.[temperature] = source.[temperature], target.[last_seen] = source.[last_seen] " +
            "WHEN NOT MATCHED THEN INSERT ([device_id], [temperature], [last_seen]) VALUES (source.[device_id], source.[temperature], source.[last_seen]);",
            sql);
    }

    [Fact]
    public async Task SqlServerUpsert_CompositeKey_JoinsWithAnd()
    {
        var config = BuildConfig(new
        {
            connection_string = "Data Source=myserver;Initial Catalog=mydb",
            table = "devices",
            columns = new { device_id = "device_id", region = "region", temperature = "temp_f" },
            upsert = new[] { "device_id", "region" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        Assert.Contains("ON target.[device_id] = source.[device_id] AND target.[region] = source.[region]", sql);
        // Keys excluded from UPDATE SET
        Assert.DoesNotContain("target.[device_id] = source.[device_id],", sql);
    }

    // ===== Edge cases =====

    [Fact]
    public async Task NoUpsert_GeneratesPureInsert_BackwardCompatible()
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
        Assert.Empty(output.GetUpsertKeysForTesting());
    }

    [Fact]
    public async Task AllColumnsAreUpsertKeys_FallsBackToPlainInsert()
    {
        var config = BuildConfig(new
        {
            connection_string = "Host=localhost;Database=test",
            table = "devices",
            columns = new { device_id = "device_id", region = "region" },
            upsert = new[] { "device_id", "region" }
        });

        var output = new SqlOutput();
        await output.InitializeAsync(config);

        var sql = output.GetSqlForTesting();
        // Nothing to UPDATE, so just plain INSERT
        Assert.DoesNotContain("ON CONFLICT", sql);
        Assert.DoesNotContain("MERGE", sql);
        Assert.StartsWith("INSERT INTO", sql);
    }
}

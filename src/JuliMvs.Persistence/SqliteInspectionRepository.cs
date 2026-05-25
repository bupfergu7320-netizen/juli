using JuliMvs.Core.Inspection;
using JuliMvs.Core.Persistence;
using JuliMvs.Core.Vision;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JuliMvs.Persistence;

public sealed class SqliteInspectionRepository : IInspectionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly string _connectionString;

    public SqliteInspectionRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Templates (
                Id TEXT PRIMARY KEY,
                BatchNo TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                ImagePath TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ReferenceCenterXPixel REAL NOT NULL,
                ReferenceCenterYPixel REAL NOT NULL,
                ReferenceCenterXMm REAL NOT NULL,
                ReferenceCenterYMm REAL NOT NULL,
                SourceCameraCalibrationId TEXT NOT NULL,
                SourceDistortionCalibrationId TEXT NOT NULL,
                ReferenceAngleDegrees REAL NOT NULL,
                ReferenceWidthPixels REAL NOT NULL DEFAULT 0.0,
                ReferenceHeightPixels REAL NOT NULL DEFAULT 0.0,
                WidthMm REAL NOT NULL,
                HeightMm REAL NOT NULL,
                AreaPixels REAL NOT NULL,
                MatchScoreBaseline REAL NOT NULL,
                ParametersJson TEXT NULL
            );
            """, cancellationToken);
        await MigrateTemplatesTableAsync(connection, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS InspectionResults (
                Id TEXT PRIMARY KEY,
                BatchNo TEXT NOT NULL,
                PartNo TEXT NOT NULL,
                Decision TEXT NOT NULL,
                NgReason TEXT NOT NULL,
                Message TEXT NOT NULL,
                RawImagePath TEXT NULL,
                ResultImagePath TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CenterXPixel REAL NULL,
                CenterYPixel REAL NULL,
                XOffsetMm REAL NULL,
                YOffsetMm REAL NULL,
                XCompensationMm REAL NULL,
                YCompensationMm REAL NULL,
                AngleDegrees REAL NULL,
                AngleOffsetDegrees REAL NULL,
                RotationCompensationDegrees REAL NULL,
                WidthMm REAL NULL,
                HeightMm REAL NULL,
                AreaPixels REAL NULL,
                MatchScore REAL NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ProductRecipes (
                ProductName TEXT PRIMARY KEY,
                UpdatedAt TEXT NOT NULL,
                Json TEXT NOT NULL
            );
            """, cancellationToken);
        await EnsureUniqueProductTemplateRowsAsync(connection, cancellationToken);
        await ExecuteAsync(
            connection,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Templates_ProductName_Unique ON Templates(ProductName);",
            cancellationToken);
    }

    public async Task SaveTemplateAsync(PartTemplate template, CancellationToken cancellationToken = default)
    {
        ValidateTemplateForSave(template);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Templates (
                Id, BatchNo, ProductName, ImagePath, CreatedAt,
                ReferenceCenterXPixel, ReferenceCenterYPixel, ReferenceCenterXMm, ReferenceCenterYMm,
                SourceCameraCalibrationId, SourceDistortionCalibrationId, ReferenceAngleDegrees,
                ReferenceWidthPixels, ReferenceHeightPixels,
                WidthMm, HeightMm, AreaPixels, MatchScoreBaseline, ParametersJson
            ) VALUES (
                $Id, $BatchNo, $ProductName, $ImagePath, $CreatedAt,
                $ReferenceCenterXPixel, $ReferenceCenterYPixel, $ReferenceCenterXMm, $ReferenceCenterYMm,
                $SourceCameraCalibrationId, $SourceDistortionCalibrationId, $ReferenceAngleDegrees,
                $ReferenceWidthPixels, $ReferenceHeightPixels,
                $WidthMm, $HeightMm, $AreaPixels, $MatchScoreBaseline, $ParametersJson
            )
            ON CONFLICT(ProductName) DO UPDATE SET
                Id = excluded.Id,
                BatchNo = excluded.BatchNo,
                ImagePath = excluded.ImagePath,
                CreatedAt = excluded.CreatedAt,
                ReferenceCenterXPixel = excluded.ReferenceCenterXPixel,
                ReferenceCenterYPixel = excluded.ReferenceCenterYPixel,
                ReferenceCenterXMm = excluded.ReferenceCenterXMm,
                ReferenceCenterYMm = excluded.ReferenceCenterYMm,
                SourceCameraCalibrationId = excluded.SourceCameraCalibrationId,
                SourceDistortionCalibrationId = excluded.SourceDistortionCalibrationId,
                ReferenceAngleDegrees = excluded.ReferenceAngleDegrees,
                ReferenceWidthPixels = excluded.ReferenceWidthPixels,
                ReferenceHeightPixels = excluded.ReferenceHeightPixels,
                WidthMm = excluded.WidthMm,
                HeightMm = excluded.HeightMm,
                AreaPixels = excluded.AreaPixels,
                MatchScoreBaseline = excluded.MatchScoreBaseline,
                ParametersJson = excluded.ParametersJson;
            """;
        command.Parameters.AddWithValue("$Id", template.Id.ToString());
        command.Parameters.AddWithValue("$BatchNo", template.BatchNo);
        command.Parameters.AddWithValue("$ProductName", template.ProductName);
        command.Parameters.AddWithValue("$ImagePath", (object?)template.ImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedAt", template.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$ReferenceCenterXPixel", template.ReferenceCenterXPixel);
        command.Parameters.AddWithValue("$ReferenceCenterYPixel", template.ReferenceCenterYPixel);
        command.Parameters.AddWithValue("$ReferenceCenterXMm", template.ReferenceCenterXMm);
        command.Parameters.AddWithValue("$ReferenceCenterYMm", template.ReferenceCenterYMm);
        command.Parameters.AddWithValue("$SourceCameraCalibrationId", template.SourceCameraCalibrationId);
        command.Parameters.AddWithValue("$SourceDistortionCalibrationId", template.SourceDistortionCalibrationId);
        command.Parameters.AddWithValue("$ReferenceAngleDegrees", template.ReferenceAngleDegrees);
        command.Parameters.AddWithValue("$ReferenceWidthPixels", template.ReferenceWidthPixels);
        command.Parameters.AddWithValue("$ReferenceHeightPixels", template.ReferenceHeightPixels);
        command.Parameters.AddWithValue("$WidthMm", template.WidthMm);
        command.Parameters.AddWithValue("$HeightMm", template.HeightMm);
        command.Parameters.AddWithValue("$AreaPixels", template.AreaPixels);
        command.Parameters.AddWithValue("$MatchScoreBaseline", template.MatchScoreBaseline);
        command.Parameters.AddWithValue("$ParametersJson", JsonSerializer.Serialize(template.Parameters, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PartTemplate?> LoadLatestTemplateAsync(
        string productName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BatchNo, ProductName, ImagePath, CreatedAt,
                   ReferenceCenterXPixel, ReferenceCenterYPixel, ReferenceCenterXMm, ReferenceCenterYMm,
                   SourceCameraCalibrationId, SourceDistortionCalibrationId,
                   ReferenceAngleDegrees, ReferenceWidthPixels, ReferenceHeightPixels,
                   WidthMm, HeightMm, AreaPixels, MatchScoreBaseline, ParametersJson
            FROM Templates
            WHERE ProductName = $ProductName
            ORDER BY CreatedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$ProductName", productName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadTemplate(reader);
    }

    public async Task<IReadOnlyList<PartTemplate>> LoadTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BatchNo, ProductName, ImagePath, CreatedAt,
                   ReferenceCenterXPixel, ReferenceCenterYPixel, ReferenceCenterXMm, ReferenceCenterYMm,
                   SourceCameraCalibrationId, SourceDistortionCalibrationId,
                   ReferenceAngleDegrees, ReferenceWidthPixels, ReferenceHeightPixels,
                   WidthMm, HeightMm, AreaPixels, MatchScoreBaseline, ParametersJson
            FROM Templates
            ORDER BY ProductName COLLATE NOCASE ASC;
            """;

        var templates = new List<PartTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task SaveResultAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO InspectionResults (
                Id, BatchNo, PartNo, Decision, NgReason, Message, RawImagePath, ResultImagePath, CreatedAt,
                CenterXPixel, CenterYPixel, XOffsetMm, YOffsetMm, XCompensationMm, YCompensationMm,
                AngleDegrees, AngleOffsetDegrees, RotationCompensationDegrees, WidthMm, HeightMm, AreaPixels, MatchScore
            ) VALUES (
                $Id, $BatchNo, $PartNo, $Decision, $NgReason, $Message, $RawImagePath, $ResultImagePath, $CreatedAt,
                $CenterXPixel, $CenterYPixel, $XOffsetMm, $YOffsetMm, $XCompensationMm, $YCompensationMm,
                $AngleDegrees, $AngleOffsetDegrees, $RotationCompensationDegrees, $WidthMm, $HeightMm, $AreaPixels, $MatchScore
            );
            """;

        command.Parameters.AddWithValue("$Id", result.Id.ToString());
        command.Parameters.AddWithValue("$BatchNo", result.BatchNo);
        command.Parameters.AddWithValue("$PartNo", result.PartNo);
        command.Parameters.AddWithValue("$Decision", result.Decision.ToString());
        command.Parameters.AddWithValue("$NgReason", result.NgReason.ToString());
        command.Parameters.AddWithValue("$Message", result.Message);
        command.Parameters.AddWithValue("$RawImagePath", (object?)result.RawImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$ResultImagePath", (object?)result.ResultImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$CreatedAt", result.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$CenterXPixel", (object?)result.Measurement?.CenterXPixel ?? DBNull.Value);
        command.Parameters.AddWithValue("$CenterYPixel", (object?)result.Measurement?.CenterYPixel ?? DBNull.Value);
        command.Parameters.AddWithValue("$XOffsetMm", (object?)result.Measurement?.XOffsetMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$YOffsetMm", (object?)result.Measurement?.YOffsetMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$XCompensationMm", (object?)result.Measurement?.XCompensationMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$YCompensationMm", (object?)result.Measurement?.YCompensationMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$AngleDegrees", (object?)result.Measurement?.AngleDegrees ?? DBNull.Value);
        command.Parameters.AddWithValue("$AngleOffsetDegrees", (object?)result.Measurement?.AngleOffsetDegrees ?? DBNull.Value);
        command.Parameters.AddWithValue("$RotationCompensationDegrees", (object?)result.Measurement?.RotationCompensationDegrees ?? DBNull.Value);
        command.Parameters.AddWithValue("$WidthMm", (object?)result.Measurement?.WidthMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$HeightMm", (object?)result.Measurement?.HeightMm ?? DBNull.Value);
        command.Parameters.AddWithValue("$AreaPixels", (object?)result.Measurement?.AreaPixels ?? DBNull.Value);
        command.Parameters.AddWithValue("$MatchScore", (object?)result.Measurement?.MatchScore ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveProductRecipeAsync(
        string productName,
        ProductRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO ProductRecipes (
                ProductName, UpdatedAt, Json
            ) VALUES (
                $ProductName, $UpdatedAt, $Json
            );
            """;
        command.Parameters.AddWithValue("$ProductName", productName.Trim());
        command.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$Json", JsonSerializer.Serialize(recipe, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProductRecipe?> LoadProductRecipeAsync(
        string productName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Json
            FROM ProductRecipes
            WHERE ProductName = $ProductName;
            """;
        command.Parameters.AddWithValue("$ProductName", productName.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not string json)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProductRecipe>(json, JsonOptions);
    }

    private static void ValidateTemplateForSave(PartTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.SourceCameraCalibrationId))
        {
            throw new InvalidOperationException("模板缺少9点XY标定来源，不能保存。请先完成9点XY标定。");
        }

    }

    private static async Task MigrateTemplatesTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var expectedColumns = new[]
        {
            "Id",
            "BatchNo",
            "ProductName",
            "ImagePath",
            "CreatedAt",
            "ReferenceCenterXPixel",
            "ReferenceCenterYPixel",
            "ReferenceCenterXMm",
            "ReferenceCenterYMm",
            "SourceCameraCalibrationId",
            "SourceDistortionCalibrationId",
            "ReferenceAngleDegrees",
            "ReferenceWidthPixels",
            "ReferenceHeightPixels",
            "WidthMm",
            "HeightMm",
            "AreaPixels",
            "MatchScoreBaseline",
            "ParametersJson"
        };

        var currentColumns = await ReadTableColumnsAsync(connection, "Templates", cancellationToken);
        if (currentColumns.SequenceEqual(expectedColumns, StringComparer.Ordinal))
        {
            return;
        }

        await ExecuteAsync(connection, "DROP TABLE IF EXISTS Templates_Migrated;", cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE Templates_Migrated (
                Id TEXT PRIMARY KEY,
                BatchNo TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                ImagePath TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ReferenceCenterXPixel REAL NOT NULL,
                ReferenceCenterYPixel REAL NOT NULL,
                ReferenceCenterXMm REAL NOT NULL,
                ReferenceCenterYMm REAL NOT NULL,
                SourceCameraCalibrationId TEXT NOT NULL,
                SourceDistortionCalibrationId TEXT NOT NULL,
                ReferenceAngleDegrees REAL NOT NULL,
                ReferenceWidthPixels REAL NOT NULL,
                ReferenceHeightPixels REAL NOT NULL,
                WidthMm REAL NOT NULL,
                HeightMm REAL NOT NULL,
                AreaPixels REAL NOT NULL,
                MatchScoreBaseline REAL NOT NULL,
                ParametersJson TEXT NULL
            );
            """, cancellationToken);

        var currentColumnSet = currentColumns.ToHashSet(StringComparer.Ordinal);
        var columnList = string.Join(", ", expectedColumns);
        var selectList = string.Join(", ", expectedColumns.Select(column => BuildTemplatesMigrationExpression(column, currentColumnSet)));
        await ExecuteAsync(
            connection,
            $"INSERT INTO Templates_Migrated ({columnList}) SELECT {selectList} FROM Templates;",
            cancellationToken);
        await ExecuteAsync(connection, "DROP TABLE Templates;", cancellationToken);
        await ExecuteAsync(connection, "ALTER TABLE Templates_Migrated RENAME TO Templates;", cancellationToken);
    }

    private static async Task EnsureUniqueProductTemplateRowsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            DELETE FROM Templates
            WHERE rowid NOT IN (
                SELECT keep.RowIdToKeep
                FROM (
                    SELECT ProductName, rowid AS RowIdToKeep
                    FROM Templates AS outerTemplate
                    WHERE rowid = (
                        SELECT rowid
                        FROM Templates AS innerTemplate
                        WHERE innerTemplate.ProductName = outerTemplate.ProductName
                        ORDER BY datetime(CreatedAt) DESC, CreatedAt DESC, rowid DESC
                        LIMIT 1
                    )
                ) AS keep
            );
            """, cancellationToken);
    }

    private static PartTemplate ReadTemplate(SqliteDataReader reader)
    {
        return new PartTemplate(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.GetDouble(5),
            reader.GetDouble(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetDouble(11),
            reader.GetDouble(14),
            reader.GetDouble(15),
            reader.GetDouble(16),
            reader.GetDouble(17),
            ImageRoi.Empty,
            reader.IsDBNull(18)
                ? VisionParameters.Default
                : JsonSerializer.Deserialize<VisionParameters>(reader.GetString(18), JsonOptions) ?? VisionParameters.Default,
            reader.GetDouble(12),
            reader.GetDouble(13));
    }

    private static string BuildTemplatesMigrationExpression(string column, ISet<string> currentColumns)
    {
        if (currentColumns.Contains(column))
        {
            return column switch
            {
                "Id" => "COALESCE(Id, lower(hex(randomblob(16))))",
                "BatchNo" => "COALESCE(BatchNo, 'UNKNOWN')",
                "ProductName" => "COALESCE(ProductName, 'UNKNOWN')",
                "CreatedAt" => "COALESCE(CreatedAt, datetime('now'))",
                "ReferenceCenterXPixel" => "COALESCE(ReferenceCenterXPixel, 0.0)",
                "ReferenceCenterYPixel" => "COALESCE(ReferenceCenterYPixel, 0.0)",
                "ReferenceCenterXMm" => "COALESCE(ReferenceCenterXMm, 0.0)",
                "ReferenceCenterYMm" => "COALESCE(ReferenceCenterYMm, 0.0)",
                "SourceCameraCalibrationId" => "COALESCE(SourceCameraCalibrationId, '')",
                "SourceDistortionCalibrationId" => "COALESCE(SourceDistortionCalibrationId, '')",
                "ReferenceAngleDegrees" => "COALESCE(ReferenceAngleDegrees, 0.0)",
                "ReferenceWidthPixels" => "COALESCE(ReferenceWidthPixels, 0.0)",
                "ReferenceHeightPixels" => "COALESCE(ReferenceHeightPixels, 0.0)",
                "WidthMm" => "COALESCE(WidthMm, 0.0)",
                "HeightMm" => "COALESCE(HeightMm, 0.0)",
                "AreaPixels" => "COALESCE(AreaPixels, 0.0)",
                "MatchScoreBaseline" => "COALESCE(MatchScoreBaseline, 1.0)",
                "ParametersJson" => "ParametersJson",
                _ => column
            };
        }

        return column switch
        {
            "Id" => "lower(hex(randomblob(16)))",
            "BatchNo" => "'UNKNOWN'",
            "ProductName" => "'UNKNOWN'",
            "ImagePath" => "NULL",
            "CreatedAt" => "datetime('now')",
            "ReferenceCenterXPixel" => "0.0",
            "ReferenceCenterYPixel" => "0.0",
            "ReferenceCenterXMm" => "0.0",
            "ReferenceCenterYMm" => "0.0",
            "SourceCameraCalibrationId" => "''",
            "SourceDistortionCalibrationId" => "''",
            "ReferenceAngleDegrees" => "0.0",
            "ReferenceWidthPixels" => "0.0",
            "ReferenceHeightPixels" => "0.0",
            "WidthMm" => "0.0",
            "HeightMm" => "0.0",
            "AreaPixels" => "0.0",
            "MatchScoreBaseline" => "1.0",
            "ParametersJson" => "NULL",
            _ => throw new InvalidOperationException($"不支持的模板表迁移字段：{column}。")
        };
    }

    private static async Task<IReadOnlyList<string>> ReadTableColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}

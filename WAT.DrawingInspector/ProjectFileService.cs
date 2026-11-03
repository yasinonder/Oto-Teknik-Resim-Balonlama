using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace WAT.DrawingInspector;

public sealed record StoredBalloon(int Number, double X, double Y);

public sealed record InspectionProjectData(
    string SourceFileName,
    string SourceExtension,
    byte[] SourceBytes,
    int CurrentPageIndex,
    Dictionary<int, List<StoredBalloon>> BalloonsByPage,
    Dictionary<int, int> NextNumbersByPage);

public static class ProjectFileService
{
    private const int CurrentSchemaVersion = 1;
    private const long MaximumEmbeddedDrawingBytes = 1024L * 1024L * 1024L;

    public static void Save(
        string projectPath,
        string drawingPath,
        string sourceFileName,
        int currentPageIndex,
        IReadOnlyDictionary<int, List<StoredBalloon>> balloonsByPage,
        IReadOnlyDictionary<int, int> nextNumbersByPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(drawingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        var drawingInfo = new FileInfo(drawingPath);
        if (!drawingInfo.Exists)
            throw new FileNotFoundException("Kaynak teknik resim dosyası bulunamadı.", drawingPath);
        if (drawingInfo.Length > MaximumEmbeddedDrawingBytes)
            throw new InvalidOperationException("1 GB üzerindeki çizimler bu proje biçimine gömülemiyor.");

        var sourceBytes = File.ReadAllBytes(drawingPath);
        var fullProjectPath = Path.GetFullPath(projectPath);
        var directory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Proje kayıt klasörü belirlenemedi.");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullProjectPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString();

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = """
                        PRAGMA journal_mode = DELETE;
                        PRAGMA foreign_keys = ON;
                        PRAGMA synchronous = FULL;
                        PRAGMA user_version = 1;

                        CREATE TABLE ProjectMetadata (
                            Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                            SchemaVersion INTEGER NOT NULL,
                            SourceFileName TEXT NOT NULL,
                            SourceExtension TEXT NOT NULL,
                            SourceData BLOB NOT NULL,
                            CurrentPageIndex INTEGER NOT NULL,
                            SavedUtc TEXT NOT NULL
                        );

                        CREATE TABLE BalloonAnnotations (
                            PageIndex INTEGER NOT NULL,
                            Number INTEGER NOT NULL,
                            X REAL NOT NULL,
                            Y REAL NOT NULL,
                            PRIMARY KEY (PageIndex, Number)
                        );

                        CREATE TABLE PageSequences (
                            PageIndex INTEGER NOT NULL PRIMARY KEY,
                            NextNumber INTEGER NOT NULL
                        );
                        """;
                    command.ExecuteNonQuery();
                }

                using var transaction = connection.BeginTransaction();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO ProjectMetadata
                            (Id, SchemaVersion, SourceFileName, SourceExtension, SourceData, CurrentPageIndex, SavedUtc)
                        VALUES
                            (1, $schema, $name, $extension, $data, $page, $saved);
                        """;
                    command.Parameters.AddWithValue("$schema", CurrentSchemaVersion);
                    command.Parameters.AddWithValue("$name", Path.GetFileName(sourceFileName));
                    command.Parameters.AddWithValue("$extension", Path.GetExtension(sourceFileName).ToLowerInvariant());
                    command.Parameters.Add("$data", SqliteType.Blob).Value = sourceBytes;
                    command.Parameters.AddWithValue("$page", Math.Max(0, currentPageIndex));
                    command.Parameters.AddWithValue("$saved", DateTimeOffset.UtcNow.ToString("O"));
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO BalloonAnnotations (PageIndex, Number, X, Y)
                        VALUES ($page, $number, $x, $y);
                        """;
                    var pageParameter = command.Parameters.Add("$page", SqliteType.Integer);
                    var numberParameter = command.Parameters.Add("$number", SqliteType.Integer);
                    var xParameter = command.Parameters.Add("$x", SqliteType.Real);
                    var yParameter = command.Parameters.Add("$y", SqliteType.Real);

                    foreach (var page in balloonsByPage.OrderBy(item => item.Key))
                    {
                        foreach (var balloon in page.Value.OrderBy(item => item.Number))
                        {
                            pageParameter.Value = page.Key;
                            numberParameter.Value = balloon.Number;
                            xParameter.Value = balloon.X;
                            yParameter.Value = balloon.Y;
                            command.ExecuteNonQuery();
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO PageSequences (PageIndex, NextNumber)
                        VALUES ($page, $next);
                        """;
                    var pageParameter = command.Parameters.Add("$page", SqliteType.Integer);
                    var nextParameter = command.Parameters.Add("$next", SqliteType.Integer);

                    foreach (var page in nextNumbersByPage.OrderBy(item => item.Key))
                    {
                        pageParameter.Value = page.Key;
                        nextParameter.Value = Math.Max(1, page.Value);
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            File.Move(temporaryPath, fullProjectPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    public static InspectionProjectData Load(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException("SQLite proje dosyası bulunamadı.", projectPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(projectPath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var integrityResult = Convert.ToString(integrity.ExecuteScalar());
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SQLite proje dosyası bütünlük kontrolünden geçemedi.");
        }

        int schemaVersion;
        string sourceFileName;
        string sourceExtension;
        byte[] sourceBytes;
        int currentPageIndex;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT SchemaVersion, SourceFileName, SourceExtension, SourceData, CurrentPageIndex
                FROM ProjectMetadata
                WHERE Id = 1;
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new InvalidDataException("Proje bilgileri SQLite dosyasında bulunamadı.");

            schemaVersion = reader.GetInt32(0);
            sourceFileName = reader.GetString(1);
            sourceExtension = reader.GetString(2);
            sourceBytes = (byte[])reader["SourceData"];
            currentPageIndex = Math.Max(0, reader.GetInt32(4));
        }

        if (schemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Desteklenmeyen proje şema sürümü: {schemaVersion}.");

        var balloonsByPage = new Dictionary<int, List<StoredBalloon>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT PageIndex, Number, X, Y
                FROM BalloonAnnotations
                ORDER BY PageIndex, Number;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var page = reader.GetInt32(0);
                if (!balloonsByPage.TryGetValue(page, out var list))
                {
                    list = new List<StoredBalloon>();
                    balloonsByPage.Add(page, list);
                }

                list.Add(new StoredBalloon(
                    reader.GetInt32(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3)));
            }
        }

        var nextNumbersByPage = new Dictionary<int, int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT PageIndex, NextNumber FROM PageSequences;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                nextNumbersByPage[reader.GetInt32(0)] = Math.Max(1, reader.GetInt32(1));
        }

        return new InspectionProjectData(
            Path.GetFileName(sourceFileName),
            sourceExtension,
            sourceBytes,
            currentPageIndex,
            balloonsByPage,
            nextNumbersByPage);
    }
}
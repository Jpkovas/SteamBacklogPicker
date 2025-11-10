using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EpicDiscovery.Tests.Fixtures;

/// <summary>
/// Builds a catalog cache SQLite database tailored for <see cref="EpicCatalogCacheTests"/>.
/// Add new setters to <see cref="CatalogItemBuilder"/> and include their columns inside
/// <see cref="CreateSchema(SqliteConnection)"/> plus <see cref="InsertRows(SqliteConnection)"/>
/// whenever the production parser starts reading extra fields or tables.
/// This keeps fixtures easy to extend without re-encoding binary blobs.
/// </summary>
public sealed class SqliteCatalogFixtureBuilder
{
    private readonly List<CatalogItemRow> rows = new();
    private string fileName = "catalog_cache.sqlite";
    private string tableName = "catalog_items";

    public SqliteCatalogFixtureBuilder WithFileName(string name)
    {
        fileName = name;
        return this;
    }

    public SqliteCatalogFixtureBuilder WithTableName(string name)
    {
        tableName = name;
        return this;
    }

    public SqliteCatalogFixtureBuilder AddCatalogItem(Action<CatalogItemBuilder> configure)
    {
        var builder = new CatalogItemBuilder();
        configure(builder);
        rows.Add(builder.Build());
        return this;
    }

    public string Build(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        CreateSchema(connection);
        InsertRows(connection);

        return path;
    }

    private void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $@"CREATE TABLE IF NOT EXISTS \"{tableName}\" (
            CatalogItemId TEXT,
            CatalogNamespace TEXT,
            AppName TEXT,
            Title TEXT,
            Tags TEXT,
            KeyImages TEXT,
            InstallSize INTEGER,
            LastModified TEXT
        );";
        command.ExecuteNonQuery();
    }

    private void InsertRows(SqliteConnection connection)
    {
        foreach (var row in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"INSERT INTO \"{tableName}\" (
                CatalogItemId,
                CatalogNamespace,
                AppName,
                Title,
                Tags,
                KeyImages,
                InstallSize,
                LastModified
            ) VALUES (
                @CatalogItemId,
                @CatalogNamespace,
                @AppName,
                @Title,
                @Tags,
                @KeyImages,
                @InstallSize,
                @LastModified
            );";

            command.Parameters.AddWithValue("@CatalogItemId", (object?)row.CatalogItemId ?? DBNull.Value);
            command.Parameters.AddWithValue("@CatalogNamespace", (object?)row.CatalogNamespace ?? DBNull.Value);
            command.Parameters.AddWithValue("@AppName", (object?)row.AppName ?? DBNull.Value);
            command.Parameters.AddWithValue("@Title", (object?)row.Title ?? DBNull.Value);
            command.Parameters.AddWithValue("@Tags", (object?)row.TagsJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@KeyImages", (object?)row.KeyImagesJson ?? DBNull.Value);
            command.Parameters.AddWithValue("@InstallSize", row.InstallSize.HasValue ? row.InstallSize.Value : DBNull.Value);
            command.Parameters.AddWithValue("@LastModified", (object?)row.LastModifiedText ?? DBNull.Value);

            command.ExecuteNonQuery();
        }
    }

    private sealed record CatalogItemRow(
        string? CatalogItemId,
        string? CatalogNamespace,
        string? AppName,
        string? Title,
        string? TagsJson,
        string? KeyImagesJson,
        long? InstallSize,
        string? LastModifiedText);

    public sealed class CatalogItemBuilder
    {
        private string? catalogItemId;
        private string? catalogNamespace;
        private string? appName;
        private string? title;
        private readonly HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyImage> keyImages = new();
        private long? installSize;
        private DateTimeOffset? lastModified;

        public CatalogItemBuilder WithIdentifiers(string? itemId, string? @namespace, string? app)
        {
            catalogItemId = itemId;
            catalogNamespace = @namespace;
            appName = app;
            return this;
        }

        public CatalogItemBuilder WithTitle(string? value)
        {
            title = value;
            return this;
        }

        public CatalogItemBuilder AddTag(string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                tags.Add(tag);
            }

            return this;
        }

        public CatalogItemBuilder AddTags(params string[] values)
        {
            foreach (var value in values)
            {
                AddTag(value);
            }

            return this;
        }

        public CatalogItemBuilder AddKeyImage(string type, string? uri = null, string? path = null)
        {
            keyImages.Add(new KeyImage(type, uri, path));
            return this;
        }

        public CatalogItemBuilder WithInstallSize(long? value)
        {
            installSize = value;
            return this;
        }

        public CatalogItemBuilder WithLastModified(DateTimeOffset? value)
        {
            lastModified = value;
            return this;
        }

        internal CatalogItemRow Build()
        {
            var tagArray = tags.Count == 0 ? null : JsonSerializer.Serialize(tags.OrderBy(tag => tag));
            var keyImagesJson = keyImages.Count == 0 ? null : JsonSerializer.Serialize(new
            {
                keyImages = keyImages.Select(image => new
                {
                    type = image.Type,
                    url = image.Uri,
                    uri = image.Uri,
                    path = image.Path
                })
            });

            return new CatalogItemRow(
                catalogItemId,
                catalogNamespace,
                appName,
                title,
                tagArray,
                keyImagesJson,
                installSize,
                lastModified?.ToString("O"));
        }

        private sealed record KeyImage(string Type, string? Uri, string? Path);
    }
}

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BookmarksBase.Storage;

public class BookmarksBaseStorageService : IDisposable
{
    public IReadOnlyCollection<Bookmark> LoadedBookmarks { get; private set; }
    public DateTime LastModifiedOn { get; }

    public enum CompatibilityLevel
    {
        Unspecified,
        V2WithoutFolderNames,
        V3WithFolderNames
    }

    public CompatibilityLevel CompatLevel { get; } = CompatibilityLevel.Unspecified;

    private readonly object _lock;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqliteConnection _sqliteConnectionInMemory;
    private bool disposedValue;
    private SqliteTransaction _transaction;

    private SqliteConnection GetConnection() => _sqliteConnectionInMemory ?? _sqliteConnection;

    public BookmarksBaseStorageService(OperationMode op, string databaseFileName, bool inMemoryMode)
    {
        if (op == OperationMode.Reading)
        {
            LastModifiedOn = File.GetLastWriteTime(databaseFileName);

            var sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder
            {
                Mode = SqliteOpenMode.ReadOnly,
                DataSource = databaseFileName
            };

            _sqliteConnection = new SqliteConnection(sqliteConnectionStringBuilder.ConnectionString);
            _sqliteConnection.Open();

            const string detectOldVersionQuery = "select count(1) from pragma_table_info('Bookmark') where name = 'Folder'";
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = detectOldVersionQuery;
            if (cmd.ExecuteScalar() is 1L)
            {
                CompatLevel = CompatibilityLevel.V3WithFolderNames;
            }
            else
            {
                CompatLevel = CompatibilityLevel.V2WithoutFolderNames;
            }

            if (inMemoryMode)
            {
                _sqliteConnectionInMemory = new SqliteConnection("Data Source=WorkingSet;Mode=Memory;Cache=Shared");
                _sqliteConnectionInMemory.Open();
                _sqliteConnection.BackupDatabase(_sqliteConnectionInMemory);
            }

            LoadBookmarksBase();
        }
        else
        {
            _lock = new object();
            _sqliteConnection = new SqliteConnection($"Data Source={databaseFileName}; Mode=ReadWriteCreate;");
            _sqliteConnection.Open();
        }

    }

    private void LoadBookmarksBase()
    {
        const string selectSQLV2 = "select Url, Title, DateAdded, SiteContentsId from Bookmark;";
        const string selectSQLV3 = "select Url, Title, DateAdded, SiteContentsId, Folder from Bookmark;";

        var ret = new List<Bookmark>(2000);
        var cmd = GetConnection().CreateCommand();
        cmd.CommandText = CompatLevel switch
        {
            CompatibilityLevel.V3WithFolderNames => selectSQLV3,
            CompatibilityLevel.V2WithoutFolderNames => selectSQLV2,
            _ => throw new InvalidOperationException("Compatibility level must be specified when reading bookmarks")
        };

        using (var dataReader = cmd.ExecuteReader())
        {
            while (dataReader.Read())
            {
                ret.Add(
                    new Bookmark
                    {
                        Url = dataReader.GetString(0),
                        Title = dataReader.GetString(1),
                        DateAdded = dataReader.GetDateTime(2),
                        SiteContentsId = dataReader.IsDBNull(3) ? null : dataReader.GetInt64(3),
                        ParentTitle = CompatLevel switch
                        {
                            CompatibilityLevel.V3WithFolderNames => dataReader.GetString(4),
                            _ => null,
                        }
                    }
                );
            }
        }
        LoadedBookmarks = ret;
    }

    public void SaveBookmarksBase(IEnumerable<Bookmark> list)
    {
        const string insertSQL = "insert into Bookmark (Url, DateAdded, SiteContentsId, Title, Folder) values (@p0, @p1, @p2, @p3, @p4);";
        using var insertCommand = new SqliteCommand(insertSQL, GetConnection());
        insertCommand.Transaction = _transaction;
        foreach (var b in list)
        {
            insertCommand.Parameters.Clear();
            insertCommand.Parameters.Add(new SqliteParameter("@p0", b.Url));
            insertCommand.Parameters.Add(new SqliteParameter("@p1", b.DateAdded));
            insertCommand.Parameters.Add(new SqliteParameter("@p2", (object)b.SiteContentsId ?? DBNull.Value));
            insertCommand.Parameters.Add(new SqliteParameter("@p3", b.Title));
            insertCommand.Parameters.Add(new SqliteParameter("@p4", b.ParentTitle));
            insertCommand.ExecuteNonQuery();
        }
    }

    public long SaveContents(string contents)
    {
        lock (_lock)
        {
            const string insertSQL = "insert into SiteContents (Text) values (@p0); select LAST_INSERT_ROWID();";
            using var insertCommand = new SqliteCommand(insertSQL, _sqliteConnection);
            insertCommand.Transaction = _transaction;
            insertCommand.Parameters.Add(new SqliteParameter("@p0", contents));
            using var dataReader = insertCommand.ExecuteReader();
            dataReader.Read();
            return dataReader.GetInt64(0);
        }
    }

    public string LoadContents(long siteContentsId)
    {
        string ret = null;
        var cmd = GetConnection().CreateCommand();
        cmd.Parameters.AddWithValue("id", siteContentsId);
        cmd.CommandText = "select Text from SiteContents where Id = @id;";
        using var dataReader = cmd.ExecuteReader();

        if (dataReader.Read())
        {
            ret = dataReader.GetString(0);
        }

        return ret;
    }

    public void Commit()
    {
        _transaction.Commit();
        const string vacuumSQL = "vacuum; pragma optimize;";
        using var vacuumCommand = new SqliteCommand(vacuumSQL, _sqliteConnection);
        vacuumCommand.ExecuteNonQuery();
    }

    public void PrepareTablesAndBeginTransaction()
    {
        const string dbPreparationSQL =
@"
CREATE TABLE IF NOT EXISTS SiteContents (
 Id INTEGER NOT NULL PRIMARY KEY,
 Text text 
);

CREATE TABLE IF NOT EXISTS Bookmark (
 Id INTEGER NOT NULL PRIMARY KEY,
 Url TEXT,
 Title TEXT,
 Folder TEXT,
 SiteContentsId INTEGER NULL,
 DateAdded DATETIME,
 FOREIGN KEY (SiteContentsId) REFERENCES SiteContents (Id)
);

PRAGMA foreign_keys = 1;
DELETE FROM Bookmark;
DELETE FROM SiteContents;
PRAGMA synchronous = 0;
PRAGMA journal_mode = MEMORY;
PRAGMA temp_store = MEMORY;
PRAGMA cache_size = 0;
";

        using var dnPreparationCommand = new SqliteCommand(dbPreparationSQL, _sqliteConnection);
        dnPreparationCommand.ExecuteNonQuery();
        _transaction = _sqliteConnection.BeginTransaction();
    }

    public enum OperationMode
    {
        Writing,
        Reading
    }

    #region IDisposable Support

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _sqliteConnection.Dispose();
                _sqliteConnectionInMemory?.Dispose();
            }

            disposedValue = true;
        }
    }

    #endregion

}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace BookmarksBase.Storage
{
    public class BookmarksBaseStorageService : IDisposable
    {
        const string DEFAULT_DB_FILENAME = "BookmarksBase.sqlite";
        public DateTime LastModifiedOn { get; }
        readonly IDictionary<long, string> _cache;
        readonly object _lock;

        public enum OperationMode
        {
            Writing,
            Reading
        }

        private OperationMode _operationMode;

        readonly SQLiteConnection _sqliteCon;
        bool disposedValue = false;

        public BookmarksBaseStorageService(OperationMode op, string databaseFileName = null)
        {
            if (databaseFileName == null)
            {
                databaseFileName = DEFAULT_DB_FILENAME;
            }

            if (op == OperationMode.Reading)
            {
                LastModifiedOn = File.GetLastWriteTime(databaseFileName);
                _cache = new ConcurrentDictionary<long, string>();
                _sqliteCon = new SQLiteConnection($"Data Source={databaseFileName}; Read Only = True;");
                _sqliteCon.Open();

                // Rely on own cache of SiteContents. Don't cache any pages in sqlite
                const string dbPreparationSQL = "PRAGMA cache_size=0";
                using (var dnPreparationCommand = new SQLiteCommand(dbPreparationSQL, _sqliteCon))
                {
                    dnPreparationCommand.ExecuteNonQuery();
                }
            }
            else
            {
                _lock = new object();
                if (!File.Exists(databaseFileName))
                {
                    SQLiteConnection.CreateFile(databaseFileName);
                }
                _sqliteCon = new SQLiteConnection($"Data Source={databaseFileName};");
                _sqliteCon.Open();
            }

            _operationMode = op;

        }

        public IList<Bookmark> LoadBookmarksBase()
        {
            const string selectSQL = "select Url, Title, DateAdded, SiteContentsId from Bookmark;";
            var ret = new List<Bookmark>(2000);
            using (var selectCommand = new SQLiteCommand(selectSQL, _sqliteCon))
            using (var dataReader = selectCommand.ExecuteReader())
            {
                while(dataReader.Read())
                {
                    ret.Add(
                        new Bookmark
                        {
                            Url = dataReader.GetString(0),
                            Title = dataReader.GetString(1),
                            DateAdded = dataReader.GetDateTime(2),
                            SiteContentsId = dataReader.GetInt64(3)
                        }
                    );
                }
            }
            return ret;
        }

        public void SaveBookmarksBase(IEnumerable<Bookmark> list)
        {
            const string insertSQL = "insert into Bookmark (Url, DateAdded, SiteContentsId, Title) values (@p0, @p1, @p2, @p3);";
            using (var insertCommand = new SQLiteCommand(insertSQL, _sqliteCon))
            {
                foreach (var b in list)
                {
                    insertCommand.Parameters.Clear();
                    insertCommand.Parameters.Add(new SQLiteParameter("@p0", b.Url));
                    insertCommand.Parameters.Add(new SQLiteParameter("@p1", b.DateAdded));
                    insertCommand.Parameters.Add(new SQLiteParameter("@p2", b.SiteContentsId));
                    insertCommand.Parameters.Add(new SQLiteParameter("@p3", b.Title));
                    insertCommand.ExecuteNonQuery();
                }
            }
        }

        public long SaveContents(string contents)
        {
            lock (_lock)
            {
                const string insertSQL = "insert into SiteContents (Text) values (@p0); select LAST_INSERT_ROWID();";
                using (var insertCommand = new SQLiteCommand(insertSQL, _sqliteCon))
                {
                    insertCommand.Parameters.Add(new SQLiteParameter("@p0", contents));
                    using (var dataReader = insertCommand.ExecuteReader())
                    {
                        dataReader.Read();
                        return dataReader.GetInt64(0);
                    }
                }
            }
        }

        public string LoadContents(long siteContentsId)
        {
            string ret = null;
            if (_cache.TryGetValue(siteContentsId, out ret))
            {
                return ret;
            }

            var selectSQL = $"select Text from SiteContents where Id = {siteContentsId};";
            using (var selectCommand = new SQLiteCommand(selectSQL, _sqliteCon))
            using (var dataReader = selectCommand.ExecuteReader())
            {
                if (dataReader.Read())
                {
                    ret = dataReader.GetString(0);
                    _cache[siteContentsId] = ret;
                }
            }
            return ret;
        }

        public void Commit()
        {
            const string vacuumSQL = "commit; vacuum; pragma optimize;";
            using (var vacuumCommand = new SQLiteCommand(vacuumSQL, _sqliteCon))
            {
                vacuumCommand.ExecuteNonQuery();
            }
        }

        public void Init()
        {
            if (_operationMode == OperationMode.Writing)
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
 SiteContentsId INTEGER NOT NULL,
 DateAdded DATETIME,
 FOREIGN KEY (SiteContentsId) REFERENCES SiteContents (Id)
);

DELETE FROM SiteContents;
DELETE FROM Bookmark;
PRAGMA synchronous = 0;
PRAGMA journal_mode = MEMORY;
PRAGMA temp_store = MEMORY;
PRAGMA cache_size = 0;
BEGIN TRANSACTION;
";

                using (var dnPreparationCommand = new SQLiteCommand(dbPreparationSQL, _sqliteCon))
                {
                    dnPreparationCommand.ExecuteNonQuery();
                }

            }
        }

        #region IDisposable Support

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _sqliteCon.Dispose();
                }

                disposedValue = true;
            }
        }

        #endregion

    }
}

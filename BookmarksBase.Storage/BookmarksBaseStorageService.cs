﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookmarksBase.Storage
{
    public class BookmarksBaseStorageService : IDisposable
    {
        const string DEFAULT_DB_FILENAME = "BookmarksBase.sqlite";
        public DateTime LastModifiedOn { get; }
        readonly Dictionary<long, string> _cache;
        readonly object _lock;

        public enum OperationMode
        {
            Writing,
            Reading
        }

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
                _cache = new Dictionary<long, string>();
                _sqliteCon = new SQLiteConnection($"Data Source={databaseFileName}; Read Only = True;");
            }
            else
            {
                _lock = new object();
                if (!File.Exists(databaseFileName))
                {
                    SQLiteConnection.CreateFile(databaseFileName);
                }
                _sqliteCon = new SQLiteConnection($"Data Source={databaseFileName};");
            }

            _sqliteCon.Open();

            if (op == OperationMode.Writing)
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
";

                using (var dnPreparationCommand = new SQLiteCommand(dbPreparationSQL, _sqliteCon))
                {
                    dnPreparationCommand.ExecuteNonQuery();
                }
            }

        }

        public IList<Bookmark> LoadBookmarksBase()
        {
            const string selectSQL = "select Url, Title, DateAdded, SiteContentsId from Bookmark;";
            var ret = new List<Bookmark>();
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
            const string vacuumSQL = "vacuum; pragma optimize;";
            using (var insertCommand = new SQLiteCommand(insertSQL, _sqliteCon))
            using (var vacuumCommand = new SQLiteCommand(vacuumSQL, _sqliteCon))
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
                vacuumCommand.ExecuteNonQuery();
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

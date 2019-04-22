using BookmarksBase.Search.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BookmarksBase.Storage
{
    public class BookmarksBaseStorageService : IDisposable
    {
        // Publics
        public IList<Bookmark> LoadedBookmarks { get; private set; }
        public DateTime LastModifiedOn { get; }

        // Privates
        private readonly object _lock;
        private readonly SQLiteConnection _sqliteConn;
        private readonly ConcurrentBag<SQLiteCommand> _sqlLiteReadingCommandsPool;
        private readonly Regex _deleteEmptyLinesRegex;
        private readonly OperationMode _operationMode;
        private const int DEFAULT_CONTEXT_LENGTH = 80;
        private const string DEFAULT_DB_FILENAME = "BookmarksBase.sqlite";
        private bool disposedValue;

        public BookmarksBaseStorageService(OperationMode op, string databaseFileName = null)
        {
            if (databaseFileName == null)
            {
                databaseFileName = DEFAULT_DB_FILENAME;
            }

            if (op == OperationMode.Reading)
            {
                _sqlLiteReadingCommandsPool = new ConcurrentBag<SQLiteCommand>();
                LastModifiedOn = File.GetLastWriteTime(databaseFileName);
                _sqliteConn = new SQLiteConnection($"Data Source={databaseFileName}; Read Only = True;");
                _sqliteConn.Open();

                const string dbPreparationSQL = "PRAGMA cache_size=5000";
                var cmd = GetReadingCommandFromPool();
                cmd.CommandText = dbPreparationSQL;
                cmd.ExecuteNonQuery();
                PutReadingCommandToPool(cmd);
                LoadBookmarksBase();
            }
            else
            {
                _lock = new object();
                if (!File.Exists(databaseFileName))
                {
                    SQLiteConnection.CreateFile(databaseFileName);
                }
                _sqliteConn = new SQLiteConnection($"Data Source={databaseFileName};");
                _sqliteConn.Open();
            }

            _operationMode = op;

            _deleteEmptyLinesRegex = new Regex(@"^\s*$[\r\n]*", RegexOptions.Compiled | RegexOptions.Multiline);

        }

        private void LoadBookmarksBase()
        {
            const string selectSQL = "select Url, Title, DateAdded, SiteContentsId from Bookmark;";
            var ret = new List<Bookmark>(2000);
            var cmd = GetReadingCommandFromPool();
            cmd.CommandText = selectSQL;
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
                            SiteContentsId = dataReader.GetInt64(3)
                        }
                    );
                }
            }
            PutReadingCommandToPool(cmd);
            LoadedBookmarks = ret;
        }

        public void SaveBookmarksBase(IEnumerable<Bookmark> list)
        {
            const string insertSQL = "insert into Bookmark (Url, DateAdded, SiteContentsId, Title) values (@p0, @p1, @p2, @p3);";
            using (var insertCommand = new SQLiteCommand(insertSQL, _sqliteConn))
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
                using (var insertCommand = new SQLiteCommand(insertSQL, _sqliteConn))
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
            var cmd = GetReadingCommandFromPool();
            cmd.CommandText = $"select Text from SiteContents where Id = {siteContentsId};";
            using (var dataReader = cmd.ExecuteReader())
            {
                if (dataReader.Read())
                {
                    ret = dataReader.GetString(0);
                }
            }
            PutReadingCommandToPool(cmd);
            return ret;
        }

        public void Commit()
        {
            const string vacuumSQL = "commit; vacuum; pragma optimize;";
            using (var vacuumCommand = new SQLiteCommand(vacuumSQL, _sqliteConn))
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

                using (var dnPreparationCommand = new SQLiteCommand(dbPreparationSQL, _sqliteConn))
                {
                    dnPreparationCommand.ExecuteNonQuery();
                }

            }
        }

        public IEnumerable<BookmarkSearchResult> DoSearch(string pattern)
        {
            if (
                pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("all:", StringComparison.CurrentCulture)
                || string.IsNullOrEmpty(pattern)
            )
            {
                return LoadedBookmarks.Select(
                    b => new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToMyDateTime(), b.SiteContentsId)
                );
            }
            bool inurl = false, caseSensitive = false, intitle = false;

            pattern = SanitizePattern(pattern);

            if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("inurl:", StringComparison.CurrentCulture))
            {
                inurl = true;
                pattern = pattern.Substring(6);
            }
            else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("casesens:", StringComparison.CurrentCulture))
            {
                caseSensitive = true;
                pattern = pattern.Substring(9);
            }
            else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("intitle:", StringComparison.CurrentCulture))
            {
                intitle = true;
                pattern = pattern.Substring(8);
            }

            Regex regex = null;

            try
            {
                regex = new Regex(
                    pattern,
                    RegexOptions.Compiled | (!caseSensitive ? RegexOptions.IgnoreCase : 0) | RegexOptions.Singleline
                );
            }
            catch (Exception e)
            {
                var ex = new RegExException(e.Message, e);
                throw ex;
            }

            var result = new ConcurrentBag<BookmarkSearchResult>();
            Parallel.ForEach(LoadedBookmarks, b =>
            {
                var match = regex.Match(inurl ? b.Url : (intitle ? b.Title : b.Url + b.Title));
                if (match.Success)
                {
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToMyDateTime(), b.SiteContentsId));
                }
                else if (!inurl && !intitle && b.SiteContentsId != 0)
                {
                    var content = LoadContents(b.SiteContentsId);
                    match = regex.Match(content);
                    if (match.Success)
                    {
                        var item = new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToMyDateTime(), b.SiteContentsId);

                        int excerptStart = match.Index - DEFAULT_CONTEXT_LENGTH;
                        if (excerptStart < 0)
                        {
                            excerptStart = 0;
                        }

                        int excerptEnd = match.Index + DEFAULT_CONTEXT_LENGTH;
                        if (excerptEnd > content.Length - 1)
                        {
                            excerptEnd = content.Length - 1;
                        }

                        item.ContentExcerpt = content.Substring(excerptStart, excerptEnd - excerptStart);
                        item.ContentExcerpt = _deleteEmptyLinesRegex.Replace(item.ContentExcerpt, string.Empty);
                        result.Add(item);
                    }
                }
            });
            return result;
        }

        private SQLiteCommand GetReadingCommandFromPool()
        {
            if (_sqlLiteReadingCommandsPool.TryTake(out SQLiteCommand c))
            {
                return c;
            }
            return new SQLiteCommand(_sqliteConn);
        }

        private void PutReadingCommandToPool(SQLiteCommand c)
        {
            _sqlLiteReadingCommandsPool.Add(c);
        }

        private string SanitizePattern(string pattern)
        {
            pattern = pattern.Replace("++", @"\+\+");
            pattern = pattern.Replace("**", @"\*\*");
            pattern = pattern.Replace("$$", @"\$\$");
            pattern = pattern.Replace("##", @"\#\#");
            pattern = pattern.Replace(" ", @"\s+");
            return pattern;
        }

        public class RegExException : Exception
        {
            public RegExException(string msg, Exception ie) : base(msg, ie)
            {

            }
        }

        public enum OperationMode
        {
            Writing,
            Reading
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
                    _sqliteConn.Dispose();
                    if (_sqlLiteReadingCommandsPool != null)
                    {
                        foreach (var c in _sqlLiteReadingCommandsPool)
                        {
                            c.Dispose();
                        }
                    }
                }

                disposedValue = true;
            }
        }

        #endregion

    }
}

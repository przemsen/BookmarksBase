using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using static BookmarksBase.Importer.BookmarksImporterBase.Constants;

namespace BookmarksBase.Importer;

class FirefoxBookmarksImporter : BookmarksImporterBase
{

    public FirefoxBookmarksImporter(
        BookmarksImporterBase.Options options,
        Storage.BookmarksBaseStorageService storage,
        IHttpClientFactory httpClientFactory
    ) : base(options, storage, httpClientFactory)
    {

    }

    public override int GetBookmarksCount()
    {
        var dbFilePath = GetBookmarksDatabaseFilePath();
        var connStr = $"Data Source={dbFilePath}; Mode=ReadOnly;";

        using var con = new SqliteConnection(connStr);
        using var cmd = new SqliteCommand(FirefoxBookmarksImporterConstants.SQLForGetBookmarksWithUrlCount, con);
        con.Open();

        var queryResult = (long)cmd.ExecuteScalar();

        var bookmarksCount = (int)queryResult;

        return bookmarksCount;
    }

    public override IEnumerable<Storage.Bookmark> GetBookmarks(int initialCount = DEFAULT_BOOKMARKS_LIST_CAPACITY)
    {
        string dbFile = GetBookmarksDatabaseFilePath();
        if (dbFile.Length == 0)
        {
            Trace.WriteLine("Firefox bookmarks file has not been found <br />");
            return null;
        }
        Trace.WriteLine("Firefox bookmarks file found: " + dbFile + " <br />");
        string cs = $"Data Source={dbFile}; Mode=ReadOnly;";
        var list = new List<Storage.Bookmark>(capacity: initialCount);
        using var con = new SqliteConnection(cs);
        using var cmd = new SqliteCommand(FirefoxBookmarksImporterConstants.SQLForGetBookmarksWithUrl, con);

        con.Open();
        using var rdr = cmd.ExecuteReader();

        int counter = 0;
        int skipCounter = _options.SkipQueriedBookmarks;
        while (rdr.Read() && (_options.LimitOfQueriedBookmarks == 0 || counter < _options.LimitOfQueriedBookmarks))
        {
            if (skipCounter > 0)
            {
                skipCounter--;
                continue;
            }

            var dateAdded = rdr.GetInt64(rdr.GetOrdinal("dateAdded"));
            var url = rdr.GetString(rdr.GetOrdinal("url"));
            if (list.Any(_ => string.Equals(_.Url, url, StringComparison.Ordinal)))
            {
                Trace.WriteLine($"{url} is duplicate bookmark. Skipping <br />");
                continue;
            }
            var b = new Storage.Bookmark()
            {
                Url = url,
                Title = rdr.GetString(rdr.GetOrdinal("title")),
                ParentTitle = rdr.GetString(rdr.GetOrdinal("parentTitle"))
            };
            if (dateAdded > 0)
            {
                var dateAddedOffset = DateTimeOffset.FromUnixTimeSeconds(dateAdded);
                b.DateAdded = dateAddedOffset.UtcDateTime;
            }
            list.Add(b);
            counter++;
        }

        Trace.WriteLine(list.Count + " bookmarks read <br />");
        return list.OrderByDescending(b => b.DateAdded).ToArray();
    }

    public override void GetCookies()
    {
        var cookiesDbFileFile = GetCookiesDatabaseFilePath();
        if (cookiesDbFileFile.Length == 0)
        {
            Trace.WriteLine("Firefox cookies file has not been found <br />");
            return;
        }
        Trace.WriteLine("Firefox cookies file found: " + cookiesDbFileFile + " <br />");
        string cs = $"Data Source={cookiesDbFileFile}; Mode=ReadOnly;";

        using var con = new SqliteConnection(cs);
        con.Open();

        foreach (var stealCookies in _options.CookieStealings)
        {
            const string hostParamName = "@host";
            using var cmd = new SqliteCommand($"select name, value from moz_cookies where host like {hostParamName}", con);
            cmd.Parameters.Add(new SqliteParameter($"{hostParamName}", $"%{stealCookies.WhereHostRLike}"));
            using var rdr = cmd.ExecuteReader();
            string value = string.Empty;

            while (rdr.Read())
            {
                value = $"{value}{rdr.GetString(0)}={rdr.GetString(1)}; ";
            }

            if (value != String.Empty)
            {
                _cookies[stealCookies] = value;
                Trace.WriteLine($"Cookie stolen for: {stealCookies.ForUrl} <br />");
            }
            else
            {
                Trace.WriteLine($"Cookie requested, but not found in query results for: {stealCookies.ForUrl} <br />");
            }

        }
    }

    private static string GetBookmarksDatabaseFilePath() => Path.Combine(GetGetProfilePath(), "places.sqlite");

    private static string GetCookiesDatabaseFilePath() => Path.Combine(GetGetProfilePath(), "cookies.sqlite");

    private static string GetGetProfilePath()
    {
        string apppath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string mozilla = Path.Combine(apppath, "Mozilla");
        bool exist = Directory.Exists(mozilla);
        if (exist)
        {
            string firefox = Path.Combine(mozilla, "firefox");
            if (Directory.Exists(firefox))
            {
                string prof_file = Path.Combine(firefox, "profiles.ini");
                bool file_exist = File.Exists(prof_file);
                if (file_exist)
                {
                    string resp;
                    using (var rdr = new StreamReader(prof_file))
                    {
                        resp = rdr.ReadToEnd();
                    }
                    string[] lines = resp.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    string location = lines.First(x => x.Contains("Path=")).Split(new string[] { "=" }, StringSplitOptions.None)[1];
                    location = location.Replace('/', '\\');
                    return Path.Combine(firefox, location);
                }
            }
        }
        return string.Empty;
    }

    public static class FirefoxBookmarksImporterConstants
    {
        public const string SQLForGetBookmarksWithUrl = @"
SELECT
    b.[title],
    _b.[title] as parentTitle,
    p.[url],
    (
        (CASE 
            WHEN b.dateAdded = 0 AND b.lastModified <> 0 THEN b.lastModified 
            WHEN b.dateAdded <> 0 THEN b.dateAdded
            ELSE 1 END
        ) / 1000000
    ) AS dateAdded
FROM [moz_places] p
JOIN [moz_bookmarks] b ON p.[id] = b.[fk]
JOIN [moz_bookmarks] _b ON b.parent = _b.id
WHERE (b.[title] IS NOT NULL) AND (p.[url] LIKE 'http://%' OR p.[url] LIKE 'https://%')
;";

        //_________________________________________________________________________

        public const string SQLForGetBookmarksWithUrlCount = @"
SELECT COUNT(1)
FROM [moz_places] p
JOIN [moz_bookmarks] b ON p.[id] = b.[fk]
JOIN [moz_bookmarks] _b ON b.parent = _b.id
WHERE (b.[title] IS NOT NULL) AND (p.[url] LIKE 'http://%' OR p.[url] LIKE 'https://%')
";
    }

}

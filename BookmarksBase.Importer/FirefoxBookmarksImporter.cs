using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BookmarksBase.Importer
{
    public class FirefoxBookmarksImporter : BookmarksImporter
    {
        public FirefoxBookmarksImporter(BookmarksImporter.Options options, BookmarksBaseStorageService storage)
            : base(options, storage)
        {

        }

        public override IEnumerable<Bookmark> GetBookmarks()
        {
            string dbFile = GetFirefoxBookmarksFile();
            if (dbFile.Length == 0)
            {
                Trace.WriteLine("Firefox bookmarks file has not been found <br />");
                return null;
            }
            Trace.WriteLine("Firefox bookmarks file found: " + dbFile + " <br />");
            string cs = $"Data Source={dbFile};Version=3;";
            var list = new List<Bookmark>();
            using (SQLiteConnection con = new SQLiteConnection(cs))
            using (SQLiteCommand cmd = new SQLiteCommand(FirefoxBookmarksImporterConstants.SQLForGetBookmarksWithUrl, con))
            {
                con.Open();
                using (SQLiteDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var dateAdded = rdr.GetInt64(rdr.GetOrdinal("dateAdded"));
                        var url = rdr.GetString(rdr.GetOrdinal("url"));
                        if (list.Any(_ => _.Url == url))
                        {
                            Trace.WriteLine($"{url} is duplicate bookmark. Skipping <br />");
                            continue;
                        }
                        var b = new Bookmark()
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
                    }
                }
            }
            Trace.WriteLine(list.Count + " bookmarks read <br />");
            return list.OrderByDescending(b => b.DateAdded).ToList();
        }

        string GetFirefoxBookmarksFile()
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
                        string prof_dir = Path.Combine(firefox, location);
                        return Path.Combine(prof_dir, "places.sqlite");
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

        }

    }
}

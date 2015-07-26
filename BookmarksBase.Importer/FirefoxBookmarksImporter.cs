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
        public FirefoxBookmarksImporter(BookmarksImporter.Options options) : base(options)
        {

        }

        public override IList<Bookmark> GetBookmarks()
        {
            string dbFile = GetFirefoxBookmarksFile();
            if (dbFile == string.Empty)
            {
                Trace.WriteLine("Firefox bookmarks file has not been found");
                return null;
            }
            Trace.WriteLine("Firefox bookmarks file found: " + dbFile);
            string cs = @"Data Source=" + dbFile + ";Version=3;";
            var list = new List<Bookmark>();
            using (SQLiteConnection con = new SQLiteConnection(cs))
            using (SQLiteCommand cmd = new SQLiteCommand(FirefoxBookmarksImporterConstants.SQLForGetBookmarksWithUrl, con))
            {
                con.Open();
                using (SQLiteDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var b = new Bookmark()
                        {
                            Url = rdr.GetString(rdr.GetOrdinal("url")),
                            Title = rdr.GetString(rdr.GetOrdinal("title"))
                        };
                        list.Add(b);
                    }
                }
            }
            Trace.WriteLine(list.Count + " bookmarks read");
            return list;
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
                        var rdr = new StreamReader(prof_file);
                        string resp = rdr.ReadToEnd();
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Language Usage Opportunities", "RECS0014:If all fields, properties and methods members are static, the class can be made static.", Justification = "<Pending>")]
        public class FirefoxBookmarksImporterConstants : BookmarksImporterConstants
        {
            public const string SQLForGetBookmarksWithUrl = @"
                SELECT b.[title], p.[url]
                FROM [moz_places] p
                INNER JOIN [moz_bookmarks] b ON p.[id] = b.[fk]
                WHERE (b.[title] IS NOT NULL) AND (p.[url] LIKE 'http://%' OR p.[url] LIKE 'https://%')
            ;";

        }

    }
}

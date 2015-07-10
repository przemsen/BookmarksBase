using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            List<Bookmark> list = new List<Bookmark>();
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

        private string GetFirefoxBookmarksFile()
        {
            string apppath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string mozilla = System.IO.Path.Combine(apppath, "Mozilla");
            bool exist = System.IO.Directory.Exists(mozilla);
            if (exist)
            {
                string firefox = System.IO.Path.Combine(mozilla, "firefox");
                if (System.IO.Directory.Exists(firefox))
                {
                    string prof_file = System.IO.Path.Combine(firefox, "profiles.ini");
                    bool file_exist = System.IO.File.Exists(prof_file);
                    if (file_exist)
                    {
                        StreamReader rdr = new StreamReader(prof_file);
                        string resp = rdr.ReadToEnd();
                        string[] lines = resp.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                        string location = lines.First(x => x.Contains("Path=")).Split(new string[] { "=" }, StringSplitOptions.None)[1];
                        location = location.Replace('/', '\\');
                        string prof_dir = System.IO.Path.Combine(firefox, location);
                        return System.IO.Path.Combine(prof_dir, "places.sqlite");
                    }
                }
            }
            return string.Empty;
        }

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

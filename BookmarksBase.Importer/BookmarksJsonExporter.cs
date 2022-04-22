using BookmarksBase.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BookmarksBase.Importer
{
    public class BookmarksJsonExporter
    {
        readonly IEnumerable<Bookmark> _bookmarks;

        public BookmarksJsonExporter(IEnumerable<Bookmark> bookmarks)
        {
            _bookmarks = bookmarks;
        }

        public void WriteJson()
        {
            var sb = new StringBuilder();

            var json = JsonConvert.SerializeObject(_bookmarks, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-dd HH:mm:ss",
                NullValueHandling = NullValueHandling.Ignore
            });

            var timeStamp = $"const bookmarksTimeStamp = '{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}';";

            sb.Append("const bookmarksArray = ");
            sb.Append(json);
            sb.AppendLine(";");
            sb.AppendLine(timeStamp);

            File.WriteAllText("bookmarksArray.js", sb.ToString());
        }
    }
}

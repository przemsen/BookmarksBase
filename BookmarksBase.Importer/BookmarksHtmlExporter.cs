using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BookmarksBase.Storage;

namespace BookmarksBase.Importer
{
    public class BookmarksHtmlExporter
    {
        readonly IEnumerable<Bookmark> _bookmarks;

        public BookmarksHtmlExporter(IEnumerable<Bookmark> bookmarks)
        {
            _bookmarks = bookmarks;
        }

        public void WriteHtml()
        {
            var template = File.ReadAllText("bookmarks_template.htm");
            template = template.Replace("**TIMESTAMP**", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var sb = new StringBuilder();
            foreach(var b in _bookmarks)
            {
                sb.AppendLine(
                    $"<tr><td>{b.Title}</td> <td><a href=\"{b.Url}\">{b.Url}</a></td> <td>{b.DateAdded:yyyy-MM-dd HH:mm:ss}</td> <td>{b.ParentTitle}</td> </tr>");
            }
            var htmlBookmarksContent = sb.ToString();
            var completeBookmarksHtml = template.Replace("**CONTENT**", htmlBookmarksContent);
            File.WriteAllText("bookmarks.htm", completeBookmarksHtml);
        }
    }
}

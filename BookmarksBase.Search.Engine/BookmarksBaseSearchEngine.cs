using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Concurrent;

namespace BookmarksBase.Search.Engine
{
    public class BookmarksBaseSearchEngine
    {
        private readonly Regex _deleteEmptyLinesRegex;
        private XDocument _doc;
        public const string DB_FILE_NAME = "bookmarksbase.xml";
        public const int DEFAULT_CONTEXT_LENGTH = 80;

        public BookmarksBaseSearchEngine()
        {
            _deleteEmptyLinesRegex = new Regex(@"^\s*$[\r\n]*", RegexOptions.Compiled | RegexOptions.Multiline);
            _doc = null;
        }

        public void Load(string fileName = DB_FILE_NAME)
        {
            _doc = XDocument.Load(fileName);
        }

        public string GetCreationDate()
        {
            var root = _doc.Element("Bookmarks");
            var date = root.Attribute("CreationDate").Value;
            return date;
        }

        public Bookmark[] GetBookmarks()
        {
            var root = _doc.Descendants("Bookmarks");
            var xbookmarks = root.Elements("Bookmark");
            var count = xbookmarks.Count();
            var bookmarks = new Bookmark[count];
            int i = 0;
            foreach(var b in xbookmarks)
            {
                bookmarks[i].Title = b.Element("Title").Value;
                bookmarks[i].Url = b.Element("Url").Value;
                bookmarks[i].Content = b.Element("Content").Value;
                i++;
            }
            return bookmarks;
        }

        public void Release()
        {
            _doc = null;
            System.GC.Collect();
        }

        public IEnumerable<BookmarkSearchResult> DoSearch(Bookmark[] bookmarks, string pattern)
        {
            pattern = SanitizePattern(pattern);
            var regex = new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
            var result = new ConcurrentBag<BookmarkSearchResult>();
            Parallel.ForEach(bookmarks, b =>
            {
                var match = regex.Match(b.Url + b.Title);
                if (match.Success)
                {
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, null));
                }
                else
                {
                    match = regex.Match(b.Content);
                    if (match.Success)
                    {
                        var item = new BookmarkSearchResult(b.Url, b.Title, null);

                        int excerptStart = match.Index - DEFAULT_CONTEXT_LENGTH;
                        if (excerptStart < 0)
                        {
                            excerptStart = 0;
                        }

                        int excerptEnd = match.Index + DEFAULT_CONTEXT_LENGTH;
                        if (excerptEnd > b.Content.Length -1)
                        {
                            excerptEnd = b.Content.Length - 1;
                        }

                        item.ContentExcerpt = b.Content.Substring(excerptStart, excerptEnd - excerptStart);
                        item.ContentExcerpt = _deleteEmptyLinesRegex.Replace(item.ContentExcerpt, string.Empty);
                        result.Add(item);
                    }
                }
            });
            return result;
        }

        private static string SanitizePattern(string pattern)
        {
            pattern = pattern.Replace("++", @"\+\+");
            pattern = pattern.Replace("**", @"\*\*");
            pattern = pattern.Replace("$$", @"\$\$");
            pattern = pattern.Replace("##", @"\#\#");
            pattern = pattern.Replace(" ", @"\s+");
            return pattern;
        }

        public IEnumerable<BookmarkSearchResult> DoDeadSearch(Bookmark[] bookmarks)
        {
            var result = from b in bookmarks
                         where b.Content == "[Error]"
                         select new BookmarkSearchResult(b.Url, b.Title, null)
                         ;
            return result;
        }

        public class Options
        {
        }

    }

    public class BookmarkSearchResult
    {
        public BookmarkSearchResult(string url, string title, string contentExcetpt)
        {
            Url = url;
            Title = title;
            ContentExcerpt = contentExcetpt;
        }
        public string Url { get; set; }
        public string Title { get; set; }
        public string ContentExcerpt { get; set; }
    }

    public struct Bookmark
    {
        public string Url;
        public string Title;
        public string Content;
    }

}
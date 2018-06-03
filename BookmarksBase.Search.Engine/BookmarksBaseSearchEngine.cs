using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System;

namespace BookmarksBase.Search.Engine
{
    public class BookmarksBaseSearchEngine
    {
        public class RegExException : Exception
        {
            public RegExException(string msg, Exception ie) : base(msg, ie)
            {

            }
        }

        readonly Regex _deleteEmptyLinesRegex;
        XDocument _doc;
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
                bookmarks[i].ContentsFileName =  b.Element("ContentsFileName").Value;
                bookmarks[i].DateAdded = b.Element("DateAdded").Value;
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
            if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("all:", System.StringComparison.CurrentCulture))
            {
                return bookmarks.Select(
                    b => new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded)
                );
            }
            bool inurl = false, caseSensitive = false;

            pattern = SanitizePattern(pattern);

            if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("inurl:", System.StringComparison.CurrentCulture))
            {
                inurl = true;
                pattern = pattern.Substring(6);
            }
            else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("casesens:", System.StringComparison.CurrentCulture))
            {
                caseSensitive = true;
                pattern = pattern.Substring(9);
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
            Parallel.ForEach(bookmarks, b =>
            {
                var match = regex.Match(inurl ? b.Url : b.Url + b.Title);
                if (match.Success)
                {
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded));
                }
                else if (!inurl && !string.IsNullOrEmpty(b.ContentsFileName))
                {
                    var content = File.ReadAllText(b.ContentsFileName);
                    match = regex.Match(content);
                    if (match.Success)
                    {
                        var item = new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded);

                        int excerptStart = match.Index - DEFAULT_CONTEXT_LENGTH;
                        if (excerptStart < 0)
                        {
                            excerptStart = 0;
                        }

                        int excerptEnd = match.Index + DEFAULT_CONTEXT_LENGTH;
                        if (excerptEnd > content.Length -1)
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

        static string SanitizePattern(string pattern)
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
                         select new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded)
                         ;
            return result;
        }

    }

    public class BookmarkSearchResult
    {
        public BookmarkSearchResult(string url, string title, string contentExcetpt, string dateAdded)
        {
            Url = url;
            Title = title;
            ContentExcerpt = contentExcetpt;
            DateAdded = dateAdded;
        }
        public string Url { get; set; }
        public string Title { get; set; }
        public string ContentExcerpt { get; set; }
        public string DateAdded { get; set; }
    }

    public struct Bookmark
    {
        public string Url;
        public string Title;
        public string Content;
        public string ContentsFileName;
        public string DateAdded;
    }

}
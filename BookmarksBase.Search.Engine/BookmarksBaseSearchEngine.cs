using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System;
using BookmarksBase.Storage;

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
        readonly BookmarksBaseStorageService _storage;
        public const int DEFAULT_CONTEXT_LENGTH = 80;

        public BookmarksBaseSearchEngine(BookmarksBaseStorageService storage)
        {
            _deleteEmptyLinesRegex = new Regex(@"^\s*$[\r\n]*", RegexOptions.Compiled | RegexOptions.Multiline);
            _storage = storage;
        }

        public IEnumerable<BookmarkSearchResult> DoSearch(IEnumerable<Bookmark> bookmarks, string pattern)
        {
            if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("all:", System.StringComparison.CurrentCulture))
            {
                return bookmarks.Select(
                    b => new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToShortDateString())
                );
            }
            bool inurl = false, caseSensitive = false, intitle = false;

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
            else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("intitle:", System.StringComparison.CurrentCulture))
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
            Parallel.ForEach(bookmarks, b =>
            {
                var match = regex.Match(inurl ? b.Url : (intitle ? b.Title : b.Url + b.Title));
                if (match.Success)
                {
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToShortDateString()));
                }
                else if (!inurl && !intitle && b.SiteContentsId != 0)
                {
                    var content = _storage.LoadContents(b.SiteContentsId);
                    match = regex.Match(content);
                    if (match.Success)
                    {
                        var item = new BookmarkSearchResult(b.Url, b.Title, null, b.DateAdded.ToShortDateString());

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

}
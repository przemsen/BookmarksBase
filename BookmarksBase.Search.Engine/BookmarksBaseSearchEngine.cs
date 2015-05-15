using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BookmarksBase.Search.Engine
{
    public class BookmarksBaseSearchEngine
    {
        private readonly Options _options;
        private readonly Regex   _deleteWhiteSpaceAtBeginningRegex;

        public BookmarksBaseSearchEngine(Options options)
        {
            _options = options;
            _deleteWhiteSpaceAtBeginningRegex = new Regex(@"^\s+", RegexOptions.Compiled);
        }

        public IEnumerable<XElement> GetBookmarks(string fileName = "bookmarksbase.xml")
        {
            XDocument bookmarksBase = XDocument.Load(fileName);
            var root = bookmarksBase.Descendants("Bookmarks");
            var bookmarks = root.Elements("Bookmark");
            return bookmarks;
        }

        public IEnumerable<BookmarkSearchResult> DoSearch(IEnumerable<XElement> bookmarks, string find)
        {
            var result = from b in bookmarks
                         let foundInContent = b
                            .Element("Content")
                            .Value
                            .IndexOf(find, StringComparison.OrdinalIgnoreCase)
                         let foundInUrl = b
                            .Element("Url")
                            .Value
                            .IndexOf(find, StringComparison.OrdinalIgnoreCase)
                         let foundInTitle = b
                            .Element("Title")
                            .Value
                            .IndexOf(find, StringComparison.OrdinalIgnoreCase)
                         where
                            (foundInContent != BookmarkSearchResult.NotFound) ||
                            (foundInUrl != BookmarkSearchResult.NotFound) ||
                            (foundInTitle != BookmarkSearchResult.NotFound)
                         select
                         GetBookmarkSearchResult(b, foundInContent)
                         ;
            return result;
        }

        public IEnumerable<BookmarkSearchResult> DoDeadSearch(IEnumerable<XElement> bookmarks)
        {
            var result = from b in bookmarks
                         where b.Element("Content").Value == "[Error]"
                         select
                         GetBookmarkSearchResult(b, foundInContent: BookmarkSearchResult.NotFound)
                         ;
            return result;
        }

        private BookmarkSearchResult GetBookmarkSearchResult(XElement bookmark, int foundInContent)
        {
            var result = new BookmarkSearchResult();

            result.Url = bookmark.Element("Url").Value;
            if (foundInContent != BookmarkSearchResult.NotFound)
            {
                result.ContentExcerpt =
                    bookmark
                        .Element("Content")
                        .Value.Substring(
                            (foundInContent - _options.ExcerptContextLength) < 0 ? 0 : (foundInContent - _options.ExcerptContextLength)
                        )
                        .Take(_options.ExcerptContextLength * 2)
                        .AsString()
                        .Replace("\n", string.Empty);
                result.ContentExcerpt = _deleteWhiteSpaceAtBeginningRegex.Replace(result.ContentExcerpt, string.Empty);
            }
            result.Title = bookmark.Element("Title").Value;

            return result;
        }

        public class Options
        {
            public int ExcerptContextLength { get; set; }
        }

    }

    public class BookmarkSearchResult
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string ContentExcerpt { get; set; }
        public const int NotFound = -1;
    }

    public static class Extensions
    {
        public static string AsString(this IEnumerable<char> value)
        {
            return string.Join(string.Empty, value);
        }
    }

}
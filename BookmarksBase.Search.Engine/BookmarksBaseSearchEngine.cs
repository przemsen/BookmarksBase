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
        private readonly Regex   _deleteWhiteSpaceAtBeginningRegex;
        private XDocument _doc;

        public BookmarksBaseSearchEngine()
        {
            _deleteWhiteSpaceAtBeginningRegex = new Regex(@"^\s+", RegexOptions.Compiled);
            _doc = null;
        }

        public void Load(string fileName = "bookmarksbase.xml")
        {
            _doc = XDocument.Load(fileName);
        }

        public string GetCreationDate()
        {
            var root = _doc.Element("Bookmarks");
            var date = root.Attribute("CreationDate").Value;
            return date;
        }

        public IEnumerable<XElement> GetBookmarks()
        {
            var root = _doc.Descendants("Bookmarks");
            var bookmarks = root.Elements("Bookmark");
            return bookmarks;
        }

        public IEnumerable<BookmarkSearchResult> DoSearch(IEnumerable<XElement> bookmarks, string pattern)
        {
            pattern = SanitizePattern(pattern);
            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var result = new List<BookmarkSearchResult>();
            var lck = new Object();

            Parallel.ForEach(bookmarks, b =>
            {
                var match = regex.Match(b.Element("Content").Value);
                if (match.Success)
                {
                    lock (lck)
                    {
                        result.Add(new BookmarkSearchResult
                        {
                            Title = b.Element("Title").Value,
                            ContentExcerpt = _deleteWhiteSpaceAtBeginningRegex.Replace(match.Value, string.Empty),
                            Url = b.Element("Url").Value
                        });
                    }
                }
                else
                {
                    match = regex.Match(b.Element("Url").Value + b.Element("Title").Value);
                    if (match.Success)
                    {
                        lock (lck)
                        {
                            result.Add(new BookmarkSearchResult
                            {
                                Title = b.Element("Title").Value,
                                Url = b.Element("Url").Value
                            });
                        }
                    }
                }
            });

            return result;
        }

        private static string SanitizePattern(string pattern)
        {
            if (pattern.IndexOf("++") != -1)
            {
                pattern = pattern.Replace("++", @"\+\+");
            }
            else if (pattern.IndexOf("**") != -1)
            {
                pattern = pattern.Replace("**", @"\*\*");
            }
            else if (pattern.IndexOf("$$") != -1)
            {
                pattern = pattern.Replace("$$", @"\$\$");
            }
            else if (pattern.IndexOf("##") != -1)
            {
                pattern = pattern.Replace("##", @"\#\#");
            }
            pattern = string.Format("^.*{0}.*$", pattern);
            return pattern;
        }

        public IEnumerable<BookmarkSearchResult> DoDeadSearch(IEnumerable<XElement> bookmarks)
        {
            var result = from b in bookmarks
                         where b.Element("Content").Value == "[Error]"
                         select new BookmarkSearchResult
                         {
                            Title = b.Element("Title").Value,
                            Url = b.Element("Url").Value
                         }
                         ;
            return result;
        }

        public class Options
        {
        }

    }

    public class BookmarkSearchResult
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string ContentExcerpt { get; set; }
        public const int NotFound = -1;
    }


}
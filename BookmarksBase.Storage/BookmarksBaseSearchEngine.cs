using BookmarksBase.Search.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BookmarksBase.Storage;

public class BookmarksBaseSearchEngine
{
    private readonly IReadOnlyCollection<Bookmark> _loadedBookmarks;
    private readonly Func<long, string> _loadContentsFunc;

    public const string HelpMessage = @"Available modifiers:
all:        -- loads all bookmarks sorted by date descending
casesens:   -- makes search case sensitive
help: or ?  -- displays this text
inurl:      -- searches only in the urls
intitle:    -- searches only in the titles
multiline:  -- default grep behaviour, analyzes line by line
";

    public BookmarksBaseSearchEngine(Expression<Func<long, string>> loadContentsFunc, IReadOnlyCollection<Bookmark> loadedBookmarks)
    {
        _loadContentsFunc = loadContentsFunc.Compile();
        _loadedBookmarks = loadedBookmarks;
    }

    public IEnumerable<BookmarkSearchResult> DoSearch(string pattern)
    {
        if (
            pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("all:", StringComparison.CurrentCulture)
            || string.IsNullOrEmpty(pattern)
        )
        {
            return _loadedBookmarks.Select(
                b => new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, BookmarkSearchResult.MatchKind.None)
            );
        }
        bool inurl = false, caseSensitive = false, intitle = false, multiline = false;

        pattern = SanitizePattern(pattern);

        if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("inurl:", StringComparison.CurrentCulture))
        {
            inurl = true;
            pattern = pattern[6..];
        }
        else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("casesens:", StringComparison.CurrentCulture))
        {
            caseSensitive = true;
            pattern = pattern[9..];
        }
        else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("multiline:", StringComparison.CurrentCulture))
        {
            multiline = true;
            pattern = pattern[10..];
        }
        else if (pattern.ToLower(Thread.CurrentThread.CurrentCulture).StartsWith("intitle:", StringComparison.CurrentCulture))
        {
            intitle = true;
            pattern = pattern[8..];
        }

        Regex regex = null;

        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.Compiled |
                (caseSensitive ? 0 : RegexOptions.IgnoreCase) |
                (multiline ? 0 : RegexOptions.Singleline)
            );
        }
        catch (Exception e)
        {
            var ex = new RegExException(e.Message, e);
            throw ex;
        }

        var result = new ConcurrentBag<BookmarkSearchResult>();
        Parallel.ForEach(_loadedBookmarks, b =>
        {
            MatchCollection matchCollection;
            BookmarkSearchResult.MatchKind matchKind = BookmarkSearchResult.MatchKind.None;

            if (inurl)
            {
                matchCollection = regex.Matches(b.Url);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Irrelevant;
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, matchKind));
                }
            }
            else if (intitle)
            {
                matchCollection = regex.Matches(b.Title);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Irrelevant;
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, matchKind));
                }
            }
            else
            {
                matchCollection = regex.Matches(b.Url);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Url;
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, matchKind));
                    return;
                }

                matchCollection = regex.Matches(b.Title);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Title;
                    result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, matchKind));
                    return;
                }

                if (b.SiteContentsId is long siteContentsId)
                {
                    var content = _loadContentsFunc(siteContentsId);
                    matchCollection = regex.Matches(content);
                    if (matchCollection.Count > 0)
                    {
                        matchKind = BookmarkSearchResult.MatchKind.Content;
                        var item = new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.ToMyDateTime(), b.ParentTitle, b.SiteContentsId, matchKind, matchCollection, fullContent: content);
                        result.Add(item);
                    }
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

    public class RegExException : Exception
    {
        public RegExException(string msg, Exception ie) : base(msg, ie)
        {

        }

        public RegExException() : base()
        {
        }

        public RegExException(string message) : base(message)
        {
        }
    }

}

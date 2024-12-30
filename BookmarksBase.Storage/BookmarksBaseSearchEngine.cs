using BookmarksBase.Search.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BookmarksBase.Storage;

public class BookmarksBaseSearchEngine(Func<long, string> loadContentsFunc, IReadOnlyCollection<Bookmark> loadedBookmarks)
{
    public const string HelpMessage = """
    Available modifier keywords:
    all:        -- loads all bookmarks sorted by date descending
    casesens:   -- makes search case sensitive
    help: or ?  -- displays this text
    inurl:      -- searches only in the urls
    intitle:    -- searches only in the titles
    singleline: -- treats whole text as one big line (affects performance)
    err:        -- search for erroneous bookmarks
    """;

    public static readonly ImmutableArray<string> KeywordsList =
    [
        "all:",         // 0
        "casesens:",
        "help:",
        "inurl:",
        "intitle:",
        "singleline:",  // 5
        "err:"
    ];

    public IReadOnlyCollection<BookmarkSearchResult> DoSearch(string pattern)
    {
        if (
            pattern.StartsWith(KeywordsList[0], StringComparison.Ordinal)
            || string.IsNullOrEmpty(pattern)
        )
        {
            return loadedBookmarks.Select(
                b => new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, BookmarkSearchResult.MatchKind.None)
            ).ToArray();
        }

        bool inurl = false, caseSensitive = false, intitle = false, singleLine = false;
        Regex regex = null;

        if (pattern.StartsWith(KeywordsList[3], StringComparison.Ordinal))
        {
            inurl = true;
            pattern = pattern[6..];
        }
        else if (pattern.StartsWith(KeywordsList[1], StringComparison.Ordinal))
        {
            caseSensitive = true;
            pattern = pattern[9..];
        }
        else if (pattern.StartsWith(KeywordsList[5], StringComparison.Ordinal))
        {
            singleLine = true;
            pattern = pattern[10..];
        }
        else if (pattern.StartsWith(KeywordsList[4], StringComparison.Ordinal))
        {
            intitle = true;
            pattern = pattern[8..];
        }
        else if (pattern.StartsWith(KeywordsList[6], StringComparison.Ordinal))
        {
            regex = regex = new Regex(
                @" \(erroneous\)$",
                RegexOptions.Compiled |
                RegexOptions.Multiline |
                RegexOptions.CultureInvariant |
                RegexOptions.NonBacktracking
            );
            intitle = true;
        }

        if (regex is null)
        {
            try
            {
                regex = new Regex(
                    pattern,
                    RegexOptions.NonBacktracking |
                    RegexOptions.Compiled |
                    RegexOptions.CultureInvariant |
                    (caseSensitive ? 0 : RegexOptions.IgnoreCase) |
                    (singleLine ? RegexOptions.Singleline : RegexOptions.Multiline)
                );
            }
            catch (Exception e)
            {
                var ex = new RegExException(e.Message, e);
                throw ex;
            }
        }

        var result = new SortedSet<BookmarkSearchResult>(comparer: new DateTimeComparer());
        Lock @lock = new();

        Parallel.ForEach(loadedBookmarks, b =>
        {
            MatchCollection matchCollection;
            BookmarkSearchResult.MatchKind matchKind = BookmarkSearchResult.MatchKind.None;

            if (inurl)
            {
                matchCollection = regex.Matches(b.Url);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Irrelevant;

                    lock (@lock)
                        result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, matchKind));
                }
            }
            else if (intitle)
            {
                matchCollection = regex.Matches(b.Title);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Irrelevant;
                    lock (@lock)
                        result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, matchKind));
                }
            }
            else
            {
                matchCollection = regex.Matches(b.Url);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Url;
                    lock (@lock)
                        result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, matchKind));
                    return;
                }

                matchCollection = regex.Matches(b.Title);
                if (matchCollection.Count > 0)
                {
                    matchKind = BookmarkSearchResult.MatchKind.Title;
                    lock (@lock)
                        result.Add(new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, matchKind));
                    return;
                }

                if (b.SiteContentsId is long siteContentsId)
                {
                    var content = loadContentsFunc(siteContentsId);
                    matchCollection = regex.Matches(content);
                    if (matchCollection.Count > 0)
                    {
                        matchKind = BookmarkSearchResult.MatchKind.Content;
                        var item = new BookmarkSearchResult(b.Url, b.Title, b.DateAdded.MyToString(), b.DateAdded, b.ParentTitle, b.SiteContentsId, matchKind, matchCollection, fullContent: content);
                        lock (@lock)
                            result.Add(item);
                    }
                }
            }
        });

        return result;
    }

    public class RegExException(string msg, Exception ie) : Exception(msg, ie)
    {

    }

    internal struct DateTimeComparer : IComparer<BookmarkSearchResult>
    {
        public int Compare(BookmarkSearchResult x, BookmarkSearchResult y)
        {
            return y.DateTimeAdded.CompareTo(x.DateTimeAdded);
        }
    }

}

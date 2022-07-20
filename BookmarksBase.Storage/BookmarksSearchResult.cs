using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BookmarksBase.Storage;

public class BookmarkSearchResult
{
    public BookmarkSearchResult(
        string url,
        string title,
        string dateAdded,
        string folder,
        long? contentsId,
        MatchKind matchKind,
        MatchCollection matchCollection = null,
        string fullContent = null
    )
    {
        Url = url;
        Title = title;
        DateAdded = dateAdded;
        Folder = folder;
        SiteContentsId = contentsId;
        MatchCollection = matchCollection;
        FullContent = fullContent;
        WhatMatched = matchKind;
    }
    public string Url { get; }
    public string Title { get; }
    public string FullContent { get; }
    public string DateAdded { get; }
    public string Folder { get; }
    public long? SiteContentsId { get; }
    public MatchCollection MatchCollection { get; }
    public MatchKind WhatMatched { get; }
    public IReadOnlyCollection<ContentFragment> GetContentFragments()
    {
        var result = new List<ContentFragment>();

        if (MatchCollection.Count > 0)
        {
            var firstFragment = new ContentFragment
            {
                Fragment = FullContent.Substring(0, MatchCollection[0].Index),
                IsHighlighted = false
            };
            result.Add(firstFragment);
        }

        int? remainingFragmentInBetweenIndex = null;
        foreach (Match m in MatchCollection)
        {
            if (remainingFragmentInBetweenIndex.HasValue)
            {
                var fragmentInBetween = new ContentFragment
                {
                    Fragment = FullContent.Substring(
                        remainingFragmentInBetweenIndex.Value,
                        m.Index - remainingFragmentInBetweenIndex.Value
                    ),
                    IsHighlighted = false
                };
                result.Add(fragmentInBetween);
            }

            var fragmentForMatch = new ContentFragment
            {
                Fragment = FullContent.Substring(m.Index, m.Length),
                IsHighlighted = true
            };
            result.Add(fragmentForMatch);

            remainingFragmentInBetweenIndex = m.Index + m.Length;
        }

        if (remainingFragmentInBetweenIndex.HasValue)
        {

            var lastFragment = new ContentFragment
            {
                Fragment = FullContent.Substring(
                    remainingFragmentInBetweenIndex.Value,
                    FullContent.Length - remainingFragmentInBetweenIndex.Value
                ),
                IsHighlighted = false
            };
            result.Add(lastFragment);
        }

        return result;
    }

    //_________________________________________________________________________

    public class ContentFragment
    {
        public string Fragment { get; init; }
        public bool IsHighlighted { get; init; }
    }

    public enum MatchKind
    {
        None,
        Title,
        Url,
        Content,
        Irrelevant
    }
}
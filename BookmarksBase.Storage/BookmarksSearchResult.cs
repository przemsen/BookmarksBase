using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BookmarksBase.Storage;

public class BookmarkSearchResult(
    string url,
    string title,
    string dateAdded,
    DateTime dateTimeAdded,
    string folder,
    long? contentsId,
    BookmarkSearchResult.MatchKind matchKind,
    MatchCollection matchCollection = null,
    string fullContent = null
)
{
    public string Url { get; } = url;
    public string Title { get; } = title;
    public string FullContent { get; } = fullContent;
    public string DateAdded { get; } = dateAdded;
    public DateTime DateTimeAdded { get; } = dateTimeAdded;
    public string Folder { get; } = folder;
    public long? SiteContentsId { get; } = contentsId;
    public MatchCollection MatchCollection { get; } = matchCollection;
    public MatchKind WhatMatched { get; } = matchKind;
    public IReadOnlyCollection<ContentFragment> GetContentFragments()
    {
        var result = new List<ContentFragment>(capacity: (MatchCollection.Count * 2) + 1);

        if (MatchCollection.Count > 0)
        {
            var firstFragment = new ContentFragment
            {
                Fragment = FullContent[..MatchCollection[0].Index],
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
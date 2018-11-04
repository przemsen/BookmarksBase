using System;

namespace BookmarksBase.Exporter
{
    class Bookmark
    {
        public string Url { get; set; }
        public DateTime DateAdded { get; set; }
        public string Title { get; set; }
        public string SiteContents { get; set; }
    }
}

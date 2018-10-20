using System;

namespace BookmarksBase.Storage
{
    public class Bookmark
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string ParentTitle { get; set; }
        public DateTime DateAdded { get; set; }
        public long SiteContentsId { get; set; }
    }

}

using System.ComponentModel;

namespace BookmarksBase.Storage
{
    public class BookmarkSearchResult : INotifyPropertyChanged
    {
        string _contentExcerpt;

        public BookmarkSearchResult(string url, string title, string contentExcetpt, string dateAdded, long? contentsId)
        {
            Url = url;
            Title = title;
            ContentExcerpt = contentExcetpt;
            DateAdded = dateAdded;
            SiteContentsId = contentsId;
        }
        public string Url { get; set; }
        public string Title { get; set; }
        public string ContentExcerpt
        {
            get { return _contentExcerpt; }
            set
            {
                _contentExcerpt = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContentExcerpt)));
            }
        }
        public string DateAdded { get; set; }
        public long? SiteContentsId { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
    }


}

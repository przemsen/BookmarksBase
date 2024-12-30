using System;
using System.Text.Json.Serialization;

namespace BookmarksBase.Storage;

public class Bookmark
{
    public string Url { get; set; }
    public string Title { get; set; }
    public string ParentTitle { get; set; }
    public DateTime DateAdded { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SiteContentsId { get; set; }

    public override int GetHashCode() => Url.GetHashCode();
    public override bool Equals(object obj) => obj is Bookmark other && Url == other.Url;

}

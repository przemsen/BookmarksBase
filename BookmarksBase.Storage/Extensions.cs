using System;

namespace BookmarksBase.Search.Storage;

public static class Extensions
{
    const string FMT = "yyyy-MM-dd";
    public static string MyToString(this DateTime dt)
    {
        return dt.ToString(FMT);
    }
}

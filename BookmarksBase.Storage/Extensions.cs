using System;

namespace BookmarksBase.Search.Storage;

public static class Extensions
{
    const string FMT = "yyyy-MM-dd";
    public static string ToMyDateTime(this DateTime dt)
    {
        return dt.ToString(FMT);
    }
}

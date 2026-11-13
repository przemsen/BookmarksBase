using System;

namespace BookmarksBase.Storage;

public static class Extensions
{
    const string FMT = "yyyy-MM-dd";

    extension(DateTime dt)
    {
        public string MyToString()
        {
            return dt.ToString(FMT);
        }
    }
}

using System.Collections.Generic;

namespace BookmarksBase.Importer;

class DownloaderSettings
{
    public string UserAgent { get; set; }
    public int Timeout { get; set; }
    public IEnumerable<string> ExceptionalUrls { get; set; }
    public string TempDir { get; set; }
}

class GeneralSettings
{
    public IList<string> MockUrls { get; set; }
    public int LimitOfQueriedBookmarks { get; set; }
    public int SkipQueriedBookmarks { get; set; }
    public bool BackupExistingDatabaseToZip { get; set; }
    public bool ConsoleReadKeyAtTheEnd { get; set; }
    public string DatabaseFileName { get; set; }
}

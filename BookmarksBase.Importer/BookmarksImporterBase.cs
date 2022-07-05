using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BookmarksBase.Importer.BookmarksImporterBase.Constants;

namespace BookmarksBase.Importer;

abstract class BookmarksImporterBase : IDisposable
{
    protected readonly Options _options;

    private readonly object _lck;
    private readonly List<string> _errLog;
    private readonly BookmarksBaseStorageService _storage;
    private readonly Random _random = new ();
    private readonly SemaphoreSlim _throttler;
    private readonly IHttpClientFactory _httpClientFactory;

    public abstract IEnumerable<Bookmark> GetBookmarks(int initialCount = DEFAULT_BOOKMARKS_LIST_CAPACITY);
    public abstract int GetBookmarksCount();

    public record Options(
        int ThrottlerSemaphoreValue,
        int LimitOfQueriedBookmarks,
        int SkipQueriedBookmarks,
        IEnumerable<string> ExceptionalUrls,
        string UserAgent
    );

    public record DownloadResult(
        string DownloadedFileName,
        string ContentsIfProblem,
        bool IsSuccess
    );

    protected BookmarksImporterBase(Options options, BookmarksBaseStorageService storage, IHttpClientFactory httpClientFactory)
    {
        AssertLynxDependencies();
        _options = options;
        _throttler = new SemaphoreSlim(options.ThrottlerSemaphoreValue);
        _lck = new object();
        _errLog = new List<string>();
        _storage = storage;
        _httpClientFactory = httpClientFactory;
    }

    public Task<DownloadResult> DownloadUrlWithEdge(string url) => Task.Run(() =>
    {
        try
        {
            _throttler.Wait();

            Trace.WriteLine($"{GetDateTime()} - Starting with MS Edge: {url} <br />");
            using var msEdge = new Process();
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;

            msEdge.StartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            msEdge.StartInfo.FileName = MS_EDGE_COMMAND;
            msEdge.StartInfo.Arguments =
                MS_EDGE_COMMANDLINE_OPTIONS +
                $"--user-agent=\"{_options.UserAgent}\" \"{url}\"";
            msEdge.StartInfo.UseShellExecute = false;
            msEdge.StartInfo.RedirectStandardOutput = true;
            msEdge.StartInfo.RedirectStandardError = true;
            msEdge.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            msEdge.Start();

            var stdoutString = msEdge.StandardOutput.ReadToEnd();
            msEdge.WaitForExit(WAIT_TIMEOUT_FOR_LYNX);

            if (stdoutString.StartsWith("<html><head></head><body></body></html>"))
            {
                Trace.WriteLine($"{GetDateTime()} - Edge ERROR: {url} <br />");
                return new DownloadResult(null, null, IsSuccess: false);
            }

            var downloadedFileName = GenerateTempFileName();
            File.WriteAllText(downloadedFileName, stdoutString, Encoding.UTF8);

            return new DownloadResult(DownloadedFileName: downloadedFileName, null, IsSuccess: true);
        }
        finally
        {
            _throttler.Release();
        }
    });

    public async Task<DownloadResult> DownloadUrl(string url)
    {
        string downloadedFileName = null;
        var httpClient = _httpClientFactory.CreateClient(DEFAULTHTTPCLIENT);
        await Task.Delay(_random.Next(1000, 3000)).ConfigureAwait(false);

        for (int i = 0; i < DOWNLOAD_RETRY_COUNT; ++i)
        {
            if (i > 0) await Task.Delay(_random.Next(4000, 6000)).ConfigureAwait(false);
            try
            {
                await _throttler.WaitAsync();
                Trace.WriteLine($"{GetDateTime()} - Starting: {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");

                var httpResponse = await httpClient.GetAsync(url);

                Trace.WriteLine($"{GetDateTime()} - OK: {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                if
                (
                    httpResponse.Content.Headers.ContentType.MediaType.Contains("text") is false ||
                    httpResponse.Content.Headers.ContentType.MediaType.Contains("html") is false
                )
                {
                    return new DownloadResult(DownloadedFileName: null, ContentsIfProblem: "Unsupported content type", IsSuccess: false);
                }

                if (((int)httpResponse.StatusCode) >= 300 && ((int)httpResponse.StatusCode) <= 399)
                {
                    var redirectUri = httpResponse.Headers.Location;
                    url = redirectUri.ToString();
                    continue;
                }

                var httpResponseString = Encoding.UTF8.GetString(await httpResponse.Content.ReadAsByteArrayAsync());

                downloadedFileName = GenerateTempFileName();
                File.WriteAllText(downloadedFileName, httpResponseString, Encoding.UTF8);

                break;
            }
            catch (HttpRequestException hre)
            {
                if (hre.StatusCode is not null)
                {
                    lock (_lck)
                    {
                        var statusCodeString = hre.StatusCode.Value.ToString();
                        if (hre.StatusCode == HttpStatusCode.NotFound)
                        {
                            _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ProtocolError {statusCodeString} {FailMarker(DOWNLOAD_RETRY_COUNT - 1)} <br />");
                            break;
                        }
                        else
                        {
                            _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) ProtocolError {statusCodeString} {FailMarker(i)} <br />");
                        }
                    }
                }
                else if (hre.InnerException is SocketException se)
                {
                    lock (_lck)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) SocketException {se.Message} {FailMarker(i)} <br />");
                    }

                }
                else
                {
                    lock (_lck)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) No status code {hre} {FailMarker(i)} <br />");
                    }
                }

                if (i < DOWNLOAD_RETRY_COUNT - 1)
                {
                    Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                }
            }
            catch (Exception e)
            {
                lock (_lck)
                {
                    if (e.InnerException is not null)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) Not HttpRequestException, but inner: {e.InnerException} {FailMarker(i)} <br />");
                    }
                    else
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) Not HttpRequestException, no inner, {e} {FailMarker(i)} <br />");
                    }
                }

                if (i < DOWNLOAD_RETRY_COUNT - 1)
                {
                    Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                }
            }
            finally
            {
                _throttler.Release();
            }

        }

        return new DownloadResult(DownloadedFileName: downloadedFileName, null, IsSuccess: true);
    }

    public long? SaveRenderedContents(DownloadResult downloadResult)
    {
        if (downloadResult.IsSuccess is false)
        {
            return null;
        }

        long? ret = null;

        if (downloadResult.ContentsIfProblem is not null)
        {
            ret = _storage.SaveContents(downloadResult.ContentsIfProblem);
        }
        else
        {
            using var lynx = new Process();
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;

            lynx.StartInfo.WorkingDirectory = currentDir;
            lynx.StartInfo.FileName = Path.Combine(currentDir, LYNX_COMMAND);
            lynx.StartInfo.Arguments = LYNX_COMMANDLINE_OPTIONS + downloadResult.DownloadedFileName;
            lynx.StartInfo.UseShellExecute = false;
            lynx.StartInfo.RedirectStandardOutput = true;
            lynx.StartInfo.RedirectStandardError = true;
            lynx.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            lynx.Start();

            var content = lynx.StandardOutput.ReadToEnd();
            ret = _storage.SaveContents(content);

            lynx.WaitForExit(WAIT_TIMEOUT_FOR_LYNX);
        }

        if (downloadResult.DownloadedFileName is not null)
        {
            File.Delete(downloadResult.DownloadedFileName);
        }

        return ret;
    }

    public void PopulateContentIds(IEnumerable<Bookmark> bookmarksList)
    {
        if (bookmarksList.TryGetNonEnumeratedCount(out int count) is false)
        {
            count = DEFAULT_BOOKMARKS_LIST_CAPACITY;
        }

        var tasks = new List<Task<long?>>(capacity: count);
        var taskBookmarkPairs = new List<(Bookmark Bookmark, Task<long?> Task)>(capacity: count);

        Trace.WriteLine($"{GetDateTime()} Entering main loop <br />");

        foreach (var b in bookmarksList)
        {
            bool useMsEdge =
                _options.ExceptionalUrls is not null && (
                    _options.ExceptionalUrls.Contains("*") ||
                    _options.ExceptionalUrls.Contains(b.Url)
                )
                ;

            var downloadTask = useMsEdge switch
            {
                true => DownloadUrlWithEdge(b.Url),
                _ =>  DownloadUrl(b.Url),
            };

            var lynxTask = downloadTask.ContinueWith(
                downloadResult => SaveRenderedContents(downloadResult.Result),
                TaskContinuationOptions.OnlyOnRanToCompletion
            );
            tasks.Add(lynxTask);
            taskBookmarkPairs.Add((b, lynxTask));
        }

        Trace.WriteLine($"{GetDateTime()} Waiting for completion of all remaining downloads... <br />");
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        Trace.WriteLine($"{GetDateTime()} All downloads completed <br />");

        foreach (var tb in taskBookmarkPairs)
        {
            if (tb.Task.Result == null)
            {
                tb.Bookmark.Title += " (erroneous)";
            }
            else
            {
                tb.Bookmark.SiteContentsId = tb.Task.Result;
            }
        }

        if (_errLog.Count > 0)
        {
            foreach (var _ in _errLog.OrderBy(_ => _))
            {
                Trace.WriteLine(_);
            }
            Trace.WriteLine(_errLog.Count + " errors. ");
        }
    }

    public void Dispose()
    {

    }

    public static string GetDateTime() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff");

    //_________________________________________________________________________

    public static class Constants
    {
        public const string DEFAULTHTTPCLIENT = nameof(DEFAULTHTTPCLIENT);
        public const int DEFAULT_BOOKMARKS_LIST_CAPACITY = 1000;
        public const int WAIT_TIMEOUT_FOR_LYNX = 1000;
        public const int DOWNLOAD_RETRY_COUNT = 3;
        public const int DEFAULT_THROTTLER_VALUE  = 4;

        public const string MS_EDGE_COMMAND = "c:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
        public const string MS_EDGE_COMMANDLINE_OPTIONS =
            "--virtual-time-budget=20000 --timeout=20000 " +
            "--run-all-compositor-stages-before-draw --disable-gpu --headless --dump-dom " +
            "--incognito --disable-sync --window-size=1280,800 --disable-blink-features=AutomationControlled "
            ;

        public const string LYNX_COMMAND = "lynx\\lynx.exe";
        public const string LYNX_COMMANDLINE_OPTIONS =
            "-nolist -nomargins -dump -nonumbers -width=90 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
    }

    //_________________________________________________________________________

    private static string FailMarker(int i)
    {
        if (i == DOWNLOAD_RETRY_COUNT - 1)
        {
            return "<span style='color:red'>&nbsp;Failed</span>";
        }
        else
        {
            return string.Empty;
        }
    }

    private static string GenerateTempFileName() => $"{Guid.NewGuid()}.htm";

    private static void AssertLynxDependencies()
    {
        string dependency = "lynx";
        if (!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dependency)))
            Throw(dependency);

        dependency = "lynx.exe";
        if (CheckIfFileExists(dependency) is false)
            Throw(dependency);

        dependency = "libssl-1_1.dll";
        if (CheckIfFileExists(dependency) is false)
            Throw(dependency);

        dependency = "libcrypto-1_1.dll";
        if (CheckIfFileExists(dependency) is false)
            Throw(dependency);

        dependency = "lynx.cfg";
        if (CheckIfFileExists(dependency) is false)
            Throw(dependency);

        static void Throw(string what) => throw new FileNotFoundException($"Missing Lynx file/directory dependency: {what}");
        static bool CheckIfFileExists(string path) => File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), path));
    }
}

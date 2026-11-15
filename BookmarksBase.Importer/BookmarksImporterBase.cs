using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BookmarksBase.Importer.BookmarksImporterBase.Constants;

namespace BookmarksBase.Importer;

abstract class BookmarksImporterBase : IDisposable
{
    protected readonly Options _options;

    private readonly Lock _lock;
    private readonly List<string> _errLog;
    private readonly BookmarksBaseStorageService _storage;
    private readonly Random _random = new ();
    private readonly SemaphoreSlim _throttler;
    private readonly IHttpClientFactory _httpClientFactory;
    protected Dictionary<StealCookie, string> _cookies = [];

    public abstract void GetCookies();
    public abstract IEnumerable<Bookmark> GetBookmarks(int initialCount = DEFAULT_BOOKMARKS_LIST_CAPACITY);
    public abstract int GetBookmarksCount();

    public record Options(
        int ThrottlerSemaphoreValue,
        int LimitOfQueriedBookmarks,
        int SkipQueriedBookmarks,
        List<string> ExceptionalUrls,
        string UserAgent,
        string TempDir,
        string PlacesFilePath,
        string CookiesFilePath,
        string NodeJSFilePath,
        IEnumerable<StealCookie> CookieStealings
    );

    public union DownloadResult(FileDownloadedResult, AlternativeContentsResult, FailedDownloadResult);
    public sealed record FailedDownloadResult;
    public sealed record FileDownloadedResult(string DownloadedFileName);
    public sealed record AlternativeContentsResult(string AlternativeContents);

    public record StealCookie(string ForUrl, string WhereHostRLike);

    protected BookmarksImporterBase(Options options, BookmarksBaseStorageService storage, IHttpClientFactory httpClientFactory)
    {
        AssertLynxDependencies();
        _options = options;
        _throttler = new SemaphoreSlim(options.ThrottlerSemaphoreValue);
        _lock = new Lock();
        _errLog = [];
        _storage = storage;
        _httpClientFactory = httpClientFactory;
    }

    public Task<DownloadResult> DownloadUrlWithNode(string url) => Task.Run(() =>
    {
        try
        {
            _throttler.Wait();
            Trace.WriteLine($"{GetDateTime()} - Starting with NodeJS: {url} <br />");

            ProcessStartInfo nodeJsPsi = new()
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                FileName = _options.NodeJSFilePath
            };

            nodeJsPsi.ArgumentList.Add("-e");
            nodeJsPsi.ArgumentList.Add(string.Format(NODEJS_INLINE_PROGRAM, url, _options.UserAgent));
            nodeJsPsi.UseShellExecute = false;
            nodeJsPsi.RedirectStandardOutput = true;
            nodeJsPsi.RedirectStandardError = true;
            nodeJsPsi.StandardOutputEncoding = Encoding.UTF8;

            var nodeJsResult = Process.RunAndCaptureText(nodeJsPsi, WAIT_TIMEOUT_FOR_LYNX_TIMESPAN * 10);

            if (nodeJsResult.ExitStatus.ExitCode != 0)
            {
                Trace.WriteLine($"{GetDateTime()} - NodeJS exit code ERROR: {url} <br />");
                return Task.FromResult<DownloadResult>(new FailedDownloadResult());
            }

            var downloadedFileName = GenerateTempFileName();
            File.WriteAllText(downloadedFileName, nodeJsResult.StandardOutput, Encoding.UTF8);

            return Task.FromResult<DownloadResult>(new FileDownloadedResult(downloadedFileName));
        }
        catch (TimeoutException)
        {
            Trace.WriteLine($"{GetDateTime()} - NodeJS process wait ERROR: {url} <br />");
            return Task.FromResult<DownloadResult>(new FailedDownloadResult());
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
        await Task.Delay(_random.Next(1500, 4000)).ConfigureAwait(false);

        if (url.StartsWith("about:"))
        {
            return new AlternativeContentsResult("""Site from "about" protocol""");
        }

        for (int i = 0; i < DOWNLOAD_RETRY_COUNT; ++i)
        {
            downloadedFileName = null;
            if (i > 0) await Task.Delay(_random.Next(4500, 7000)).ConfigureAwait(false);
            try
            {
                await _throttler.WaitAsync();
                Trace.WriteLine($"{GetDateTime()} - Starting: {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");

                HttpResponseMessage httpResponse = null;

                var cookiesKey = _cookies.Keys.SingleOrDefault(x => url.StartsWith(x.ForUrl));
                if (cookiesKey is not null)
                {
                    var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);

                    foreach (var header in httpClient.DefaultRequestHeaders)
                    {
                        requestMessage.Headers.Add(header.Key, header.Value);
                    }
                    requestMessage.Headers.Add("Cookie", _cookies[cookiesKey]);

                    httpResponse = await httpClient.SendAsync(requestMessage);
                }
                else
                {
                    httpResponse = await httpClient.GetAsync(url);
                }

                Trace.WriteLine($"{GetDateTime()} - OK: {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                if
                (
                    httpResponse?.Content?.Headers?.ContentType?.MediaType.Contains("text") is false ||
                    httpResponse?.Content?.Headers?.ContentType?.MediaType.Contains("html") is false
                )
                {
                    return new FailedDownloadResult();
                }

                int statusCode = (int)httpResponse.StatusCode;
                if (statusCode is >= 300 and < 400)
                {
                    if (httpResponse?.Headers?.Location is Uri redirectUri)
                    {
                        url = redirectUri.ToString();
                        continue;
                    }
                    else
                    {
                        using (_lock.EnterScope())
                        {
                            _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) StatusCode {statusCode} but absent location {FailMarker(i)} <br />");
                        }
                        continue;
                    }
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
                    using (_lock.EnterScope())
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
                    using (_lock.EnterScope())
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) SocketException {se.Message} {FailMarker(i)} <br />");
                    }

                }
                else
                {
                    using (_lock.EnterScope())
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) No status code {hre.Message} {FailMarker(i)} {hre} --- with InnerException: --- {hre.InnerException?.ToString()} <br />");
                    }
                }

                if (i < DOWNLOAD_RETRY_COUNT - 1)
                {
                    Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                }
            }
            catch (Exception e)
            {
                using (_lock.EnterScope())
                {
                    if (e.InnerException is TimeoutException)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) Timeout {FailMarker(i)} <br />");
                    }
                    else if (e.InnerException is not null)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) Not HttpRequestException, but inner: {e.InnerException.Message} {FailMarker(i)} <br />");
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

        return downloadedFileName switch
        {
            null => new FailedDownloadResult(),
            _ => new FileDownloadedResult(downloadedFileName)
        };
    }

    public long? SaveRenderedContents(DownloadResult downloadResult)
    {
        long? ret = null;

        if (downloadResult is AlternativeContentsResult altRresult)
        {
            ret = _storage.SaveContents(altRresult.AlternativeContents);
        }
        else if (downloadResult is FileDownloadedResult fileResult)
        {
            ProcessStartInfo lynxPsi = new();
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;

            lynxPsi.WorkingDirectory = currentDir;
            lynxPsi.FileName = Path.Combine(currentDir, LYNX_COMMAND);
            lynxPsi.Arguments = LYNX_COMMANDLINE_OPTIONS + fileResult.DownloadedFileName;
            lynxPsi.UseShellExecute = false;
            lynxPsi.RedirectStandardOutput = true;
            lynxPsi.RedirectStandardError = true;
            lynxPsi.StandardOutputEncoding = Encoding.UTF8;

            var lynxResult = Process.RunAndCaptureText(lynxPsi, WAIT_TIMEOUT_FOR_LYNX_TIMESPAN);

            ret = _storage.SaveContents(lynxResult.StandardOutput);

            File.Delete(fileResult.DownloadedFileName);
        }

        return ret;
    }

    public void DownloadContents(IEnumerable<Bookmark> bookmarksList)
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
            Task<DownloadResult> downloadTask = null;

            if (b.Title.Contains(SKIP_DOWNLOADING_CONTENTS_MARKER))
            {
                Trace.WriteLine($"{GetDateTime()} Skipping {b.Url} because of {SKIP_DOWNLOADING_CONTENTS_MARKER} <br />");
                downloadTask = Task.FromResult<DownloadResult>(new AlternativeContentsResult($"Skipped because of {SKIP_DOWNLOADING_CONTENTS_MARKER} marker"));
            }
            else
            {
                bool useAlternativeDownloader =
                    _options.ExceptionalUrls is [.. var exUrls ] &&
                    exUrls.Any(b.Url.StartsWith);

                downloadTask = useAlternativeDownloader switch
                {
                    true => DownloadUrlWithNode(b.Url),
                    _ => DownloadUrl(b.Url),
                };
            }

            var lynxTask = downloadTask.ContinueWith(
                downloadResult => SaveRenderedContents(downloadResult.Result),
                TaskContinuationOptions.OnlyOnRanToCompletion
            );
            tasks.Add(lynxTask);
            taskBookmarkPairs.Add((b, lynxTask));
        }

        Trace.WriteLine($"{GetDateTime()} Waiting for completion of all remaining downloads... <br />");

        try
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
        catch (TaskCanceledException)
        {
            Trace.WriteLine($"{GetDateTime()} TaskCanceledException thrown <br />");

            var canceledTasks = tasks.Where(t => t.IsCanceled || t.IsFaulted).ToArray();
            if (canceledTasks.Length > 0)
            {
                foreach (var canceledTask in canceledTasks)
                {
                    var pair = taskBookmarkPairs.Single(x => x.Task == canceledTask);
                    Trace.WriteLine($"{GetDateTime()} Task canceled/faulted ${pair.Bookmark.Url} <br />");
                }

                Task.WhenAll(tasks.Where(t => !t.IsCanceled && !t.IsFaulted)).GetAwaiter().GetResult();
            }
        }

        Trace.WriteLine($"{GetDateTime()} All downloads completed <br />");

        foreach (var tb in taskBookmarkPairs.Where(x => !x.Task.IsFaulted && !x.Task.IsCanceled))
        {
            if (tb.Task.Result is null)
            {
                tb.Bookmark.Title = $"{tb.Bookmark.Title} (erroneous)";
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
        public const int DEFAULT_BOOKMARKS_LIST_CAPACITY = 5000;
        public const int WAIT_TIMEOUT_FOR_LYNX = 1000;
        public static readonly TimeSpan WAIT_TIMEOUT_FOR_LYNX_TIMESPAN = TimeSpan.FromMilliseconds(WAIT_TIMEOUT_FOR_LYNX);
        public const int DOWNLOAD_RETRY_COUNT = 3;
        public const int DEFAULT_THROTTLER_VALUE  = 4;

        public const string NODEJS_INLINE_PROGRAM =
            "const url = '{0}'; const fetchOptions = {{ signal: AbortSignal.timeout(7000), method: 'GET', headers: {{ 'User-Agent': '{1}', 'Accept': 'text/html, application/xhtml+xml, text/plain, application/xml'}}, redirect: 'follow'}}; try {{ const response = await fetch(url, fetchOptions); if (!response.ok) {{ console.log('http error'); process.exit(1); }} let rawHtmlInput = await response.text(); console.log(rawHtmlInput); }} catch (error) {{ console.log('fetch error'); process.exit(1); }}";

        public const string LYNX_COMMAND = "lynx\\lynx.exe";
        public const string LYNX_COMMANDLINE_OPTIONS =
            "-nolist -nomargins -dump -nonumbers -width=100 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
        public const string SKIP_DOWNLOADING_CONTENTS_MARKER = "bb:onlytitle";
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

    private string GenerateTempFileName()
    {
        if (!string.IsNullOrEmpty(_options.TempDir))
        {
            return Path.Combine(_options.TempDir, $"{Guid.NewGuid()}.htm");
        }

        return $"{Guid.NewGuid()}.htm";
    }

    private static void AssertLynxDependencies()
    {
        string lynxDirDependency = "lynx";
        if (!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, lynxDirDependency)))
        {
            throw new FileNotFoundException($"Missing directory dependency: {lynxDirDependency}");
        }

        string[] filesDependencies = [
            "lynx.exe",
            "libssl-3.dll",
            "libcrypto-3.dll",
            "lynx.cfg"
        ];

        foreach (var dep in filesDependencies)
        {
            AssertFileDependency(dep, lynxDirDependency);
        }
    }

    protected static void AssertFileDependency(string path, string dir = "")
    {
        var exists = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir, path));
        if (exists is false)
        {
            throw new FileNotFoundException($"Missing file dependency: {dir}\\{path}");
        }
    }
}

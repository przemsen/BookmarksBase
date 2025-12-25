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
        IEnumerable<string> ExceptionalUrls,
        string UserAgent,
        string TempDir,
        string PlacesFilePath,
        string CookiesFilePath,
        string NodeJSFilePath,
        IEnumerable<StealCookie> CookieStealings
    );

    public record DownloadResult(
        string DownloadedFileName,
        string ContentsIfProblem,
        bool IsSuccess
    );

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
            using var nodeJs = new Process();
            var currentDir = AppDomain.CurrentDomain.BaseDirectory;

            nodeJs.StartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            nodeJs.StartInfo.FileName = _options.NodeJSFilePath;
            nodeJs.StartInfo.ArgumentList.Add("-e");
            nodeJs.StartInfo.ArgumentList.Add(string.Format(NODEJS_INLINE_PROGRAM, url, _options.UserAgent));
            nodeJs.StartInfo.UseShellExecute = false;
            nodeJs.StartInfo.RedirectStandardOutput = true;
            nodeJs.StartInfo.RedirectStandardError = true;
            nodeJs.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            nodeJs.Start();

            var stdoutStringTask = nodeJs.StandardOutput.ReadToEndAsync();
            var stdoutTimeOutTask = Task.Delay(10_000);

            DownloadResult timeOutResult = null;

            Task.WhenAny([stdoutStringTask, stdoutTimeOutTask]).ContinueWith(t =>
            {
                if (t == stdoutTimeOutTask)
                {
                    timeOutResult = new DownloadResult(null, null, IsSuccess: false);
                }
            }).GetAwaiter().GetResult();

            if (timeOutResult is not null)
            {
                return timeOutResult;
            }

            nodeJs.WaitForExit(WAIT_TIMEOUT_FOR_LYNX * 5);

            if (nodeJs.ExitCode != 0)
            {
                Trace.WriteLine($"{GetDateTime()} - NodeJS ERROR: {url} <br />");
                return new DownloadResult(null, null, IsSuccess: false);
            }

            nodeJs.Close();

            var downloadedFileName = GenerateTempFileName();
            File.WriteAllText(downloadedFileName, stdoutStringTask.Result, Encoding.UTF8);

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
        await Task.Delay(_random.Next(1000, 3500)).ConfigureAwait(false);
        bool isSuccess = true;

        if (url.StartsWith("about:"))
        {
            return new DownloadResult(DownloadedFileName: null, ContentsIfProblem: "Site from \"about\" protocol", IsSuccess: false);
        }

        for (int i = 0; i < DOWNLOAD_RETRY_COUNT; ++i)
        {
            if (i > 0) await Task.Delay(_random.Next(4000, 6000)).ConfigureAwait(false);
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
                    return new DownloadResult(DownloadedFileName: null, ContentsIfProblem: "Unsupported content type", IsSuccess: false);
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
                isSuccess = true;

                break;
            }
            catch (HttpRequestException hre)
            {
                isSuccess = false;
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
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{DOWNLOAD_RETRY_COUNT}) No status code {hre.Message} {FailMarker(i)} {hre.ToString()} --- with InnerException: --- {hre.InnerException?.ToString()} <br />");
                    }
                }

                if (i < DOWNLOAD_RETRY_COUNT - 1)
                {
                    Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{DOWNLOAD_RETRY_COUNT}) <br />");
                }
            }
            catch (Exception e)
            {
                isSuccess = false;
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

        return new DownloadResult(DownloadedFileName: downloadedFileName, null, IsSuccess: isSuccess);
    }

    public long? SaveRenderedContents(DownloadResult downloadResult)
    {
        long? ret = null;
        if (downloadResult.ContentsIfProblem is not null)
        {
            ret = _storage.SaveContents(downloadResult.ContentsIfProblem);
        }
        else if (downloadResult.IsSuccess is false)
        {
            return null;
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
            bool useAlternativeDownloader =
                _options.ExceptionalUrls is not null &&
                _options.ExceptionalUrls.Any(eu => b.Url.StartsWith(eu));

            var downloadTask = useAlternativeDownloader switch
            {
                true => DownloadUrlWithNode(b.Url),
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
        public const int DEFAULT_BOOKMARKS_LIST_CAPACITY = 2500;
        public const int WAIT_TIMEOUT_FOR_LYNX = 1000;
        public const int DOWNLOAD_RETRY_COUNT = 3;
        public const int DEFAULT_THROTTLER_VALUE  = 4;

        public const string NODEJS_INLINE_PROGRAM =
            "const url = '{0}'; const fetchOptions = {{ method: 'GET', headers: {{ 'User-Agent': '{1}', 'Accept': 'text/html, application/xhtml+xml, text/plain, application/xml'}}, redirect: 'follow'}}; const response = await fetch(url); let rawHtmlInput = await response.text(); console.log(rawHtmlInput);"
            ;

        public const string LYNX_COMMAND = "lynx\\lynx.exe";
        public const string LYNX_COMMANDLINE_OPTIONS =
            "-nolist -nomargins -dump -nonumbers -width=100 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
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
        var exists = File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir), path));
        if (exists is false)
        {
            throw new FileNotFoundException($"Missing file dependency: {dir}\\{path}");
        }
    }
}

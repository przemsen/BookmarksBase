using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace BookmarksBase.Importer
{
    public abstract class BookmarksImporter : IDisposable
    {
        private struct TaskBookmarkPair
        {
            public Bookmark Bookmark;
            public Task<long?> Task;
        }

        private readonly Options _options;
        private readonly object _lck;
        private readonly List<string> _errLog;
        private readonly BookmarksBaseStorageService _storage;
        private readonly UrlEncoder _urlEncoder;

        public abstract IEnumerable<Bookmark> GetBookmarks();
        protected BookmarksImporter(Options options, BookmarksBaseStorageService storage)
        {
            if (!VerifyLynxDependencies())
            {
                throw new FileNotFoundException("Required Lynx files were not found");
            }
            _options = options;
            _lck = new object();
            _errLog = new List<string>();
            _urlEncoder = UrlEncoder.Default;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Ssl3 |
                SecurityProtocolType.SystemDefault |
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls
                ;
            ServicePointManager.DefaultConnectionLimit = int.MaxValue;
            ServicePointManager.ServerCertificateValidationCallback += (s, cert, ch, sec) => { return true; };
            _storage = storage;
        }

        public async Task<long?> Lynx(string url)
        {
            byte[] rawData = null;
            long? ret = null;
            BookmarksBaseWebClient webClient = null;
            bool useFallbackDownloaderInRetry = false;

            for (int i = 0; i < BookmarksImporterConstants.RetryCount; ++i)
            {
                if (i > 0 && !useFallbackDownloaderInRetry) await Task.Delay(2000);
                try
                {
                    Trace.WriteLine($"{GetDateTime()} - Starting{(useFallbackDownloaderInRetry ? " with fallback service" : string.Empty)}: {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                    webClient = new BookmarksBaseWebClient(_options);

                    if (useFallbackDownloaderInRetry)
                    {
                        var fallbackDownloaderUrl = $"{_options.FallbackDownloaderUrl}&url={_urlEncoder.Encode(url)}";
                        rawData = await webClient.DownloadAsync(fallbackDownloaderUrl, smallTimeoutForRetry: i > 0).ConfigureAwait(false);
                    }
                    else
                    {
                        rawData = await webClient.DownloadAsync(url, smallTimeoutForRetry: i > 0).ConfigureAwait(false);
                    }

                    Trace.WriteLine($"{GetDateTime()} - OK: {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                    if
                    (
                        webClient.ResponseHeaders.AllKeys.Any(k => k == "Content-Type") &&
                        !webClient.ResponseHeaders["Content-Type"].Contains("text/") &&
                        !webClient.ResponseHeaders["Content-Type"].Contains("/xhtml")
                    )
                    {
                        return _storage.SaveContents("Not text content type");
                    }

                    var tempFileName = $"{Guid.NewGuid()}.htm";
                    File.WriteAllBytes(tempFileName, rawData);
                    using (Process lynx = new Process())
                    {
                        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                        lynx.StartInfo.WorkingDirectory = currentDir;
                        lynx.StartInfo.FileName = Path.Combine(currentDir, BookmarksImporterConstants.LynxCommand);
                        lynx.StartInfo.Arguments = BookmarksImporterConstants.LynxCommandLineOptions + tempFileName;
                        lynx.StartInfo.UseShellExecute = false;
                        lynx.StartInfo.RedirectStandardOutput = true;
                        lynx.StartInfo.RedirectStandardError = true;
                        lynx.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                        lynx.Start();
                        var content = lynx.StandardOutput.ReadToEnd();
                        ret = _storage.SaveContents(content);
                        lynx.WaitForExit(BookmarksImporterConstants.WaitTimeoutForLynxProcess);
                    }
                    File.Delete(tempFileName);
                    break;
                }
                catch (AggregateException ae)
                {
                    if (ae.InnerException is WebException we)
                    {
                        lock (_lck)
                        {
                            if (we.Status == WebExceptionStatus.ProtocolError)
                            {
                                var statusCode = ((HttpWebResponse)we.Response).StatusCode.ToString();
                                _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) ProtocolError {statusCode} <br />");
                                if (statusCode == "NotFound")
                                {
                                    break;
                                }
                            }
                            else if (we.Status == WebExceptionStatus.ConnectFailure)
                            {
                                _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) ConnectFailure <br />");
                            }
                            else if (we.Status == WebExceptionStatus.SecureChannelFailure || we.Status == WebExceptionStatus.TrustFailure)
                            {
                                _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) SecureChannelFailure - trying fallback download helper service <br />");
                                useFallbackDownloaderInRetry = !string.IsNullOrEmpty(_options.FallbackDownloaderUrl);
                            }
                            else
                            {
                                var status = we.Status.ToString();
                                _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) {status} <br />");
                                if (status == "NameResolutionFailure")
                                {
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        lock (_lck)
                        {
                            _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) {ae.GetType()} (Aggregate) : {ae.Message} <br />");
                        }
                    }

                    if (i < BookmarksImporterConstants.RetryCount - 1)
                    {
                        if (useFallbackDownloaderInRetry)
                        {
                            Trace.WriteLine($"{GetDateTime()} - Retrying with fallback service {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                        }
                        else
                        {
                            Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (_lck)
                    {
                        _errLog.Add($"{GetDateTime()} ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) {e.GetType()}: {e.Message} <br />");
                    }

                    if (i < BookmarksImporterConstants.RetryCount - 1)
                    {
                        Trace.WriteLine($"{GetDateTime()} - Retrying {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                    }
                }
                finally
                {
                    webClient.Dispose();
                }

            }
            return ret;
        }

        public void LoadContents(IEnumerable<Bookmark> list)
        {
            var tasks = new List<Task<long?>>();
            var taskBookmarkPairs = new List<TaskBookmarkPair>();

            Trace.WriteLine($"{GetDateTime()} Entering main loop <br />");

            foreach (var b in list)
            {
                var task = Lynx(b.Url);
                tasks.Add(task);
                taskBookmarkPairs.Add(new TaskBookmarkPair { Bookmark = b, Task = task });
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

            if (_errLog.Any())
            {
                foreach (var _ in _errLog.OrderBy(_ => _))
                {
                    Trace.WriteLine(_);
                }
                Trace.WriteLine(_errLog.Count + " errors. ");
            }
        }

        private bool VerifyLynxDependencies() =>
            Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.exe")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.cfg")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "libbz2.dll"))
            ;

        public void Dispose()
        {

        }

        public static string GetDateTime() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:fff");

        public static class BookmarksImporterConstants
        {
            public const string LynxCommandLineOptions = "-nolist -nomargins -dump -nonumbers -width=90 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
            public const int RetryCount = 3;
        }

        public class Options
        {
            public string FallbackDownloaderUrl { get; set; }
        }
    }
}

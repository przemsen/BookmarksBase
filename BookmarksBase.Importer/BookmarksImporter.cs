using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BookmarksBase.Importer
{
    public abstract class BookmarksImporter : IDisposable
    {
        struct TaskBookmarkPair
        {
            public Bookmark Bookmark;
            public Task<long> Task;
        }

        readonly Options _options;
        readonly object _lck;
        readonly List<string> _errLog;
        readonly BookmarksBaseStorageService _storage;
        readonly SHA1Managed _sha1;

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
            _sha1 = new SHA1Managed();
        }
        private string SHA1Hash(string stringToHash) =>
            BitConverter.ToString(_sha1.ComputeHash(Encoding.UTF8.GetBytes(stringToHash)));

        public async Task<long> Lynx(string url)
        {
            byte[] rawData = null;
            long ret = 0;
            BookmarksBaseWebClient webClient = null;
            var urlHash = SHA1Hash(url).Substring(0, 12).Replace("-", string.Empty);

            for (int i = 0; i < BookmarksImporterConstants.RetryCount; ++i)
            {
                if (i > 0) await Task.Delay(2000);
                try
                {
                    Trace.WriteLine($"{urlHash} - Starting: {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                    webClient = new BookmarksBaseWebClient(_options);
                    rawData = await webClient.DownloadAsync(url, smallTimeoutForRetry: i > 0).ConfigureAwait(false);

                    Trace.WriteLine($"{urlHash} - OK: {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
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
                catch (WebException we)
                {
#if DEBUG
                    Trace.WriteLine($"{urlHash} - Before lck in catch WE {url} <br />");
#endif
                    lock (_lck)
                    {
#if DEBUG
                        Trace.WriteLine($"{urlHash} - After lck in catch WE {url} <br />");
#endif
                        if (we.Status == WebExceptionStatus.ProtocolError)
                        {
                            var statusCode = ((HttpWebResponse)we.Response).StatusCode.ToString();
                            _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> ({i+1}/{BookmarksImporterConstants.RetryCount}) ProtocolError {statusCode} <br />");
                        }
                        else if (we.Status == WebExceptionStatus.ConnectFailure)
                        {
                            _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) ConnectFailure <br />");
                        }
                        else
                        {
                            var status = we.Status.ToString();
                            _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) {status} <br />");
                            if (
                                status == "SecureChannelFailure" ||
                                status == "TrustFailure" ||
                                status == "NameResolutionFailure"
                            )
                            {
                                break;
                            }
                        }
                    }

                    if (i < BookmarksImporterConstants.RetryCount - 1)
                    {
                        Trace.WriteLine($"{urlHash} - Retrying {url} ({i + 1}/{BookmarksImporterConstants.RetryCount}) <br />");
                        continue;
                    }

                    try
                    {
                        ret = _storage.SaveContents($"Error: {we.Status.ToString()}");
                    }
                    catch { Trace.WriteLine($"{urlHash} - SaveContents error {url} <br />"); }
                }
                catch (Exception e)
                {
                    lock (_lck)
                    {
                        _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> ({i + 1}/{BookmarksImporterConstants.RetryCount}) {e.GetType()} : {e.Message} <br />");
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
            var tasks = new List<Task<long>>();
            var taskBookmarkPairs = new List<TaskBookmarkPair>();

            Trace.WriteLine("Entering main loop <br />");

            foreach (var b in list)
            {
                var task = Lynx(b.Url);
                tasks.Add(task);
                taskBookmarkPairs.Add(new TaskBookmarkPair { Bookmark = b, Task = task });
            }

            Trace.WriteLine("Waiting for completion of all remaining downloads... <br />");
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            Trace.WriteLine("All downloads completed <br />");

            foreach (var tb in taskBookmarkPairs)
            {
                if (tb.Task.Result == 0)
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
                    Trace.WriteLine($"{_} <br />");
                }
                Trace.WriteLine(_errLog.Count + " errors. ");
            }
        }

        bool VerifyLynxDependencies() =>
            Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.exe")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.cfg")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "libbz2.dll"))
            ;

        public void Dispose()
        {
            _sha1.Dispose();
        }

        public static class BookmarksImporterConstants
        {
            public const string LynxCommandLineOptions = "-nolist -nomargins -dump -nonumbers -width=90 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
            public const int RetryCount = 3;
        }

        public class Options
        {
            public bool SockProxyFriendly { get; set; }
        }
    }
}

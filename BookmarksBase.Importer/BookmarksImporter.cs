using BookmarksBase.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace BookmarksBase.Importer
{
    public abstract class BookmarksImporter
    {
        readonly Options _options;
        readonly object _lck;
        readonly List<string> _errLog;
        readonly ConcurrentBag<BookmarksBaseWebClient> _webClientPool;
        readonly BookmarksBaseStorageService _storage;

        BookmarksBaseWebClient GetWebClientFromPool()
        {
            if (_webClientPool.TryTake(out BookmarksBaseWebClient wc))
            {
                return wc;
            }
            return new BookmarksBaseWebClient(_options);
        }

        void PutWebClientToPool(BookmarksBaseWebClient wc)
        {
            _webClientPool.Add(wc);
        }

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
            _webClientPool = new ConcurrentBag<BookmarksBaseWebClient>();
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Ssl3 |
                SecurityProtocolType.SystemDefault |
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls
                ;

            _storage = storage;
        }

        public long Lynx(string url)
        {
            byte[] rawData = null;
            long ret = 0;

            try
            {
                var webClient = GetWebClientFromPool();
                rawData = webClient.DownloadData(url);

                Trace.WriteLine($"OK: {url} <br />");
                if
                (
                    webClient.ResponseHeaders.AllKeys.Any(k => k == "Content-Type") &&
                    !webClient.ResponseHeaders["Content-Type"].ToString().Contains("text/") &&
                    !webClient.ResponseHeaders["Content-Type"].ToString().Contains("/xhtml")
                )
                {
                    PutWebClientToPool(webClient);
                    return _storage.SaveContents("Not text content type");
                }
                PutWebClientToPool(webClient);

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
            }
            catch (WebException we)
            {
                lock (_lck)
                {
                    if (we.Status == WebExceptionStatus.ProtocolError)
                    {
                        _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> {((HttpWebResponse)we.Response).StatusCode.ToString()}<br />");
                    }
                    else
                    {
                        _errLog.Add($"ERROR: <a href=\"{url}\">{url}</a> {we.Status.ToString()}<br />");
                    }
                }

                try
                {
                    ret = _storage.SaveContents($"Error: {we.Status.ToString()}");
                }
                catch { }
            }
            catch (Exception e)
            {
                lock (_lck)
                {
                    _errLog.Add(e.ToString());
                }
            }
            return ret;
        }

        public void LoadContents(IEnumerable<Bookmark> list)
        {
            Parallel.ForEach(
                list,
                b =>
                {
                    var siteContetsId = Lynx(b.Url);
                    if (siteContetsId == 0)
                    {
                        b.Title += " (erroneous)";
                    }
                    else
                    {
                        b.SiteContentsId = siteContetsId;
                    }
                }
            );
            if (_errLog.Any())
            {
                _errLog.ForEach(e => { Trace.WriteLine(e); });
                Trace.WriteLine(_errLog.Count + " errors. ");
                _errLog.Clear();
            }
        }

        bool VerifyLynxDependencies() =>
            Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.exe")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.cfg")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "libbz2.dll"))
            ;

        public class BookmarksImporterConstants
        {
            public const string LynxCommandLineOptions = "-nolist -nomargins -dump -nonumbers -width=80 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
        }


        public class Options
        {
            public bool SockProxyFriendly { get; set; }
        }
    }
}

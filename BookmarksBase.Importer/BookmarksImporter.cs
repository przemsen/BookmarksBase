using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private BookmarksBaseWebClient GetWebClientFromPool()
        {
            if (_webClientPool.TryTake(out BookmarksBaseWebClient wc))
            {
                return wc;
            }
            return new BookmarksBaseWebClient(_options);
        }

        private void PutWebClientToPool(BookmarksBaseWebClient wc)
        {
            _webClientPool.Add(wc);
        }

        public abstract IEnumerable<Bookmark> GetBookmarks();
        protected BookmarksImporter(Options options)
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

            if (Directory.Exists("data"))
            {
                Directory.Delete("data", true);
            }
            Directory.CreateDirectory("data");
        }

        public string RemoveIllegalCharacters(string fileName)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());

            foreach (char c in invalid)
            {
                fileName = fileName.Replace(c.ToString(), string.Empty);
            }

            return fileName;
        }

        public string Lynx(string url)
        {
            byte[] rawData = null;

            if (url.Length > 150)
            {
                url = $"{url.Substring(0, 150)}{CreateMD5(url)}";
            }

            var contentFileName = Path.Combine("data", RemoveIllegalCharacters(url) + ".txt");

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
                    File.WriteAllText(contentFileName, "Not text content type");
                    return contentFileName;
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
                    File.WriteAllText(contentFileName, content);
                    //var err = lynx.StandardError.ReadToEnd();
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
                    File.WriteAllText(contentFileName, $"Error: {we.Status.ToString()}");
                }
                catch
                {
                    contentFileName = null;
                }
            }
            catch (Exception e)
            {
                lock (_lck)
                {
                    _errLog.Add(e.ToString());
                }
                contentFileName = null;
            }
            return contentFileName;
        }

        public void LoadContents(IEnumerable<Bookmark> list)
        {
            Parallel.ForEach(
                list,
                b =>
                {
                    var contentsFileName = Lynx(b.Url);
                    if (contentsFileName == null)
                    {
                        b.Title += " (erroneous)";
                    }
                    else
                    {
                        b.ContentsFileName = contentsFileName;
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

        public void SaveBookmarksBase(IEnumerable<Bookmark> list, string outputFile = "bookmarksbase.xml", bool preCache = false)
        {
            var xws = new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };

            using (var writer = XmlWriter.Create(outputFile, xws))
            {
                writer.WriteStartElement("Bookmarks");
                writer.WriteAttributeString("CreationDate", DateTime.Now.ToString());
                foreach (var bookmark in list)
                {
                    writer.WriteStartElement("Bookmark");
                    writer.WriteStartElement("Url");
                    writer.WriteString(bookmark.Url);
                    writer.WriteEndElement();

                    writer.WriteStartElement("Title");
                    writer.WriteString(bookmark.Title);
                    writer.WriteEndElement();

                    writer.WriteStartElement("DateAdded");
                    writer.WriteString(bookmark.DateAdded.ToShortDateString());
                    writer.WriteEndElement();

                    writer.WriteStartElement("ContentsFileName");
                    writer.WriteString(bookmark.ContentsFileName);
                    if (preCache)
                    {
                        File.ReadAllText(bookmark.ContentsFileName);
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            Trace.WriteLine(outputFile + " saved");
        }

        bool VerifyLynxDependencies() =>
            Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.exe")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.cfg")) &&
            File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "libbz2.dll"))
            ;

        protected string CreateMD5(string input)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        public class BookmarksImporterConstants
        {
            public const string LynxCommandLineOptions = "-nolist -nomargins -dump -nonumbers -width=80 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
        }

        public class Bookmark
        {
            public string Url { get; set; }
            public string Title { get; set; }
            public string ContentsFileName { get; set; }
            public DateTime DateAdded { get; set; }
        }

        public class Options
        {
            public bool SockProxyFriendly { get; set; }
        }
    }
}

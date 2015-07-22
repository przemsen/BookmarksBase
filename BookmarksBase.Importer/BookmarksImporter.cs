using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace BookmarksBase.Importer
{
    public abstract class BookmarksImporter
    {
        readonly Options _options;
        readonly object _lck;
        readonly List<string> _errLog;

        public abstract IList<Bookmark> GetBookmarks();
        protected BookmarksImporter(Options options)
        {
            if (!VerifyLynxDependencies())
            {
                throw new FileNotFoundException("Required Lynx files were not found");
            }
            _options = options;
            _lck = new object();
            _errLog = new List<string>();
        }

        public string Lynx(string url)
        {
            string result = String.Empty;
            byte[] rawData = null;
            try
            {
                using (var webClient = new BookmarksBaseWebClient(_options))
                {
                    rawData = webClient.DownloadData(url);
                    Trace.WriteLine("OK: " + url);
                    if
                    (
                        !webClient.ResponseHeaders["Content-Type"].ToString().Contains("text/") &&
                        !webClient.ResponseHeaders["Content-Type"].ToString().Contains("/xhtml")
                    )
                    {
                        result = "[Not text]";
                        return result;
                    }
                }
                var tempFileName = string.Format(@"{0}", Guid.NewGuid()) + ".htm";
                File.WriteAllBytes(tempFileName, rawData);
                using (Process lynx = new Process())
                {
                    lynx.StartInfo.FileName = BookmarksImporterConstants.LynxCommand;
                    lynx.StartInfo.Arguments = BookmarksImporterConstants.LynxCommandLineOptions + tempFileName;
                    lynx.StartInfo.UseShellExecute = false;
                    lynx.StartInfo.RedirectStandardOutput = true;
                    lynx.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                    lynx.Start();
                    result = lynx.StandardOutput.ReadToEnd();
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
                        _errLog.Add("ERROR: " + url + " " + ((HttpWebResponse)we.Response).StatusCode.ToString());
                    }
                    else
                    {
                        _errLog.Add("ERROR: " + url + " " + we.Status.ToString());
                    }
                }
                result = "[Error]";
            }
            return result;
        }

        public void LoadContents(IList<Bookmark> list)
        {
            Parallel.ForEach(
                list,
                b => b.Contents = Lynx(b.Url)
            );
            if (_errLog.Any())
            {
                _errLog.ForEach(e => { Trace.WriteLine(e); });
                Trace.WriteLine(_errLog.Count + " errors. ");
                _errLog.Clear();
            }
        }

        public void SaveBookmarksBase(IList<Bookmark> list, string outputFile = "bookmarksbase.xml")
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

                    writer.WriteStartElement("Content");
                    writer.WriteString(bookmark.Contents);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            Trace.WriteLine(outputFile + " saved");
        }

        bool VerifyLynxDependencies()
        {
            return
                Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx")) &&
                File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.exe")) &&
                File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "lynx.cfg")) &&
                File.Exists(Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lynx"), "libbz2.dll"))
                ;
        }

        public class BookmarksImporterConstants
        {
            public const string LynxCommandLineOptions = " -nolist -nomargins -dump -nonumbers -width=80 -hiddenlinks=ignore -display_charset=UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
        }

        public class Bookmark
        {
            public string Url { get; set; }
            public string Title { get; set; }
            public string Contents { get; set; }
        }

        public class Options
        {
            public bool SockProxyFriendly { get; set; }
        }
    }
}

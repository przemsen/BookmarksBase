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
        private readonly Options _options;

        public abstract IList<Bookmark> GetBookmarks();

        public BookmarksImporter(Options options)
        {
            if (!VerifyLynxDependencies())
            {
                throw new FileNotFoundException("Required Lynx files were not found");
            }
            _options = options;
        }

        public string Lynx(string url)
        {
            String result = String.Empty;
            Byte[] rawData = null;
            try
            {
                using (var webClient = new BookmarksBaseWebClient(_options))
                {
                    rawData = webClient.DownloadData(url);
                    Console.WriteLine("OK: " + url);
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
                if (we.Status == WebExceptionStatus.ProtocolError)
                {
                    Console.WriteLine("ERROR: " + url + " " + ((HttpWebResponse)we.Response).StatusCode.ToString());
                }
                else
                {
                    Console.WriteLine("ERROR: " + url + " " + we.Status.ToString());
                }
                result = "[Error]";
            }
            return result;
        }

        public void LoadContents(IList<Bookmark> list)
        {
            foreach (var b in list)
            {
                b.Contents = Task.Factory.StartNew<string>(() => Lynx(b.Url));
            }
            Task.WaitAll(list.Select(b => b.Contents).ToArray());
        }

        public void SaveBookmarksBase(IList<Bookmark> list, string outputFile = "bookmarksbase.xml")
        {
            XmlWriterSettings xws = new XmlWriterSettings()
            {
                Encoding = Encoding.UTF8,
                Indent = true
            };

            using (var writer = XmlWriter.Create(outputFile, xws))
            {
                writer.WriteStartElement("Bookmarks");
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
                    writer.WriteString(bookmark.Contents.Result);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            Console.WriteLine(outputFile + " saved");
        }

        private bool VerifyLynxDependencies()
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
            public const string LynxCommandLineOptions = " --nolist --dump -display_charset UTF-8 -cfg=lynx\\lynx.cfg ";
            public const string LynxCommand = "lynx\\lynx.exe";
            public const int WaitTimeoutForLynxProcess = 1000;
        }

        public class Bookmark
        {
            public string Url { get; set; }
            public string Title { get; set; }
            public Task<string> Contents { get; set; }
        }

        public class Options
        {
            public bool SockProxyFriendly { get; set; }
        }
    }
}

using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Configuration;

namespace BookmarksBase.Importer
{
    internal static class Program
    {
        public const string DB_FILE_NAME = "BookmarksBase.sqlite";

        private static void Main(string[] args)
        {
            // This is a feature switch pattern. If set, mock urls are retrieved instead of real bookmarks
            const bool debug = false;

            Setup();

            if (!debug)
            {
                ArchiveExistingFiles();
            }

            Trace.WriteLine("Default importer: Fierfox");

            HandleCommandlineArgs(args, out bool dontWait);

            BookmarksBaseStorageService storage = null;
            FirefoxBookmarksImporter fbi = null;
            try
            {
                storage = new BookmarksBaseStorageService(BookmarksBaseStorageService.OperationMode.Writing);
                try
                {
                    fbi = new FirefoxBookmarksImporter(null, storage);
                }
                catch (FileNotFoundException e)
                {
                    Trace.WriteLine(e.Message);
                    storage.Dispose();
                    Environment.Exit(1);
                }

                IEnumerable<Bookmark> bookmarks = null;
                if (debug)
                {
                    bookmarks = GetMockData();
                }
                else
                {
                    bookmarks = fbi.GetBookmarks();
                }

                if (bookmarks == null)
                {
                    return;
                }

                var htmlExporter = new BookmarksHtmlExporter(bookmarks);
                htmlExporter.WriteHtml();

                storage.Init();

                fbi.LoadContents(bookmarks);
                storage.SaveBookmarksBase(bookmarks);

                storage.Commit();

                Trace.WriteLine("</body></html>");

                if (dontWait)
                {
                    return;
                }

                Trace.WriteLine("Press any key to continue...");
            }
            finally
            {
                storage?.Dispose();
                fbi?.Dispose();
            }
            Console.ReadKey();
        }

        private static IEnumerable<Bookmark> GetMockData()
        {
            var ret = new List<Bookmark>
            {
                new Bookmark
                {
                     DateAdded = DateTime.Now,
                     Title = "WP.pl",
                     Url = "https://gunnarpeipman.com/aspnet-core-file-uploads/",
                     ParentTitle = "Folder"
                },
            //    new Bookmark
            //    {
            //         DateAdded = DateTime.Now,
            //         Title = "ONET.pl",
            //         Url = "https://onet.pl",
            //         ParentTitle = "Folder"
            //    },
            //    new Bookmark
            //    {
            //         DateAdded = DateTime.Now,
            //         Title = "o2.pl",
            //         Url = "https://o2.pl",
            //         ParentTitle = "Folder"
            //    }
            };
            return ret;
        }

        private static void Setup()
        {
            var tr1 = new TextWriterTraceListenerWithHtmlFiler(Console.Out);
            var tr2 = new TextWriterTraceListener(File.CreateText("importer.log.htm"));
            tr2.Write(
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset = ""utf-8"">
  <title>BookmarksBase Importer log file</title>
  <style>body{font-family: monospace; font-size: larger; width: 200vw;}</style>
  </head>
  <body>
");
            tr2.Flush();
            Trace.Listeners.Add(tr1);
            Trace.Listeners.Add(tr2);
            Trace.AutoFlush = true;
            ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        }

        private static void ArchiveExistingFiles()
        {
            try
            {
                if (File.Exists(DB_FILE_NAME))
                {
                    if (File.Exists(DB_FILE_NAME + ".zip"))
                    {
                        File.Delete(DB_FILE_NAME + ".zip");
                    }
                    using (var zip = ZipFile.Open(DB_FILE_NAME + ".zip", ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(DB_FILE_NAME, DB_FILE_NAME);

                        Trace.WriteLine("Previous database file has been archived to " + DB_FILE_NAME + ".zip");
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("Exception while trying to archive previous database file:");
                Trace.WriteLine(e.Message);
            }
        }

        private static void HandleCommandlineArgs(string[] args, out bool dontWait)
        {
            dontWait = false;
            if (args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (string.Equals(arg, "/batch", StringComparison.InvariantCultureIgnoreCase))
                    {
                        dontWait = true;
                    }
                    else if (TryParseTimeoutFromCmdlineArg(arg, out int newTimeout))
                    {
                        BookmarksBaseWebClient.CustomTimeout = newTimeout;
                    }
                    else
                    {
                        Trace.WriteLine($"Unrecognized command line argument: {arg}");
                        Environment.Exit(1);
                    }
                }
            }

            //_____________________________________

            bool TryParseTimeoutFromCmdlineArg(string cmdLineArg, out int timeout)
            {
                timeout = 0;

                if (cmdLineArg.StartsWith("/timeout:", StringComparison.InvariantCultureIgnoreCase))
                {
                    var afterSplit = cmdLineArg.Split(':');
                    return int.TryParse(afterSplit[1], out timeout);
                }

                return false;
            }

        }

    }

    internal class TextWriterTraceListenerWithHtmlFiler : TextWriterTraceListener
    {
        private static readonly Regex _htmlFilterRegex = new Regex("<.*?>", RegexOptions.Compiled);

        public TextWriterTraceListenerWithHtmlFiler(TextWriter writer) : base(writer)
        {

        }

        public override void WriteLine(string message)
        {
            message = _htmlFilterRegex.Replace(message, string.Empty);
            base.WriteLine(message);
        }

    }

}

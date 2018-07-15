using System;
using System.Linq;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Collections.Generic;
using BookmarksBase.Storage;

namespace BookmarksBase.Importer
{
    class Program
    {
        public const string DB_FILE_NAME = "BookmarksBase.sqlite";

        static void Main(string[] args)
        {
            var dontWait = false;
            var preCache = dontWait;
            var debug = false;

            Setup();

            if (!debug)
            {
                ArchiveExistingFiles();
            }

            Trace.WriteLine("Default importer: Fierfox");

            var opts = new BookmarksImporter.Options();
            if (args.Any())
            {
                if (args[0] == "/batch")
                {
                    dontWait = preCache = true;
                }
                else
                {
                    Trace.WriteLine("Unrecognized option: " + args[0]);
                    Environment.Exit(1);
                }
            }

            using (var storage = new BookmarksBaseStorageService(BookmarksBaseStorageService.OperationMode.Writing))
            {
                FirefoxBookmarksImporter fbi = null;
                try
                {
                    fbi = new FirefoxBookmarksImporter(opts, storage);
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

                storage.Init();

                fbi.LoadContents(bookmarks);
                storage.SaveBookmarksBase(bookmarks);

                storage.Commit();

                if (dontWait)
                {
                    return;
                }

                Trace.WriteLine("Press any key to continue...");
                Trace.WriteLine("</body></html>");
            }
            Console.ReadKey();
        }

        static IEnumerable<Bookmark> GetMockData()
        {
            var ret = new List<Bookmark>
            {
                new Bookmark
                {
                     DateAdded = DateTime.Now,
                     Title = "WP.pl",
                     Url = "https://wp.pl"
                },
                new Bookmark
                {
                     DateAdded = DateTime.Now,
                     Title = "ONET.pl",
                     Url = "https://onet.pl"
                },
                new Bookmark
                {
                     DateAdded = DateTime.Now,
                     Title = "o2.pl",
                     Url = "https://o2.pl"
                }
            };
            return ret;
        }

        static void Setup()
        {
            var tr1 = new TextWriterTraceListener(Console.Out);
            var tr2 = new TextWriterTraceListener(File.CreateText("log.htm"));
            tr2.Write(
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset = ""utf-8"">
  <title>BookmarksBase Importer log file</title>
  </head>
  <body>
");
            tr2.Flush();
            Trace.Listeners.Add(tr1);
            Trace.Listeners.Add(tr2);
            Trace.AutoFlush = true;
            ServicePointManager.DefaultConnectionLimit = 128;
        }

        static void ArchiveExistingFiles()
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
    }

}

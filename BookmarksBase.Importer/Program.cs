﻿using System;
using System.Linq;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;

namespace BookmarksBase.Importer
{
    class Program
    {
        public const string DB_FILE_NAME = "bookmarksbase.xml";

        static void Main(string[] args)
        {
            Setup();
            ArchiveExistingFiles();

            Trace.WriteLine("Default importer: Fierfox");

            var opts = new BookmarksImporter.Options();
            if (args.Any())
            {
                if (args[0] == "--socksproxyfriendly")
                {
                    opts.SockProxyFriendly = true;
                }
                else
                {
                    Trace.WriteLine("Unrecognized option: " + args[0]);
                    System.Environment.Exit(1);
                }
            }

            FirefoxBookmarksImporter fbi = null;
            try
            {
                fbi = new FirefoxBookmarksImporter(opts);
            }
            catch (FileNotFoundException e)
            {
                Trace.WriteLine(e.Message);
                System.Environment.Exit(1);
            }

            var bookmarks = fbi.GetBookmarks();
            if (bookmarks == null)
            {
                return;
            }
            fbi.LoadContents(bookmarks);
            fbi.SaveBookmarksBase(bookmarks);

            Trace.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        static void Setup()
        {
            var tr1 = new TextWriterTraceListener(Console.Out);
            var tr2 = new TextWriterTraceListener(File.CreateText("log.txt"));
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
                        zip.Dispose();
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

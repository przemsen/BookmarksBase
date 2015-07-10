using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Data.SQLite;
using System.Xml;

namespace BookmarksBase.Importer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Default importer: Fierfox");

            BookmarksImporter.Options opts = new BookmarksImporter.Options();
            opts.SockProxyFriendly = true; // TODO
            if (args.Any())
            {
                if (args[0] == "--socksproxyfriendly")
                {
                    opts.SockProxyFriendly = true;
                }
                else
                {
                    Console.WriteLine("Unrecognized option: " + args[0]);
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
                Console.WriteLine(e.Message);
                System.Environment.Exit(1);
            }

            var bookmarks = fbi.GetBookmarks();
            if (bookmarks == null)
            {
                return;
            }
            fbi.LoadContents(bookmarks);
            fbi.SaveBookmarksBase(bookmarks);

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }


}

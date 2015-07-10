using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BookmarksBase.Search.Engine;

namespace BookmarksBase.Search.Cli
{
    public static class Program
    {
        static void Main(string[] args)
        {
            string find = null;
            Operation operation = Operation.DefaultCli;

            if (args.Length > 0)
            {
                if (args[0] == "--finddeadbookmarks")
                {
                    operation = Operation.FindDeadBookmarks;
                }
                else
                {
                    find = args[0];
                    operation = Operation.SingleRun;
                }
            }

            var bookmarksEngine = new BookmarksBaseSearchEngine();

            var bookmarks = bookmarksEngine.GetBookmarks();

            if(operation == Operation.DefaultCli || operation == Operation.SingleRun)
            {
                Action prompt = () =>
                {
                    if (operation == Operation.DefaultCli)
                    {
                        Console.Write("? ");

                        find = Console.ReadLine();
                        if (find == ":q") System.Environment.Exit(0);
                        Console.Clear();
                    }
                };

                while (true)
                {
                    prompt();
                    var result = bookmarksEngine.DoSearch(bookmarks, find);
                    foreach (var r in result)
                    {
                        Console.WriteLine("*** " + r.Title);
                        Console.WriteLine("### " + r.Url);
                        Console.WriteLine("    " + r.ContentExcerpt);
                        Console.WriteLine();
                    }
                    if (operation == Operation.SingleRun) System.Environment.Exit(0);
                }
            }
            else if (operation == Operation.FindDeadBookmarks)
            {
                var result = bookmarksEngine.DoDeadSearch(bookmarks);
                foreach (var r in result)
                {
                    Console.WriteLine("### " + r.Url);
                }
            }

        }
    }

    public enum Operation
    {
        FindDeadBookmarks,
        SingleRun,
        DefaultCli
    }
}

using System;
using System.IO;

namespace BookmarksBase.Exporter
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0 && !File.Exists(BookmarksExporter.DEFAULT_DB_FILENAME))
            {
                Console.WriteLine($"Please provide either path to {BookmarksExporter.DEFAULT_DB_FILENAME} file as argument or place the file in the current directory");
                Environment.Exit(1);
            }
            var exporter = new BookmarksExporter();
            exporter.Run(args.Length == 1 ? args[0] : null);
        }
    }
}

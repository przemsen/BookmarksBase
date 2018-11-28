using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace BookmarksBase.Exporter
{
    class Program
    {
        static void Main(string[] args)
        {
            var tr1 = new TextWriterTraceListener(Console.Out);
            var tr2 = new TextWriterTraceListener(File.CreateText("log.txt"));
            Trace.Listeners.Add(tr1);
            Trace.Listeners.Add(tr2);
            Trace.AutoFlush = true;

            if (args.Length == 0 && !File.Exists(BookmarksExporter.DEFAULT_DB_FILENAME))
            {
                Trace.WriteLine($"Please provide either path to {BookmarksExporter.DEFAULT_DB_FILENAME} file as argument or place the file in the current directory");
                Environment.Exit(1);
            }
            var exporter = new BookmarksExporter();

            try
            {
                exporter.Run(args.Length == 1 ? args[0] : null);
            }
            catch (Exception e)
            {
                MessageBox.Show($"{e.Message}, {e.StackTrace}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }
    }
}

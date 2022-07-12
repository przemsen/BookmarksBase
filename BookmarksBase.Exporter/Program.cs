using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BookmarksBase.Exporter;

static class Program
{
    static internal ExporterSettings Settings;

    static void Main()
    {
        var configurationBuilder =  new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.exporter.json", optional: false)
            ;
        var configuration = configurationBuilder.Build();

        var settings = new ExporterSettings();
        configuration.GetRequiredSection("Settings").Bind(settings);
        Settings = settings;

        Trace.WriteLine($"BookmarksBase Exporter {GetAssemblyVersionInfo()}");

        var tr1 = new TextWriterTraceListener(Console.Out);
        var tr2 = new TextWriterTraceListener(File.CreateText("exporter.log.txt"));
        Trace.Listeners.Add(tr1);
        Trace.Listeners.Add(tr2);
        Trace.AutoFlush = true;

        if (!File.Exists(Settings.DatabaseFileName))
        {
            Trace.WriteLine($"Database file does not exist");
            Environment.Exit(1);
        }
        var exporter = new BookmarksExporter();

        try
        {
            exporter.Run(Settings.DatabaseFileName);
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
            File.WriteAllText(Settings.ErrorNotificationsFilePath, e.ToString());
            Environment.Exit(1);
        }
    }

    static string GetAssemblyVersionInfo()
    {
        var theAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fvi = FileVersionInfo.GetVersionInfo(theAssembly.Location);
        var inforVersion = theAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        return $"{fvi.FileMajorPart}.{fvi.FileMinorPart} — Build {inforVersion}";
    }
}
class ExporterSettings
{
    public string ConnectionString { get; set; }
    public string DatabaseFileName { get; set; }
    public string ErrorNotificationsFilePath { get; set; }
}

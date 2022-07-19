using BookmarksBase.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BookmarksBase.Importer;

static class Program
{
    #region Main workflow

    static void Main(string[] args)
    {

        #region Setups

        var configurationBuilder =  new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.importer.json", optional: false)
            ;
        var configuration = configurationBuilder.Build();

        var settingsDownloader = new DownloaderSettings();
        var settingsGeneral = new GeneralSettings();
        configuration.GetRequiredSection(nameof(DownloaderSettings)).Bind(settingsDownloader);
        configuration.GetRequiredSection(nameof(GeneralSettings)).Bind(settingsGeneral);

        var services = new ServiceCollection();
        services.AddHttpClient(
            BookmarksImporterBase.Constants.DEFAULTHTTPCLIENT,
            c =>
            {
                c.DefaultRequestHeaders.Add("User-Agent", settingsDownloader.UserAgent);
                c.DefaultRequestHeaders.Accept.Add(new("text/html"));
                c.DefaultRequestHeaders.Accept.Add(new("application/xhtml+xml"));
                c.DefaultRequestHeaders.Accept.Add(new("application/xml"));
                c.DefaultRequestHeaders.AcceptEncoding.Add(new("gzip"));
                c.DefaultRequestHeaders.AcceptEncoding.Add(new("deflate"));
                c.DefaultRequestHeaders.AcceptLanguage.Add(new("pl"));
                c.DefaultRequestHeaders.AcceptLanguage.Add(new("en-US"));
                c.Timeout = TimeSpan.FromMilliseconds(settingsDownloader.Timeout);

            }
        ).ConfigurePrimaryHttpMessageHandler(
            () =>
            new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseDefaultCredentials = true,
                MaxAutomaticRedirections = 5,
                UseCookies = true,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                CookieContainer = new(),
                AutomaticDecompression = System.Net.DecompressionMethods.All
            }
        );

        var servicesProvider = services.BuildServiceProvider();
        var httpClientFactory = servicesProvider.GetRequiredService<IHttpClientFactory>();

        SetupTraceListeners();

        #endregion

        Trace.WriteLine($"BookmarksBase Importer {GetAssemblyVersionInfo()} <br />");
        Trace.WriteLine("Default importer: Firefox <br />");

        BookmarksBaseStorageService storage = null;
        FirefoxBookmarksImporter fbi = null;

        var options = new BookmarksImporterBase.Options(
            ThrottlerSemaphoreValue: BookmarksImporterBase.Constants.DEFAULT_THROTTLER_VALUE,
            LimitOfQueriedBookmarks: settingsGeneral.LimitOfQueriedBookmarks,
            SkipQueriedBookmarks: settingsGeneral.SkipQueriedBookmarks,
            ExceptionalUrls: settingsDownloader.ExceptionalUrls,
            UserAgent: settingsDownloader.UserAgent
        );

        try
        {
            storage = new BookmarksBaseStorageService(
                BookmarksBaseStorageService.OperationMode.Writing,
                settingsGeneral.DatabaseFileName
            );
            try
            {
                fbi = new FirefoxBookmarksImporter(options, storage, httpClientFactory);
            }
            catch (FileNotFoundException e)
            {
                Trace.WriteLine(e.Message);
                storage.Dispose();
                Environment.Exit(1);
            }

            IEnumerable<Bookmark> bookmarks = null;
            if (settingsGeneral.MockUrls?.Count is int count && count > 0)
            {
                bookmarks = (
                    from url in settingsGeneral.MockUrls
                    select new Bookmark
                    {
                        Url = url,
                        DateAdded = DateTime.Now,
                        ParentTitle = "Parent",
                        Title = "Title"
                    }
                ).ToList();
            }
            else
            {
                if (settingsGeneral.BackupExistingDatabaseToZip)
                {
                    BackupExistingDatabaseToZip(settingsGeneral.DatabaseFileName);
                }

                int initialCount = settingsGeneral.LimitOfQueriedBookmarks switch
                {
                    >0 => settingsGeneral.LimitOfQueriedBookmarks,
                    _ => fbi.GetBookmarksCount()
                };

                bookmarks = fbi.GetBookmarks(initialCount);
                new BookmarksJsonExporter(bookmarks).WriteJson();
            }

            storage.PrepareTablesAndBeginTransaction();

            fbi.PopulateContentIds(bookmarks);
            storage.SaveBookmarksBase(bookmarks);

            storage.Commit();

            Trace.WriteLine("</body></html>");

            if (settingsGeneral.ConsoleReadKeyAtTheEnd is false)
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

        //_________________________________________________________________________

        static void SetupTraceListeners()
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
        }
    }

    #endregion

    #region Helper methods

    static void BackupExistingDatabaseToZip(string dbFileName)
    {
        try
        {
            if (File.Exists(dbFileName))
            {
                if (File.Exists(dbFileName + ".zip"))
                {
                    File.Delete(dbFileName + ".zip");
                }
                using var zip = ZipFile.Open(dbFileName + ".zip", ZipArchiveMode.Create);
                zip.CreateEntryFromFile(dbFileName, dbFileName);

                Trace.WriteLine("Previous database file has been archived to " + dbFileName + ".zip");
            }
        }
        catch (Exception e)
        {
            Trace.WriteLine("Exception while trying to archive previous database file:");
            Trace.WriteLine(e.Message);
        }
    }

    static string GetAssemblyVersionInfo()
    {
        var theAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fvi = FileVersionInfo.GetVersionInfo(theAssembly.Location);
        var inforVersion = theAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        return $"{fvi.FileMajorPart}.{fvi.FileMinorPart} — Build {inforVersion}";
    }

    #endregion

}

class TextWriterTraceListenerWithHtmlFiler : TextWriterTraceListener
{
    private static readonly Regex _htmlFilterRegex = new ("<.*?>", RegexOptions.Compiled);

    public TextWriterTraceListenerWithHtmlFiler(TextWriter writer) : base(writer)
    {

    }

    public override void WriteLine(string message)
    {
        message = _htmlFilterRegex.Replace(message, string.Empty);
        base.WriteLine(message);
    }

}

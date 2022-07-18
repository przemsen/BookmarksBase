using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BookmarksBase.Search;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string ERROR_LOG_FILENAME = "search.error.log.txt";

    private readonly BookmarksBaseStorageService _storage;
    private readonly BookmarksBaseSearchEngine _searchEngine;
    private readonly Brush _highlightBrush;
    private readonly Paragraph _helpMessageParagrapg;

    private List<Run> _highlightedRuns;
    private List<Run>.Enumerator _highlightedRunsEnumerator;

    private ListSortDirection _urlSortDirection = ListSortDirection.Ascending;
    private ListSortDirection _dateSortDirection = ListSortDirection.Ascending;

    public MainWindow()
    {
        InitializeComponent();
        var theApp = (App)Application.Current;

        try
        {
            if (string.IsNullOrEmpty(theApp.Settings.DatabasePath))
            {
                MessageBox.Show(
                    "Database path must be specified",
                    "Fatal error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current.Shutdown();
            }

            _storage = new BookmarksBaseStorageService(
                BookmarksBaseStorageService.OperationMode.Reading,
                theApp.Settings.DatabasePath
            );

            _searchEngine = new BookmarksBaseSearchEngine(
                (long siteContentsId) => _storage.LoadContents(siteContentsId),
                _storage.LoadedBookmarks
            );

            DisplayInitialStatus(
                _storage.LastModifiedOn.ToString(),
                _storage.LoadedBookmarks.Count
            );

            _highlightBrush = (Brush)(new BrushConverter().ConvertFrom(theApp.Settings.MatchHighlightColor));
            Resources["HighlightBrush"] = _highlightBrush;

            var helpRun = new Run(BookmarksBaseSearchEngine.HelpMessage);
            helpRun.Foreground = Brushes.DarkGray;
            _helpMessageParagrapg = new Paragraph(helpRun);

            ResultsFlowDocument.Blocks.Clear();
            ResultsFlowDocument.Blocks.Add(_helpMessageParagrapg);
        }
        catch (Exception e)
        {
            MessageBox.Show(
                e.Message,
                e.GetType().FullName,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Application.Current.Shutdown();
        }

        FindTxt.Focus();
    }

    private void UrlLst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBlock)
        {
            var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
            if (currentBookmark == null) return;
            Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = currentBookmark.Url });
        }

    }

    private void UrlLst_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
            if (currentBookmark == null) return;
            Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = currentBookmark.Url });
        }
    }

    private void UrlLst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UrlLst.SelectedItem is BookmarkSearchResult b)
        {
            TitleTxt.Text = b.Title;

            if (b.WhatMatched == BookmarkSearchResult.MatchKind.Content)
            {
                RenderResults();

            }
            else if (b.SiteContentsId is long siteContentsId)
            {
                var fullContent = b.FullContent ?? _storage.LoadContents(siteContentsId);
                ResultsFlowDocument.Blocks.Clear();
                ResultsFlowDocument.Blocks.Add(new Paragraph(new Run(fullContent)));
            }

        }
    }

    private async void FindTxt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            await DoSearch();
        }
    }

    private void DisplayInitialStatus(string creationDate, int count)
    {
        var theAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fvi = FileVersionInfo.GetVersionInfo(theAssembly.Location);
        var inforVersion = theAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        this.Title += $" — Build {inforVersion}";
        string status = $"Loaded {count} bookmarks, created at {creationDate}. Application version {fvi.FileMajorPart}.{fvi.FileMinorPart}";
        StatusTxt.Text = status;
    }

    private void UrlLst_HeaderClick(object sender, RoutedEventArgs e)
    {
        GridViewColumnHeader column = e.OriginalSource as GridViewColumnHeader;
        if (column == null) return;

        ICollectionView resultDataView = CollectionViewSource.GetDefaultView(UrlLst.ItemsSource);
        if (resultDataView == null) return;

        resultDataView.SortDescriptions.Clear();

        switch (column.Content.ToString())
        {
            case "URL":
                _urlSortDirection = (_urlSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending);
                resultDataView.SortDescriptions.Add(new SortDescription("Url", _urlSortDirection));
                break;

            case "DateAdded":
                _dateSortDirection = (_dateSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending);
                resultDataView.SortDescriptions.Add(new SortDescription("DateAdded", _dateSortDirection));
                break;

            default:
                break;
        }

    }
    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (UrlLst.SelectedItem is BookmarkSearchResult b)
        {
            Clipboard.SetText(b.Url);
        }
    }

    public async Task DoSearch()
    {

        if (
            FindTxt.Text
                .ToLower(Thread.CurrentThread.CurrentCulture)
                .StartsWith("help:", StringComparison.CurrentCulture)

            ||

            FindTxt.Text
                .ToLower(Thread.CurrentThread.CurrentCulture)
                .StartsWith("?", System.StringComparison.CurrentCulture)
        )
        {

            ResultsFlowDocument.Blocks.Clear();
            ResultsFlowDocument.Blocks.Add(_helpMessageParagrapg);

            return;
        }

        ResultsFlowDocument.Blocks.Clear();
        Stopwatch stopWatch = Stopwatch.StartNew();
        long searchEngineElapsedMs = 0l;

        try
        {
            var textToSearch = FindTxt.Text;
            FindTxt.IsEnabled = false;

            var searchEngineStopWatch = Stopwatch.StartNew();
            var result = await Task.Run(() =>
            {
                return _searchEngine
                   .DoSearch(textToSearch)
                   ;
            });
            searchEngineStopWatch.Stop();
            searchEngineElapsedMs = searchEngineStopWatch.ElapsedMilliseconds;

            if (GroupedViewCheckBox.IsChecked is true)
            {
                UrlLst.DataContext = result;
            }
            else
            {
                UrlLst.DataContext = result.OrderByDescending(b => b.DateAdded);
            }

            if (!result.Any())
            {
                ResultsFlowDocument.Blocks.Clear();
                ResultsFlowDocument.Blocks.Add(new Paragraph(new Run("No results")));
            }
        }
        catch (BookmarksBaseSearchEngine.RegExException ree)
        {
            MessageBox.Show(
                "Cannot create regular expression from given pattern. " + ree.InnerException.Message,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            File.WriteAllText(ERROR_LOG_FILENAME, ree.StackTrace);
            if (ree.InnerException != null)
            {
                File.AppendAllText(ERROR_LOG_FILENAME, "---" + Environment.NewLine + ree.InnerException.StackTrace);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(
                "An unexpected error occurred when performing search. " + e.InnerException?.Message,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            File.WriteAllText(ERROR_LOG_FILENAME, e.StackTrace);
            if (e.InnerException != null)
            {
                File.AppendAllText(ERROR_LOG_FILENAME, "---" + Environment.NewLine + e.InnerException.StackTrace);
            }
        }
        finally
        {
            FindTxt.IsEnabled = true;
            FindTxt.Focus();
            stopWatch.Stop();
            StatusTxt.Text = $"Finished in total {stopWatch.ElapsedMilliseconds} ms. Search engine: {searchEngineElapsedMs} ms. UI: {stopWatch.ElapsedMilliseconds - searchEngineElapsedMs} ms";
        }
    }

    private void winMain_Closing(object sender, CancelEventArgs e)
    {
        if (_storage != null)
        {
            _storage.Dispose();
        }
    }

    private void RenderResults()
    {
        var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
        if (currentBookmark is null)
            return;

        var contentFragments = currentBookmark.GetContentFragments();

        var paragraph = new Paragraph();

        _highlightedRuns = new();

        if (currentBookmark.MatchCollection.Count > 1)
        {
            NextMatchButton.Visibility = Visibility.Visible;
            MatchCountTextBlock.Visibility = Visibility.Visible;
            MatchCountTextBlock.Text = currentBookmark.MatchCollection.Count.ToString();
        }
        else
        {
            MatchCountTextBlock.Visibility = Visibility.Hidden;
            NextMatchButton.Visibility = Visibility.Hidden;
            MatchCountTextBlock.Text = null;
        }

        foreach (var cf in contentFragments)
        {
            var run = new Run(cf.Fragment);

            if (cf.IsHighlighted)
            {
                run.Background = _highlightBrush;
                _highlightedRuns.Add(run);
            }
            else
            {
                run.Background = null;
            }

            paragraph.Inlines.Add(run);
        }

        _highlightedRunsEnumerator = _highlightedRuns.GetEnumerator();

        ResultsFlowDocument.Blocks.Clear();
        ResultsFlowDocument.Blocks.Add(paragraph);

        IterateNextResultHighlight();

    }

    private void winMain_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            IterateNextResultHighlight();
        }
    }

    private void IterateNextResultHighlight()
    {
        if (!_highlightedRunsEnumerator.MoveNext())
        {
            _highlightedRunsEnumerator.Dispose();
            _highlightedRunsEnumerator = _highlightedRuns.GetEnumerator();
            if (!_highlightedRunsEnumerator.MoveNext())
            {
                return;
            }
        }

        ResultsRichTxt.Selection.Select(_highlightedRunsEnumerator.Current.ContentStart, _highlightedRunsEnumerator.Current.ContentEnd);
        ResultsRichTxt.Focus();
    }

    private void NextMatchButton_Click(object sender, RoutedEventArgs e)
    {
        IterateNextResultHighlight();
    }

    private async void DoSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!FindTxt.IsEnabled)
        {
            return;
        }

        await DoSearch();
    }
}

public class MatchCountToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        < 3 => 14,
        >= 3 and < 5 => 10,
        _ => 8
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}

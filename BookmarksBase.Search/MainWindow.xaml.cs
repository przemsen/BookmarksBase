using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using System.Windows.Media.Imaging;

namespace BookmarksBase.Search;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string ERROR_LOG_FILENAME = "search.error.log.txt";

    private readonly SearchIconAdorner _searchIconAdorner;
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

            DisplayStatus(
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

        var layer = AdornerLayer.GetAdornerLayer(FindTxt);
        _searchIconAdorner = new SearchIconAdorner(FindTxt);
        layer.Add(_searchIconAdorner);
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
            if (b.WhatMatched == BookmarkSearchResult.MatchKind.Content)
            {
                RenderResults();
                return;
            }
            else if (b.WhatMatched == BookmarkSearchResult.MatchKind.Title)
            {
                TitleTxt.Background = _highlightBrush;
            }

            if (b.SiteContentsId is long siteContentsId)
            {
                var fullContent = b.FullContent ?? _storage.LoadContents(siteContentsId);
                ResultsFlowDocument.Blocks.Clear();
                ResultsFlowDocument.Blocks.Add(new Paragraph(new Run(fullContent)));
            }

            TitleTxt.Text = b.Title;
        }
        else
        {
            TitleTxt.Background = null;
        }
    }

    private async void FindTxt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            await DoSearch();
        }
    }

    private void DisplayStatus(string creationDate, int count)
    {
        var theAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fvi = FileVersionInfo.GetVersionInfo(theAssembly.Location);
        var inforVersion = theAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        this.Title += $" — Build {inforVersion}";
        string status = $"Loaded {count} bookmarks, created at {creationDate}. Application version {fvi.FileMajorPart}.{fvi.FileMinorPart}";
        StatusTxt.Text = status;
    }

    private void UrlLst_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            GridView view = UrlLst.View as GridView;
            view.Columns[0].Width = Width - 130;

        }
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

        try
        {
            var textToSearch = FindTxt.Text;
            FindTxt.IsEnabled = false;

            var result = await Task.Run(() =>
            {
                return _searchEngine
                   .DoSearch(textToSearch)
                   .OrderByDescending(b => b.DateAdded)
                   ;
            });

            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(result);
            PropertyGroupDescription groupDescription = new PropertyGroupDescription(nameof(BookmarkSearchResult.Folder));
            view.GroupDescriptions.Add(groupDescription);

            UrlLst.ItemsSource = view;

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
            _searchIconAdorner.ResetHighlight();
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
        }
        else
        {
            NextMatchButton.Visibility = Visibility.Hidden;
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
}

#region Adorner

public class SearchIconAdorner : Adorner
{
    private readonly VisualCollection _visualCollection;
    private readonly Image _image;
    private readonly Border _border;
    private readonly SolidColorBrush _bckgrBrush;
    private readonly SolidColorBrush _borderBrush;
    private readonly SolidColorBrush _bckgrBrush2;
    private readonly SolidColorBrush _borderBrush2;

    private bool IsFindTxtEnabled => ((TextBox)AdornedElement).IsEnabled;

    public SearchIconAdorner(UIElement adornedElement) : base(adornedElement)
    {
        _bckgrBrush = new SolidColorBrush(Colors.Gold);
        _bckgrBrush.Opacity = 0.3d;
        _borderBrush = new SolidColorBrush(Colors.DarkGoldenrod);

        _bckgrBrush2 = new SolidColorBrush(Colors.White);
        _bckgrBrush2.Opacity = 0;
        _borderBrush2 = new SolidColorBrush(Colors.White);

        _visualCollection = new VisualCollection(this);
        _image = new Image();
        _border = new Border();
        _border.Padding = new Thickness(1);

        _border.BorderBrush = _borderBrush2;
        _border.BorderThickness = new Thickness(1);
        _border.Background = _bckgrBrush2;

        _border.SnapsToDevicePixels = true;

        _border.Child = _image;
        _image.Source = new BitmapImage(new Uri("pack://application:,,,/BookmarksBase.Search;component/searchicon.png"));
        _image.Stretch = Stretch.Uniform;

        _visualCollection.Add(_border);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        if (!IsFindTxtEnabled)
        {
            return;
        }

        var b = VisualTreeHelper.GetChild(e.OriginalSource as DependencyObject, 0);

        if (b is Border)
        {
            _border.BorderBrush = _borderBrush;
            _border.BorderThickness = new Thickness(1);
            _border.Background = _bckgrBrush;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!IsFindTxtEnabled)
        {
            return;
        }

        var b = VisualTreeHelper.GetChild(e.OriginalSource as DependencyObject, 0);

        if (b is Border)
        {
            _border.BorderBrush = _borderBrush2;
            _border.BorderThickness = new Thickness(1);
            _border.Background = _bckgrBrush2;
        }

    }

    protected override async void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!IsFindTxtEnabled)
        {
            return;
        }

        if (Application.Current.Windows[0] is MainWindow w)
        {
            await w.DoSearch();
        }
    }

    public void ResetHighlight()
    {
        _border.BorderBrush = _borderBrush2;
        _border.BorderThickness = new Thickness(1);
        _border.Background = _bckgrBrush2;
    }

    protected override Visual GetVisualChild(int index) => _visualCollection[index];
    protected override int VisualChildrenCount => _visualCollection.Count;
    protected override Size ArrangeOverride(Size finalSize)
    {
        double controlWidth = AdornedElement.RenderSize.Width;
        double controlHeight = AdornedElement.RenderSize.Height;
        double imgSize = controlHeight - 9;
        _border.Width = imgSize;
        _border.Height = imgSize;
        _border.Arrange(new Rect(controlWidth - imgSize * 1.3, imgSize / 4, imgSize, imgSize));
        return finalSize;
    }

}

#endregion

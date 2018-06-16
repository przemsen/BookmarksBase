﻿using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BookmarksBase.Search.Engine;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookmarksBase.Storage;
using System.IO;

namespace BookmarksBase.Search
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        SearchIconAdorner _searchIconAdorner;
        BookmarksBaseSearchEngine _bookmarksEngine;
        IList<Bookmark> _bookmarks;
        BookmarksBaseStorageService _storage;

        ListSortDirection _urlSortDirection = ListSortDirection.Ascending;
        ListSortDirection _dateSortDirection = ListSortDirection.Ascending;

        public static readonly DependencyProperty DisplayHelp =
            DependencyProperty.Register(
                "DisplayHelp",
                typeof(bool),
                typeof(Window),
                new FrameworkPropertyMetadata(true)
            );

        public IEnumerable<BookmarkSearchResult> ViewModel { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            _storage = new BookmarksBaseStorageService(BookmarksBaseStorageService.OperationMode.Reading);
            _bookmarksEngine = new BookmarksBaseSearchEngine(_storage);
            try
            {
                _bookmarks = _storage.LoadBookmarksBase();
                DisplayStatus(_storage.LastModifiedOn.ToString(), _bookmarks.Count, _bookmarks.Count(b => b.SiteContentsId == 0));
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "An error occured while loading BookmarksBase.sqlite file. Did you run BookmarksBase.Importer?",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                Application.Current.Shutdown();
            }
            var layer = AdornerLayer.GetAdornerLayer(FindTxt);
            _searchIconAdorner = new SearchIconAdorner(FindTxt);
            layer.Add(_searchIconAdorner);
            SetValue(DisplayHelp, true);
            FindTxt.Focus();
        }

        void UrlLst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock)
            {
                var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
                if (currentBookmark == null) return;
                Process.Start(currentBookmark.Url);
            }

        }

        void UrlLst_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
                if (currentBookmark == null) return;
                Process.Start(currentBookmark.Url);
            }
        }

        async void FindTxt_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                await DoSearch();
            }
        }

        void FindTxt_GotFocus(object sender, RoutedEventArgs e)
        {
            if ((bool)GetValue(DisplayHelp))
            {
                FindTxt.Text = string.Empty;
            }
            SetValue(DisplayHelp, false);
        }

        void DisplayStatus(string creationDate, int count, int erroneous)
        {
            var myself = System.Reflection.Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(myself.Location);
            string status = null;
            if (erroneous == 0)
            {
                status = $"Loaded {count} bookmarks, created at {creationDate}. Application version {fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}";
            }
            else
            {
                status = $"Loaded {count} bookmarks ({erroneous} warnings), created at {creationDate}. Application version {fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}";
            }
            StatusTxt.Text = status;
        }

        private void UrlLst_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                GridView view = this.UrlLst.View as GridView;
                view.Columns[0].Width = this.Width - 130;

            }
        }

        private void UrlLst_HeaderClick(object sender, RoutedEventArgs e )
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
                const string helpMsg = @"Available modifiers:
'all:'         -- loads all bookmarks sorted by date descending
'casesens:'    -- makes search case sensitive
'help:' or '?' -- displays this text
'inurl:'       -- searches only in the urls
'intitle:'     -- searches only in the titles
";
                ExcerptTxt.Text = helpMsg;
                return;
            }

            ExcerptTxt.Text = null;

            try
            {
                var textToSearch = FindTxt.Text;
                FindTxt.IsEnabled = false;

                var result = await Task.Run(() =>
                {
                    return _bookmarksEngine
                       .DoSearch(_bookmarks, textToSearch)
                       .OrderByDescending(b => b.DateAdded)
                       ;
                });

                DataContext = result;

                if (!result.Any())
                {
                    ExcerptTxt.Text = "No results";
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
                File.WriteAllText("error.log", ree.StackTrace);
                if (ree.InnerException != null)
                {
                    File.AppendAllText("error.log", "---" + Environment.NewLine + ree.InnerException.StackTrace);
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
                File.WriteAllText("error.log", e.StackTrace);
                if (e.InnerException != null)
                {
                    File.AppendAllText("error.log", "---" + Environment.NewLine + e.InnerException.StackTrace);
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
            _storage.Dispose();
        }
    }

    #region Adorner

    public class SearchIconAdorner : Adorner
    {
        readonly VisualCollection _visualCollection;
        readonly Image _image;
        readonly Border _border;
        readonly SolidColorBrush _bckgrBrush;
        readonly SolidColorBrush _borderBrush;

        readonly SolidColorBrush _bckgrBrush2;
        readonly SolidColorBrush _borderBrush2;

        bool IsFindTxtEnabled => ((TextBox)AdornedElement).IsEnabled;

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

            if (b is Border border)
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

            if (b is Border border)
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

            var w = ((App)Application.Current).Windows[0] as MainWindow;
            if (w != null)
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
            _border.Arrange(new Rect(controlWidth - imgSize * 1.3, imgSize / 4 , imgSize, imgSize));
            return finalSize;
        }

    }

    #endregion

}

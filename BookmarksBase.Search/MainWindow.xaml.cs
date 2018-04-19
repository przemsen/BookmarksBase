using System;
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

namespace BookmarksBase.Search
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        BookmarksBaseSearchEngine _bookmarksEngine;
        Bookmark[] _bookmarks;

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
            _bookmarksEngine = new BookmarksBaseSearchEngine();
            try
            {
                _bookmarksEngine.Load();
                _bookmarks = _bookmarksEngine.GetBookmarks();
                DisplayStatus(_bookmarksEngine.GetCreationDate(), _bookmarks.Length);
                _bookmarksEngine.Release();
            }
            catch (Exception)
            {
                MessageBox.Show(
                    "An error occured while loading bookmarksbase.xml file. Did you run BookmarksBase.Importer?",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                Application.Current.Shutdown();
            }
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(FindTxt);
            layer.Add(new SearchIconAdorner(FindTxt));
            SetValue(DisplayHelp, true);
            FindTxt.Focus();
        }

        void UrlLst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBlock)
            {
                var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
                if (currentBookmark == null) return;
                System.Diagnostics.Process.Start(currentBookmark.Url);
            }

        }

        void UrlLst_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
                if (currentBookmark == null) return;
                System.Diagnostics.Process.Start(currentBookmark.Url);
            }
        }

        void FindTxt_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                DoSearch();
            }
        }

        void FindTxt_GotFocus(object sender, RoutedEventArgs e)
        {
            if ((bool)GetValue(DisplayHelp) == true)
            {
                FindTxt.Text = string.Empty;
            }
            SetValue(DisplayHelp, false);
        }

        void DisplayStatus(string creationDate, int count)
        {
            System.Reflection.Assembly myself = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(myself.Location);
            var status = string.Format(
                "Loaded {0} bookmarks, created at {1}. Application version {2}.{3}.{4}",
                count,
                creationDate,
                fvi.FileMajorPart,
                fvi.FileMinorPart,
                fvi.FileBuildPart
            );
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
            }

        }
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (UrlLst.SelectedItem is BookmarkSearchResult b)
            {
                Clipboard.SetText(b.Url);
            }
        }

        public void DoSearch()
        {
            try
            {
                DataContext =
                    _bookmarksEngine
                        .DoSearch(_bookmarks, FindTxt.Text)
                        .OrderByDescending(b => b.DateAdded)
                        ;
            }
            catch (ArgumentException ae)
            {
                MessageBox.Show(
                    "Cannot create regular expression from given pattern. " + ae.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
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

            _border.BorderBrush = _borderBrush2;
            _border.BorderThickness = new Thickness(1);
            _border.Background = _bckgrBrush2;

            _border.Child = _image;
            _image.Source = new BitmapImage(new Uri("pack://application:,,,/BookmarksBase.Search;component/searchicon.png"));
            _image.Stretch = Stretch.Fill;

            _visualCollection.Add(_border);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
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
            var b = VisualTreeHelper.GetChild(e.OriginalSource as DependencyObject, 0);
            if (b is Border border)
            {
                _border.BorderBrush = _borderBrush2;
                _border.BorderThickness = new Thickness(1);
                _border.Background = _bckgrBrush2;
            }
        
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            var w = ((App)Application.Current).Windows[0] as MainWindow;
            if (w != null)
            {
                w.DoSearch();
            }
        }

        protected override Visual GetVisualChild(int index) => _visualCollection[index];
        protected override int VisualChildrenCount => _visualCollection.Count;
        protected override Size ArrangeOverride(Size finalSize)
        {
            double controlWidth = AdornedElement.RenderSize.Width;
            double controlHeight = AdornedElement.RenderSize.Height;
            double imgSize = controlHeight - 10;
            _border.Width = imgSize;
            _border.Height = imgSize;
            _border.Arrange(new Rect(controlWidth - imgSize * 1.3, imgSize / 4 , imgSize, imgSize));
            return finalSize;
        }

    }

    #endregion

}

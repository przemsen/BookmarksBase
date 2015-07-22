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

namespace BookmarksBase.Search
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        BookmarksBaseSearchEngine _bookmarksEngine;
        Bookmark[] _bookmarks;

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
            try
            {
                if (e.Key == Key.Return)
                {
                    DataContext = _bookmarksEngine.DoSearch(_bookmarks, FindTxt.Text);
                }
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
    }

    #region Adorner

    public class SearchIconAdorner : Adorner
    {
        readonly VisualCollection _visualCollection;
        readonly Image _image;

        public SearchIconAdorner(UIElement adornedElement) : base(adornedElement)
        {
            _visualCollection = new VisualCollection(this);
            _image = new Image();
            _image.Source = new BitmapImage(new Uri("pack://application:,,,/BookmarksBase.Search;component/searchicon.png"));
            _image.Stretch = Stretch.Uniform;
            _visualCollection.Add(_image);
        }

        protected override Visual GetVisualChild(int index)
        {
            return _visualCollection[index];
        }
        protected override int VisualChildrenCount
        {
            get { return _visualCollection.Count; }
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            double controlWidth = AdornedElement.RenderSize.Width;
            double controlHeight = AdornedElement.RenderSize.Height;
            double imgSize = controlHeight - 10;
            _image.Width = imgSize;
            _image.Height = imgSize;
            _image.Arrange(new Rect(controlWidth - imgSize * 1.3, imgSize / 4 , imgSize, imgSize));
            return finalSize;
        }

    }

    #endregion

}

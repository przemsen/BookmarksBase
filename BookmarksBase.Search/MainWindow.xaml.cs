using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;

using BookmarksBase.Search.Engine;

namespace BookmarksBase.Search
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private BookmarksBaseSearchEngine _bookmarksEngine;
        private IEnumerable<XElement> _bookmarks;

        public IEnumerable<BookmarkSearchResult> ViewModel { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            _bookmarksEngine = new BookmarksBaseSearchEngine(
                new BookmarksBaseSearchEngine.Options()
                {
                    ExcerptContextLength = 50
                }
            );
            try
            {
                _bookmarks = _bookmarksEngine.GetBookmarks();
            }
            catch
            {
                MessageBox.Show(
                    "An error occured while loading bookmarksbase.xml file. Did you run BookmarksBase.Importer?",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                Application.Current.Shutdown();
            }
            FindTxt.Focus();
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            DataContext = _bookmarksEngine.DoSearch(_bookmarks, FindTxt.Text);
        }

        private void UrlLst_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var currentBookmark = UrlLst.SelectedItem as BookmarkSearchResult;
            if (currentBookmark == null) return;
            System.Diagnostics.Process.Start(currentBookmark.Url);
        }

    }
}

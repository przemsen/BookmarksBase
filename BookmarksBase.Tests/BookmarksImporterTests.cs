using NUnit.Framework;

namespace BookmarksImporter.Tests
{
    using BookmarksBase.Importer;
    using System;
    using System.Net;

    [TestFixture]
    public class BookmarksImporterTests
    {
        [Test]
        public void LynxWorks()
        {
                       
            var fbi = new FirefoxBookmarksImporter(
                new BookmarksImporter.Options() { SockProxyFriendly = true  }
            );
            var result = fbi.Lynx("https://github.com/Windows-XAML/Template10/wiki");
        }
    }
}

﻿using System;
using NUnit.Framework;

namespace BookmarksImporter.Tests
{
    using BookmarksBase.Importer;

    [TestFixture]
    public class BookmarksImporterTests
    {
        [Test]
        public void LynxWorks()
        {
            FirefoxBookmarksImporter fbi = new FirefoxBookmarksImporter(
                new BookmarksImporter.Options() { SockProxyFriendly = true  }
            );
            var result = fbi.Lynx("http://localhost:5000");
        }
    }
}

using NUnit.Framework;

namespace BookmarksImporter.Tests
{
    using BookmarksBase.Importer;
    using BookmarksBase.Storage;
    using System;
    using System.Collections.Generic;
    using System.Net;

    [TestFixture]
    public class BookmarksStorageTests
    {
        [Test]
        public void Storage_saves_contents()
        {
            var sut = new BookmarksBaseStorageService(BookmarksBaseStorageService.OperationMode.Writing);
            var ret = sut.SaveContents("aaa");
        }

        [Test]
        public void Storage_saves_bookmarks()
        {
            var sut = new BookmarksBaseStorageService(BookmarksBaseStorageService.OperationMode.Writing);

            var data = new List<Bookmark>()
            {
                new Bookmark
                {
                    Url = "http://wp.pl",
                    DateAdded = DateTime.Now,
                    SiteContentsId = 1
                }
            };

            sut.SaveBookmarksBase(data);



        }


    }
}
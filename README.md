#### BookmarksBase is a tool that allows you to search within your bookmarks including entire contents of web sites, addresses and titles. ####

![Screen](https://cloud.githubusercontent.com/assets/6310503/8783838/abc8b3a8-2f1e-11e5-9f50-3ce8f4ec5f0a.png)

----------

# 1. Instructions for the user #

In application package you get:

- `BookmarksBase.Importer` &mdash; **First run this tool** to generate database of bookmarks along with their contents. It produces `bookmarksbase.xml` file. If everything works fine, it will print long list of addresses followed by: 

> bookmarksbase.xml file saved

The `log.txt` file is also created and contains exactly the same contents as these printed in the window.

- `BookmarksBase.Search.exe` &mdash; **Main application intended for searching.** It basically searches within `bookmarksbase.xml` generated by the importer.
- `BookmarksBase.Search.dll` &mdash; Library file containing implementation of search.
- `lynx` folder &mdash; Files of Lynx browser used for HTML parsing required by the importer.
- `x64` and `x86` folders &mdash; Folders with runtime libraries required by the importer.
- `System.Data.SQLite.dll` &mdash; Runtime library required by the importer.

**[Download link](https://github.com/przemsen/BookmarksBase/releases/download/1.2.4/BookmarksBase.zip)**

- You can use TAB to switch between search box and results list.
- You can move up and down using keyboard when results list is focused.
- Search is case insensitive and a text you enter is compiled as a regular expression.
- If you wish to express any number of any characters use `.*`.
- Double click on results list item opens it in a web browser.
- Enter while focused on results list also opens selected item in a web browser.

# 2. Changelog #

Current state of source code may be newer than version of application in precompiled package. I update package when significant changes are introduced.

## 1.2.4 &mdash; 2015-07-20 ##
- Importer more precisely mimics Firefox web browser

## 1.2.3 &mdash; 2015-07-17 ##
- Fixed performance overhead bug inadvertently introduced in 1.2.2

## 1.2.2 &mdash; 2015-07-16 ##
- Fixed bug when multi word pattern does not match when it spans 2 lines in rendered contents

## 1.2.1 &mdash; 2015-07-15 ##
- Search engine performance improvements.
- Importer now creates zipped copy of previous contents.

## 1.2.0 &mdash; 2015-07-14 ##
- Importer now saves the date of import and Search is able to display it

## 1.1.1 &mdash; 2015-07-13 ##
- Proper error handling on regex error
- Minor UI improvements

## 1.1.0 &mdash; 2015-07-11 ##
- Improved concurrency while downloading sites
- Regex support
- Small improvements in BookmarksBase.Search interface
- Logging importer output written to file
- Erroneous urls are now printed at the end of the log 

## 1.0.0 &mdash; 2015-05-16 ##

- First working version is published.

# 3. Source code structure #

- `BookmarksBase.Search` &mdash; WPF application to perform searches. 
- `BookmarksBase.Search.Cli` &mdash; Command line version of searching application. It is expected to have more functionalities than WPF version.
- `BookmarksBase.Engine` &mdash; DLL library containing actual implementation of search. It is shared between both `Search` applications.
- `BookmarksBase.Importer` &mdash; Command line utility to import bookmarks from web browser and pull text from web pages.
- `BookmarksBase.Tests` &mdash; Unit tests.

# 4. Release notes #

- All applications require .NET Framework version 4.5.2.
- Currently only Firefox web browser is supported. Other implementations can easily be created by inheriting `BookmarksImporter` class (following open-closed principle). 
- HTML parsing is conducted by [Lynx](http://lynx.isc.org) and thus its binaries are required to run the importer.

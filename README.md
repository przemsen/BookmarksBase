#### BookmarksBase is a tool that allows for searching in your bookmarks including entire contents of web sites, addresses and titles. ####

![Screen](https://user-images.githubusercontent.com/6310503/39720331-4e9b4d32-523c-11e8-9b4a-85032ac392d7.png)

## Currently supports only Firefox ##

# How to start? #

- Download current release: **[Download link](https://github.com/przemsen/BookmarksBase/releases/latest)**
- Run `BookmarksBase.Importer`. It reads all your bookmarks, downloads clean text from the bookmarked websites and stores in current folder as plain text files.
- Enjoy using `BookmarksBase.Search.exe`. It supports regular expressions and searches in the indexed web page contents. Type `help:` to view some extra features. 

----------

# 1. Instructions for the user #

In application package you get:

- `BookmarksBase.Importer.exe` &mdash; **First run this tool** to generate database of bookmarks along with their contents. 
- `BookmarksBase.Search.exe` &mdash; **Main application intended for searching.** It searches through text from bookmarked websites

- When application starts, searching box is already focused. Start typing the text and press Enter
- You can use TAB to switch between search box and results list
- You can move up and down using keyboard when results list is focused
- Search is case insensitive and a text you enter is compiled as a regular expression
- If you wish to express any number of any characters use `.*`
- Double click on results list item opens it in a web browser
- Enter while focused on results list also opens selected item in a web browser

# 2. Source code structure #

- `BookmarksBase.Search` &mdash; WPF application to perform searches
- `BookmarksBase.Importer` &mdash; Command line utility to import bookmarks from web browser and pull text from web pages
- `BookmarksBase.Exporter` &mdash; Command line utility to export boomarks from SQLite database into MS SQL database
- `BookmarksBase.Storage` &mdash; DLL library with the implementation of the search as well as i/o with database file

# 3. Release notes #

- All applications are compiled for .NET Framework version 4.7.1
- Currently only Firefox web browser is supported. Other implementations can easily be created by inheriting `BookmarksImporter` class (following open-closed principle)
- HTML parsing is done with [Lynx](http://lynx.isc.org) and thus its binaries (included) are required to run the importer

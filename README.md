#### BookmarksBase is a tool that allows for searching in your bookmarks including entire contents of web sites, addresses and titles. ####

![Screen](/screenshot.jpg?raw=true)

## Currently supports only Firefox ##

# How to start? #

- Run `BookmarksBase.Importer`. It reads all your bookmarks, downloads clean text from the bookmarked websites and stores in current folder SQLite database file.
- Please note, that some anti-virus software may consider the importer as suspicious. It opens many connections in parallel to download web sites, which is quite unusual. I personally added whole folder to Windows Defender exceptions.
- Enjoy using `BookmarksBase.Search.exe`. It supports regular expressions and searches in the indexed web page contents. Type `help:` to view some extra features. 

----------

# 1. Instructions for the user #

- `BookmarksBase.Importer.exe` &mdash; **First run this tool** to generate database of bookmarks along with their contents. 
- `BookmarksBase.Search.exe` &mdash; **Main application intended for searching.** It searches through text from bookmarked websites

- When application starts, searching box is already focused. Start typing the text and press Enter
- You can use TAB to switch between search box and results list
- You can move up and down using keyboard when results list is focused
- Search is case insensitive and a text you enter is compiled as a regular expression
- If you wish to express any number of any characters use `.*`
- Double click on results list item opens it in a web browser
- Enter while focused on results list also opens selected item in a web browser
- You can switch between grouped and ungrouped view by using option in the context menu

# 2. Source code structure #

- `BookmarksBase.Search` &mdash; WPF application to perform searches
- `BookmarksBase.Importer` &mdash; Command line utility to import bookmarks from web browser and pull text from web pages
- `BookmarksBase.Storage` &mdash; DLL library with the implementation of the search as well as i/o with database file
- *Experimental* `BookmarksBase.Exporter` &mdash; Command line utility to export boomarks from SQLite database into MS SQL database. You probably will not need this.

# 3. Release notes #

- All applications are written for .NET6 (at the moment)
- Currently only Firefox web browser is supported. Other implementations can easily be created by inheriting `BookmarksImporter` class (following open-closed principle)
- HTML parsing is done with [Lynx](http://lynx.isc.org) and thus its binaries (included) are required to run the importer

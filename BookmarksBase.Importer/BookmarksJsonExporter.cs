using BookmarksBase.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookmarksBase.Importer;

public class BookmarksJsonExporter
{

    readonly IEnumerable<Bookmark> _bookmarks;

    public BookmarksJsonExporter(IEnumerable<Bookmark> bookmarks)
    {
        _bookmarks = bookmarks;
    }

    public void WriteJson()
    {
        var sb = new StringBuilder(capacity: 500_000);

        var jsonOptions = new JsonSerializerOptions();
        jsonOptions.Converters.Add(new DateTimeConverter());
        jsonOptions.WriteIndented = false;

        var json = JsonSerializer.Serialize(_bookmarks, jsonOptions);

        var timeStamp = $"const bookmarksTimeStamp = '{DateTime.Now.ToString(DateTimeConverter.DATE_FORMAT)}';";
        sb.Append("const bookmarksArray = ");
        sb.Append(json);
        sb.AppendLine(";");
        sb.AppendLine(timeStamp);

        File.WriteAllText("bookmarksArray.js", sb.ToString());
    }

    //_________________________________________________________________________

    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public const string DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";

        public override void Write(Utf8JsonWriter writer, DateTime date, JsonSerializerOptions options)
            => writer.WriteStringValue(date.ToString(DATE_FORMAT));

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.ParseExact(reader.GetString(), DATE_FORMAT, null);
    }
}

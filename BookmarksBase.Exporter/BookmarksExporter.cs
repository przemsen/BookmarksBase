﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;

namespace BookmarksBase.Exporter
{

    public class BookmarksExporter
    {
        private SqlConnection _sqlOutConn;
        private SqlCommand _sqlOutCmd;
        private SQLiteConnection _sqliteInConn;
        private SQLiteCommand _sqliteInCmd;
        public const string DEFAULT_DB_FILENAME = "BookmarksBase.sqlite";

        public void Run(string dbFileName)
        {
            bool bookmarksAvailable = false;
            var connectionString = ConfigurationManager.ConnectionStrings["dst"].ConnectionString;

            using (_sqlOutConn = new SqlConnection(connectionString))
            using (_sqlOutCmd = new SqlCommand("SET ANSI_WARNINGS OFF; truncate table [dbo].[Bookmark];", _sqlOutConn))
            using (_sqliteInConn = new SQLiteConnection($"Data Source={dbFileName ?? DEFAULT_DB_FILENAME}; Read Only = True;"))
            using (_sqliteInCmd = new SQLiteCommand(SELECT_SQL, _sqliteInConn))
            {
                _sqliteInConn.Open();
                _sqlOutConn.Open();
                var tran = _sqlOutConn.BeginTransaction();
                _sqlOutCmd.Transaction = tran;
                _sqlOutCmd.ExecuteNonQuery();
                Trace.WriteLine("Target table truncated");

                _sqlOutCmd.CommandText = INSERT_SQL_4;

                var buffer = new List<Bookmark>();
                int counter = 0;

                using (var dataReader = _sqliteInCmd.ExecuteReader())
                {
                    while (dataReader.Read())
                    {
                        bookmarksAvailable = true;
                        _sqlOutCmd.Parameters.Clear();

                        var bookmark = new Bookmark
                        {
                            Url = dataReader.GetString(0),
                            DateAdded = dataReader.GetDateTime(1),
                            Title = dataReader.GetString(2),
                            SiteContents = dataReader.GetString(3)
                        };
                        buffer.Add(bookmark);

                        if (buffer.Count % 4 == 0)
                        {
                            AddBookmarkAsSqlParams(_sqlOutCmd.Parameters, buffer, 0);
                            AddBookmarkAsSqlParams(_sqlOutCmd.Parameters, buffer, 1);
                            AddBookmarkAsSqlParams(_sqlOutCmd.Parameters, buffer, 2);
                            AddBookmarkAsSqlParams(_sqlOutCmd.Parameters, buffer, 3);
                            _sqlOutCmd.ExecuteNonQuery();
                            buffer.Clear();
                            Trace.WriteLine($"Exported next 4 records ({counter})");
                        }
                        ++counter;
                    }

                    _sqlOutCmd.Parameters.Clear();

                    if (buffer.Count > 0)
                    {
                        for (int i = 0; i < buffer.Count; ++i)
                        {
                            AddBookmarkAsSqlParams(_sqlOutCmd.Parameters, buffer, i);
                        }

                        switch (buffer.Count)
                        {
                            case 1: _sqlOutCmd.CommandText = INSERT_SQL_1; break;
                            case 2: _sqlOutCmd.CommandText = INSERT_SQL_2; break;
                            case 3: _sqlOutCmd.CommandText = INSERT_SQL_3; break;
                            default: throw new InvalidOperationException($"Unexpected buffer count: {buffer.Count}");
                        }

                        _sqlOutCmd.ExecuteNonQuery();
                        Trace.WriteLine($"Exported remainder of {buffer.Count} records");
                    }
                }

                _sqliteInConn.Close();

                _sqlOutCmd.CommandText = INSERT_METADATA_SENTINEL_SQL;
                _sqlOutCmd.Parameters.Clear();
                _sqlOutCmd.Parameters.Add("@p0", SqlDbType.DateTime).Value = new DateTime(1900, 1, 1);
                _sqlOutCmd.Parameters.Add("@p1", SqlDbType.NVarChar).Value = METADATA_SENTINEL_TITLE;
                _sqlOutCmd.Parameters.Add("@p2", SqlDbType.NVarChar).Value = METADATA_SENTINEL_URL;
                _sqlOutCmd.Parameters.Add("@p3", SqlDbType.NVarChar).Value = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm")}, {counter}";
                _sqlOutCmd.ExecuteNonQuery();

                if (bookmarksAvailable)
                {
                    tran.Commit();
                    Trace.WriteLine("Commit");
                }
                else
                {
                    tran.Rollback();
                    Trace.WriteLine("Rollback");
                }

                _sqlOutConn.Close();
                Trace.WriteLine("Finished");
            }
        }

        private void AddBookmarkAsSqlParams(SqlParameterCollection spc, List<Bookmark> bl, int index)
        {
            spc.Add($"@p{4 * index}", SqlDbType.NVarChar).Value = bl[index].Url;
            spc.Add($"@p{4 * index + 1}", SqlDbType.DateTime).Value = bl[index].DateAdded;
            spc.Add($"@p{4 * index + 2}", SqlDbType.NVarChar).Value = bl[index].Title;
            spc.Add($"@p{4 * index + 3}", SqlDbType.NVarChar).Value = bl[index].SiteContents;
        }

        #region Constants with SQL

        private const string SELECT_SQL = @"
select b.Url, b.DateAdded, b.Title, sc.Text
from Bookmark b
join SiteContents sc on b.SiteContentsId = sc.Id
order by b.DateAdded desc;
";
        private const string INSERT_SQL_1 = @"
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p0, @p1, @p2, @p3);
";
        private const string INSERT_SQL_2 = @"
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p0, @p1, @p2, @p3);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p4, @p5, @p6, @p7);

";
        private const string INSERT_SQL_3 = @"
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p0, @p1, @p2, @p3);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p4, @p5, @p6, @p7);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p8, @p9, @p10, @p11);

";
        private const string INSERT_SQL_4 = @"
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p0, @p1, @p2, @p3);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p4, @p5, @p6, @p7);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p8, @p9, @p10, @p11);
insert into Bookmark (Url, DateAdded, Title, SiteContents) values (@p12, @p13, @p14, @p15);
";
        private const string INSERT_METADATA_SENTINEL_SQL = @"
insert into [dbo].[Bookmark] 
    (DateAdded, Title, Url, SiteContents) 
values 
    (@p0, @p1, @p2, @p3);
";
        private const string METADATA_SENTINEL_TITLE = "Metadata sentinel: export date, number of bookmarks";
        private const string METADATA_SENTINEL_URL = "080b8253-307d-430a-bcca-9abea46e093a";

        #endregion

    }
}

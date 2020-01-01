CREATE TABLE [Bookmark]
(
    [Id] int IDENTITY(1,1) NOT NULL,
    [DateAdded] datetime NOT NULL,
    [Url] VARCHAR(2000) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL,
    [Title] VARCHAR(1000) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL,
    [SiteContents] VARCHAR(MAX) COLLATE Latin1_General_100_CI_AS_SC_UTF8),
    CONSTRAINT [PK_Bookmark] PRIMARY KEY CLUSTERED 
    (
        [DateAdded] desc, [Id] asc
    )
)



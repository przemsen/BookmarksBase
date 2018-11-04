CREATE TABLE [Bookmark]
(
    [Id] int IDENTITY(1,1) NOT NULL,
    [DateAdded] datetime NOT NULL,
    [Url] nvarchar(2000) NOT NULL,
    [Title] nvarchar(1000) NOT NULL,
    [SiteContents] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Bookmark] PRIMARY KEY CLUSTERED 
    (
        [DateAdded] desc, [Id] asc
    )
)



CREATE PROCEDURE [dbo].[Search] @searchString nvarchar(100)
as
select top 100
    Url
    ,DateAdded
    ,Title
    ,( 
        case when patindex(@searchString, Title) > 0 then
            N''
        else
        substring(
            SiteContents,
            case
                when
                    patindex(@searchString, SiteContents) - 120 > 0 
                then
                    patindex(@searchString, SiteContents) - 120 
                else 
                    0 
                end,
            case 
                when 
                    (len(@searchString) + (2 * 120)) > len(SiteContents)
                then 
                    len(SiteContents)
                else
                    (len(@searchString) + (2 * 120))
                end
        )
        end
     ) e
from dbo.Bookmark 
where 
    patindex(@searchString, Title) > 0 or patindex(@searchString, SiteContents) > 0 and
    Url <> N'080b8253-307d-430a-bcca-9abea46e093a'
union
select 
    Url, 
    DateAdded, 
    N'', 
    SiteContents 
from Bookmark 
where 
    DateAdded = '1900-01-01' and 
    Url = N'080b8253-307d-430a-bcca-9abea46e093a'
order by DateAdded desc
;

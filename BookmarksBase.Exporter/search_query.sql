CREATE PROCEDURE [dbo].[Search]  @searchString nvarchar(100)
as
select top 100
    Url
    ,DateAdded
    ,Title
    ,( 
        case 
            when patindex(@searchString, Title) > 0 then 'IN TITLE'
            when patindex(@searchString, Url) > 0 then 'IN URL'
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
    (
        patindex(@searchString, Title) > 0 or
        patindex(@searchString, Url) > 0 or
        patindex(@searchString, SiteContents) > 0 
    ) 
    and
    Url <> '080b8253-307d-430a-bcca-9abea46e093a'
union
select 
    Url, 
    DateAdded, 
    '', 
    SiteContents 
from Bookmark 
where 
    DateAdded = '1900-01-01' and 
    Url = '080b8253-307d-430a-bcca-9abea46e093a'
order by DateAdded desc
-- OPTION(USE HINT('ENABLE_PARALLEL_PLAN_PREFERENCE'))
;

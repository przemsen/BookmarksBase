--set statistics time on
declare @searchString nvarchar(100) = N'c%myip';
declare @searchPattern nvarchar(100) = N'%' + @searchString + N'%';
declare @excerptLength int = 120;
declare @placeholder nvarchar(max) = N'';

select 
    DateAdded
    ,Url
    ,Title
    ,( 
        case when patindex(@searchPattern, Title) > 0 then
            @placeholder
        else
        substring(
            SiteContents, 
            case 
                when
                    patindex(@searchPattern, SiteContents) - @excerptLength > 0 
                then 
                    patindex(@searchPattern, SiteContents) - @excerptLength 
                else 
                    0 
                end,
            case 
                when 
                    (len(@searchPattern) + (2 * @excerptLength)) > len(SiteContents)
                then 
                    len(SiteContents)
                else
                    (len(@searchPattern) + (2 * @excerptLength))
                end
        )        
        end
     ) e
from dbo.Bookmark 
where 
    patindex(@searchPattern, Title) > 0 or patindex(@searchPattern, SiteContents) > 0
order by DateAdded desc

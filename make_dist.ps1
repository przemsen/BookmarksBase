﻿Remove-Item .\dist -Force -Recurse -ErrorAction SilentlyContinue
mkdir -force .\dist
mkdir -force .\dist\BookmarksBase

Copy-Item .\BookmarksBase.Search\bin\Release\BookmarksBase.Search.exe -Destination .\dist\BookmarksBase
Copy-Item .\BookmarksBase.Search\bin\Release\BookmarksBase.Search.Engine.dll -Destination .\dist\BookmarksBase
Copy-Item .\BookmarksBase.Importer\bin\Release\System.Data.SQLite.dll -Destination .\dist\BookmarksBase
Copy-Item .\BookmarksBase.Importer\bin\Release\BookmarksBase.Importer.exe -Destination .\dist\BookmarksBase

Copy-Item .\BookmarksBase.Importer\bin\Release\lynx -Destination .\dist\BookmarksBase -Recurse
Copy-Item .\BookmarksBase.Importer\bin\Release\x64 -Destination .\dist\BookmarksBase -Recurse
Copy-Item .\BookmarksBase.Importer\bin\Release\x86 -Destination .\dist\BookmarksBase -Recurse

"Deployed on: $(Get-Date -format "dd-MM-yyyy HH:mm")" | Out-File .\dist\BookmarksBase\timestamp.txt



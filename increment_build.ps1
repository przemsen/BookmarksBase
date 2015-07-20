Function IncrementBuild ($versionString)
{
    $versionString -imatch '"(.*)"' | Out-Null
    $version = $Matches[1]
    
    $version -imatch '(\d+)\.(\d+)\.(\d+)\.(\d+)' | Out-Null
    [int]$build = [convert]::ToInt32($Matches[3], 10)
    ++$build;
    
    $version -imatch '(\d+)\.(\d+)\.(\d+)\.(\d+)' | Out-Null
    $newVersion = $Matches[1] + "." + $Matches[2] + "." + $build + "." + $Matches[4]
    $newVersionString = $versionString -ireplace '"(.*)"', ('"' + $newVersion + '"') 
    
    return $newVersionString
}

Function ProcessAssemblyInfo($path)
{
    $content = Get-Content $path
    $path | Out-String
    
    Copy-Item $path ($path + ".bak")
    Remove-Item $path
    
    foreach ($line in $content)
    {
        if ( (($line -match 'AssemblyVersion') -or ($line -match 'AssemblyFileVersion')) -and !($line -match '^\/\/') )
        {
            $line = IncrementBuild($line)
            $line | Out-String
        }
        $line | Out-File $path -Append -Encoding "UTF8"       
    }    
}

#$test = '[assembly: AssemblyVersion("1.2.7.0")]'
#IncrementBuild($test) | Out-String

$files = Get-ChildItem . -recurse -filter "AssemblyInfo.cs"
foreach ($file in $files) 
{
   $path = (Join-Path -Path $file.Directory -ChildPath $file.Name)
   ProcessAssemblyInfo($path)
}

cmd /c pause
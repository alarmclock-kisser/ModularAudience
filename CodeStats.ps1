param([string]$Root)

# Remove trailing backslash if present
if ($Root.EndsWith("\")) { $Root = $Root.TrimEnd('\') }

Write-Host "============================================================================================="
Write-Host "                     > > >  C# Code Quantity Statistics  < < <                               "
Write-Host "          by alarmclock-kisser and a very supportive local instance of Qwen3.8               "
Write-Host "============================================================================================="
Write-Host ""
Write-Host " ### Scanning from: $Root"
Write-Host ""

$totalCode = 0
$totalComment = 0
$totalWhitespace = 0
$totalCombined = 0
$totalJsonElements = 0
$totalXmlElements = 0
$totalCsFiles = 0
$totalRazorFiles = 0
$totalJsonFiles = 0
$totalXmlFiles = 0
$projectCount = 0

# Find all .csproj files (excluding bin/obj)
$csprojFiles = Get-ChildItem -Path $Root -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }

foreach ($proj in $csprojFiles) {
    $projectCount++
    $projName = [System.IO.Path]::GetFileNameWithoutExtension($proj.Name)
    $projDir = $proj.DirectoryName

    # Find all .cs, .razor, .json, .xml files in this project dir + subdirs (excluding bin/obj).
    $csFiles = Get-ChildItem -Path $projDir -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    $razorFiles = Get-ChildItem -Path $projDir -Recurse -Filter *.razor |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    $jsonFiles = Get-ChildItem -Path $projDir -Recurse -Filter *.json |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    $xmlFiles = Get-ChildItem -Path $projDir -Recurse -Filter *.xml |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }

    $code = 0; $comment = 0; $ws = 0;

    # Iterate over every .cs and .razor file, count code-, comment- and whitespace- lines. 
    # In razor files, detect comment lines also by leading <!-- or @* and trailing --> or *@
    foreach ($f in (@($csFiles) + @($razorFiles))) {
        try {
            $lines = [System.IO.File]::ReadAllLines($f.FullName)
        } catch { continue }

        $blockType = '' # Speichert, welcher Block-Typ offen ist ('/*', '<!--' oder '@*')
    
        foreach ($line in $lines) {
            $t = $line.TrimStart()
        
            if ($t -eq '') {
                $ws++
            } elseif ($blockType -ne '') {
                $comment++
            
                # Prüfe auf das passende End-Tag je nach aktivem Block
                if ($blockType -eq '/*') {
                    if ($t.Contains('*/')) { $blockType = '' }
                } elseif ($blockType -eq '<!--') {
                    if ($t.Contains('-->')) { $blockType = '' }
                } elseif ($blockType -eq '@*') {
                    if ($t.Contains('*@')) { $blockType = '' }
                }
            } elseif ($t.StartsWith('//')) {
                $comment++
            } elseif ($t.StartsWith('/*')) {
                $comment++
                if (-not $t.Contains('*/')) { $blockType = '/*' }
            } elseif ($t.StartsWith('<!--')) {
                $comment++
                if (-not $t.Contains('-->')) { $blockType = '<!--' }
            } elseif ($t.StartsWith('@*')) {
                $comment++
                if (-not $t.Contains('*@')) { $blockType = '@*' }
            } else {
                $code++
            }
        }
    }

    $combined = $code + $comment + $ws;

    $json = 0; $xml = 0;
    
    # Iterate over every .json and .xml file, count elements or easier:
    # Count elements encapsulated between un-escaped/-interpolated { or < and } or >
    # Even easier: Get Min('{'-count, '}'-count) and Min('<'-count, '>'-count)
    foreach ($f in (@($jsonFiles) + @($xmlFiles))) {
        try {
            # Ganze Datei als einen String einlesen ist hier am schnellsten
            $text = [System.IO.File]::ReadAllText($f.FullName)
        } catch { 
            continue 
        }

        if ($f.Extension -match '\.json$') {
            $openCount  = $text.Split('{').Count - 1
            $closeCount = $text.Split('}').Count - 1
            $json += [Math]::Min($openCount, $closeCount)
        } 
        elseif ($f.Extension -match '\.xml$') {
            $openCount  = $text.Split('<').Count - 1
            $closeCount = $text.Split('>').Count - 1
            $xml += [Math]::Min($openCount, $closeCount)
        }
    }


    Write-Host " [$projectCount] $projName"
    Write-Host "     Path:             $projDir\"
    Write-Host "     .cs    files:         $($csFiles.Count.ToString().PadLeft(7))"
    Write-Host "     .razor files:         $($razorFiles.Count.ToString().PadLeft(7))"
    Write-Host "     .json  files:         $($jsonFiles.Count.ToString().PadLeft(7))"
    Write-Host "     .xml   files:         $($xmlFiles.Count.ToString().PadLeft(7))"
    Write-Host "     Code lines:           $($code.ToString().PadLeft(7))"
    Write-Host "     Comment lines:        $($comment.ToString().PadLeft(7))"
    Write-Host "     Whitespace lines:     $($ws.ToString().PadLeft(7))"
    Write-Host "     Combined lines:   Σ= $($combined.ToString().PadLeft(7))"
    Write-Host "     JSON elements:        $($json.ToString().PadLeft(7))"
    Write-Host "     XML elements:         $($xml.ToString().PadLeft(7))"
    Write-Host ""

    $totalCode += $code
    $totalComment += $comment
    $totalWhitespace += $ws
    $totalJsonElements += $json
    $totalXmlElements += $xml
    $totalCsFiles += $csFiles.Count
    $totalJsonFiles += $jsonFiles.Count
    $totalXmlFiles += $xmlFiles.Count
}

$totalCombined = $totalCode + $totalComment + $totalWhitespace;

Write-Host "========================< TOTAL >============================"
Write-Host " $($totalCode.ToString().PadLeft(7))      lines of code (LoC),"
Write-Host " $($totalComment.ToString().PadLeft(7))      comment lines and"
Write-Host " $($totalWhitespace.ToString().PadLeft(7))      whitespace lines => "
Write-Host " $($totalCombined.ToString().PadLeft(7))  Σ= total combined lines, "
Write-Host " $($totalJsonElements.ToString().PadLeft(7))      JSON elements and"
Write-Host " $($totalXmlElements.ToString().PadLeft(7))      XML elements in"
Write-Host " $($totalCsFiles.ToString().PadLeft(7))      .cs source code files, "
Write-Host " $($totalJsonFiles.ToString().PadLeft(7))      .json files and "
Write-Host " $($totalXmlFiles.ToString().PadLeft(7))      .xml files within "
Write-Host " $($projectCount.ToString().PadLeft(7))      C#-Projects for $Root"
Write-Host "============================================================="
$file = "ProjectSettings/ProjectSettings.asset"
$content = Get-Content $file -Raw

if ($content -match 'bundleVersion: (\d+)\.(\d+)') {
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2].PadLeft(2, '0')
    $hundredths = $major * 100 + $minor
    $hundredths += 1
    $newMajor = [int]($hundredths / 100)
    $newMinor = $hundredths % 100
    $newVersion = "{0}.{1:D2}" -f $newMajor, $newMinor
    $content = $content -replace 'bundleVersion: [\d.]+', "bundleVersion: $newVersion"
    Set-Content $file $content -NoNewline
    if (-not $?) { Write-Error "Failed to write $file"; exit 1 }
    git add $file
    Write-Host "Version bumped to $newVersion"
} else {
    Write-Warning "Could not find bundleVersion in $file"
}

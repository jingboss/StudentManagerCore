$file = "C:\StudentManagerCore\StudentManagerCore\Views\Score\ScoreView.cshtml"
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)

# The duplicate lines are:
#   Line 887: \t\t\t}).fail... (right after the auto-select code)
#   Line 888: \t\t\t}).fail... (duplicate)
# Replace the pattern: auto-select code + .fail (x2) + } + blank + function startClassAnalysis
# with: auto-select code + .fail (x1) + } + blank + function startClassAnalysis

$old = "                }`r`n            }).fail(function () { `$('#aiClassSelect').html('<option value="""">-- 加载失败 --</option>'); });`r`n            }).fail(function () { `$('#aiClassSelect').html('<option value="""">-- 加载失败 --</option>'); });`r`n        }`r`n`r`n        function startClassAnalysis(forceRegenerate) {"

$new = "                }`r`n            }).fail(function () { `$('#aiClassSelect').html('<option value="""">-- 加载失败 --</option>'); });`r`n        }`r`n`r`n        function startClassAnalysis(forceRegenerate) {"

if ($content.Contains($old)) {
    Write-Host "Found duplicate, replacing..."
    $content = $content.Replace($old, $new)
    [System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
    Write-Host "Done!"
} else {
    Write-Host "Pattern not found, trying alternative..."
    # Try with tabs
    $old2 = "                 }`t`t`t}).fail(function () { `$('#aiClassSelect').html('<option value="""">-- 加载失败 --</option>'); });`t`t`t}).fail(function () { `$('#aiClassSelect').html('<option value="""">-- 加载失败 --</option>'); });`t`t}`
`        function startClassAnalysis(forceRegenerate) {"
    if ($content.Contains($old2)) {
        Write-Host "Found with tabs!"
    } else {
        Write-Host "Still not found"
    }
}

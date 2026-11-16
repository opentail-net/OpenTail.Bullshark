# Session library, L1 check (read-only). Counts the facts Claude Code writes into transcripts, and prints the ten newest
# titles to read by eye. It reads ~\.claude\projects\**\*.jsonl and writes nothing.
#
#   pwsh scripts\Check-Titles.ps1 [-Root <projects folder>]
#
# A line that mentions a record type but is not that record (for example a message quoting the JSON) is counted anyway.
param([string]$Root = (Join-Path $env:USERPROFILE '.claude\projects'))
$ErrorActionPreference = 'Stop'

$files = @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.jsonl' -File)
$subagentFiles = @($files | Where-Object { $_.FullName -like '*\subagents\*' }).Count
$counts = [ordered]@{ AiTitle = 0; CustomTitle = 0; Recap = 0; Compaction = 0; FirstMessage = 0 }
$titled = New-Object System.Collections.Generic.List[object]

foreach ($f in $files) {
    $aiTitle = $null; $customTitle = $null; $firstMessage = $null; $recap = $false; $compaction = $false
    foreach ($line in [System.IO.File]::ReadLines($f.FullName)) {
        if ($line.Contains('"type":"ai-title"')) {
            try { $o = $line | ConvertFrom-Json; if ($o.aiTitle -and $o.aiTitle.Trim()) { $aiTitle = $o.aiTitle.Trim() } } catch {}
        }
        elseif ($line.Contains('"type":"custom-title"')) {
            try { $o = $line | ConvertFrom-Json; if ($o.customTitle) { $customTitle = $o.customTitle } } catch {}
        }
        elseif ($line.Contains('"subtype":"away_summary"')) { $recap = $true }
        elseif ($line.Contains('"subtype":"compact_boundary"')) { $compaction = $true }
        elseif ($null -eq $firstMessage -and $line.Contains('"type":"user"')) {
            try { $o = $line | ConvertFrom-Json; if ($o.message.content -is [string]) { $firstMessage = $o.message.content } } catch {}
        }
    }
    if ($aiTitle) { $counts.AiTitle++; $titled.Add([pscustomobject]@{ File = $f; Title = $aiTitle; First = $firstMessage }) }
    if ($customTitle) { $counts.CustomTitle++ }
    if ($recap) { $counts.Recap++ }
    if ($compaction) { $counts.Compaction++ }
    if ($firstMessage) { $counts.FirstMessage++ }
}

"transcript files: $($files.Count) (of them in subagents folders: $subagentFiles)"
foreach ($key in $counts.Keys) { "files with {0}: {1}" -f $key, $counts[$key] }
''
'ten newest titles, to read by eye (title, then the first message cut to 60 characters):'
$titled | Sort-Object { $_.File.LastWriteTime } -Descending | Select-Object -First 10 | ForEach-Object {
    $first = if ($_.First) { $_.First.Substring(0, [Math]::Min(60, $_.First.Length)) } else { '(none)' }
    '{0}  {1}  | first: {2}' -f $_.File.LastWriteTime.ToString('yyyy-MM-dd'), $_.Title, $first
}

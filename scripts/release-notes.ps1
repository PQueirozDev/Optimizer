param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Output = "release-notes.md"
)

# Notas da versao para a pagina da release, tiradas das mesmas notas mostradas no app
# (MainWindow.Info.cs). O app le essa lista na janela de atualizacao ("O que ha de novo").
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot "PQueirozOptimizer\Pages\MainWindow.Info.cs")
$pattern = '\("v' + [regex]::Escape($Version.TrimStart('v')) + '",\s*"[^"]*",\s*new\[\]\s*\{(?<body>.*?)\}\)'
$match = [regex]::Match($source, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
$lines = @()
if ($match.Success) {
    foreach ($note in [regex]::Matches($match.Groups['body'].Value, '"((?:[^"\\]|\\.)*)"')) {
        $lines += "- " + ($note.Groups[1].Value -replace '\\"', '"')
    }
}
if ($lines.Count -eq 0) { $lines = @("- Melhorias e correcoes.") }
$text = "## Novidades`n`n" + ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $Output), $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host $text

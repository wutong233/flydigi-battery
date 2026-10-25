param([Parameter(Mandatory = $true)][string]$Path)
$errors = $null
$tokens = $null
[void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Path).Path, [ref]$tokens, [ref]$errors)
if ($errors -and $errors.Count -gt 0) {
    "PARSE FAIL: $($errors.Count) error(s)"
    $errors | Select-Object -First 5 | ForEach-Object { "  line $($_.Extent.StartLineNumber): $($_.Message)" }
    exit 1
}
"PARSE OK"
exit 0

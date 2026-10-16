$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$sourceRoot = Join-Path $packageRoot '源代码'
$releaseRoot = Join-Path $packageRoot '正式版'
$releaseSource = Join-Path $releaseRoot '源代码'
foreach ($directory in @($releaseSource, (Join-Path $releaseSource 'Assets'), (Join-Path $releaseSource '验证工具'), (Join-Path $releaseRoot '预览'), (Join-Path $releaseRoot '验证记录'))) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
Get-ChildItem -LiteralPath $sourceRoot -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $releaseSource -Force }
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Assets') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseSource 'Assets') -Force }
Get-ChildItem -LiteralPath (Join-Path $sourceRoot '验证工具') -File | Where-Object Extension -in @('.cs', '.ps1') | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseSource '验证工具') -Force }
foreach ($name in @('使用说明.txt', '测试记录.txt')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $releaseRoot -Force }
Get-ChildItem -LiteralPath (Join-Path $packageRoot '界面验证') -File -Filter '*.png' | Where-Object Name -notlike '*修改前*' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRoot '预览') -Force }
foreach ($folder in @('内存验证','界面验证')) {
    Get-ChildItem -LiteralPath (Join-Path $packageRoot $folder) -File -Filter '*.txt' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRoot '验证记录') -Force }
}
$manifest = Join-Path $releaseRoot 'SHA256SUMS.txt'
$lines = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object FullName -ne $manifest | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
})
[System.IO.File]::WriteAllLines($manifest, $lines, [System.Text.UTF8Encoding]::new($false))
$archive = Join-Path $packageRoot '骑士助手-v5.4.zip'
Compress-Archive -LiteralPath $releaseRoot -DestinationPath $archive -CompressionLevel Optimal -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
$verified = 0
try {
    foreach ($entry in $zip.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        $relative = $entry.FullName.Replace('\', '/')
        if (-not $relative.StartsWith('正式版/')) { throw ('Unexpected archive entry: ' + $relative) }
        $localPath = Join-Path $releaseRoot ($relative.Substring(4).Replace('/', '\'))
        $stream = $entry.Open(); $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose(); $stream.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash) { throw ('ZIP payload mismatch: ' + $relative) }
        $verified++
    }
} finally { $zip.Dispose() }
$fileCount = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File).Count
if ($verified -ne $fileCount) { throw ('ZIP file count mismatch: ' + $verified + ' / ' + $fileCount) }
$message = 'ZIP integrity verified: ' + $verified + ' files; all SHA-256 values match the release folder.'
[System.IO.File]::WriteAllText((Join-Path $packageRoot '压缩包校验.txt'), $message + [Environment]::NewLine + (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash, [System.Text.UTF8Encoding]::new($false))
Write-Host $message
Get-Item -LiteralPath $archive | Select-Object FullName,Length

$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$sourceRoot = Join-Path $packageRoot '源代码'
$releaseRoot = Join-Path $packageRoot '正式版'
$releaseSource = Join-Path $releaseRoot '源代码'
foreach ($directory in @($releaseSource, (Join-Path $releaseSource 'Assets'), (Join-Path $releaseSource '验证工具'))) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
Get-ChildItem -LiteralPath $sourceRoot -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $releaseSource -Force
}
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Assets') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseSource 'Assets') -Force
}
Get-ChildItem -LiteralPath (Join-Path $sourceRoot '验证工具') -File | Where-Object Extension -in @('.cs', '.ps1') | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseSource '验证工具') -Force
}
foreach ($name in @('使用说明.txt', '测试记录.txt')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $releaseRoot -Force
}
Copy-Item -LiteralPath (Join-Path $packageRoot '角色重绘检查.png') -Destination (Join-Path $releaseRoot '预览\六角色与怪物图鉴.png') -Force
Get-ChildItem -LiteralPath (Join-Path $packageRoot '怪物方向验证') -File | Where-Object Extension -in @('.png', '.gif') | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRoot '预览') -Force
}
Get-ChildItem -LiteralPath (Join-Path $packageRoot '界面验证') -File -Filter '*.png' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRoot '预览') -Force
}
foreach ($name in @('怪物方向验证.txt')) {
    $candidate = Join-Path $packageRoot $name
    if (Test-Path -LiteralPath $candidate) { Copy-Item -LiteralPath $candidate -Destination (Join-Path $releaseRoot '验证记录') -Force }
}
Get-ChildItem -LiteralPath (Join-Path $packageRoot '独立审查') -File -Filter '*.cs' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseSource '验证工具') -Force
}
$manifest = Join-Path $releaseRoot 'SHA256SUMS.txt'
$lines = @(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Where-Object FullName -ne $manifest | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($releaseRoot.Length + 1).Replace('\', '/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
})
[System.IO.File]::WriteAllLines($manifest, $lines, [System.Text.UTF8Encoding]::new($false))
$archive = Join-Path $packageRoot '骑士助手-v5.2.zip'
Compress-Archive -LiteralPath $releaseRoot -DestinationPath $archive -CompressionLevel Optimal -Force

# Read every ZIP payload and compare it with the delivered folder, without extracting or executing anything.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
$verified = 0
try {
    foreach ($entry in $zip.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        $relative = $entry.FullName.Replace('\', '/')
        if (-not $relative.StartsWith('正式版/')) { throw ('Unexpected archive entry: ' + $relative) }
        $localPath = Join-Path $releaseRoot ($relative.Substring(4).Replace('/', '\'))
        $stream = $entry.Open()
        $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($hashAlgorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $hashAlgorithm.Dispose(); $stream.Dispose() }
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

param([string]$GameDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dirty = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Packaging requires clean, committed source.' }
$head = (& git -C $repo rev-parse HEAD).Trim()
$manifestPath = Join-Path $repo 'mod.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.id -cne 'stellarfoundations' -or $manifest.name -cne 'StellarFoundations' -or
    $manifest.author -cne 'SilentHacks' -or
    $manifest.version -notmatch '^\d+\.\d+\.\d+$' -or [string]::IsNullOrWhiteSpace($manifest.description)) {
    throw 'Invalid release metadata.'
}
if (@('ai-free', 'ai-assisted', 'vibecoded') -cnotcontains $manifest.ai) { throw 'Invalid AI disclosure.' }
if (@('active', 'inactive') -cnotcontains $manifest.development) { throw 'Invalid development status.' }
if ($manifest.description.Length -gt 600) { throw 'Keep the manager description within 600 characters.' }
$tagsProperty = $manifest.PSObject.Properties['tags']
if ($null -ne $tagsProperty) {
    $tags = @($tagsProperty.Value)
    if ($tags.Count -gt 6) { throw 'Use at most six manager tags.' }
    foreach ($tag in $tags) {
        if ([string]::IsNullOrWhiteSpace($tag) -or $tag.Length -gt 24 -or @('ai', 'ai-free', 'ai-assisted', 'vibecoded', 'active', 'inactive') -contains $tag) { throw 'Invalid or reserved manager tag.' }
    }
}
$properties = @('-p:SourceRevisionId=' + $head)
if ($GameDirectory) { $properties += '-p:GameDirectory=' + $GameDirectory }
& dotnet build (Join-Path $repo 'StellarFoundations.sln') -c Release --no-incremental @properties --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& dotnet test (Join-Path $repo 'tests/StellarFoundations.Tests/StellarFoundations.Tests.csproj') -c Release --no-build --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
$dll = Join-Path $repo 'src/StellarFoundations/bin/Release/net472/StellarFoundations.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3) -cne $manifest.version) { throw 'DLL version differs.' }
$info = Join-Path $repo 'src/StellarFoundations/obj/Release/net472/StellarFoundations.AssemblyInfo.cs'
if (!(Get-Content -LiteralPath $info -Raw).Contains($head)) { throw 'Source revision missing from build metadata.' }
if (@(& git -C $repo status --porcelain).Count) { throw 'Source changed during build.' }

$output = Join-Path $repo ('artifacts/' + $manifest.version)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zipPath = Join-Path $output 'StellarFoundations.zip'
# The manager deploys these paths relative to the game directory. Documentation
# and media stay in the repository, avoiding collisions with other mods' files.
$inputs = [ordered]@{ 'Mods/StellarFoundations.dll' = $dll; 'mod.json' = $manifestPath }
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($pair in $inputs.GetEnumerator()) {
            $entry = $archive.CreateEntry($pair.Key, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead($pair.Value)
            try { $target = $entry.Open(); try { $source.CopyTo($target) } finally { $target.Dispose() } }
            finally { $source.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

$stream = [IO.File]::OpenRead($zipPath)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        if ($archive.Entries.Count -ne $inputs.Count) { throw 'Unexpected ZIP entries.' }
        foreach ($entry in $archive.Entries) {
            if (!$inputs.Contains($entry.FullName)) { throw ('Unapproved ZIP entry: ' + $entry.FullName) }
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $source = $entry.Open()
                try { $actual = [BitConverter]::ToString($sha.ComputeHash($source)).Replace('-', '') }
                finally { $source.Dispose() }
            } finally { $sha.Dispose() }
            if ($actual -cne (Get-FileHash -LiteralPath $inputs[$entry.FullName]).Hash) { throw 'ZIP bytes differ from verified source.' }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zipPath).Hash
[IO.File]::WriteAllText((Join-Path $output 'StellarFoundations.sha256'), $hash + '  StellarFoundations.zip' + [Environment]::NewLine)
$receipt = [ordered]@{
    Version = $manifest.version; IntendedTag = 'v' + $manifest.version; Commit = $head
    Archive = 'StellarFoundations.zip'; SHA256 = $hash; DllSHA256 = (Get-FileHash -LiteralPath $dll).Hash
    Entries = @($inputs.Keys); Runtime = 'Mono / MelonLoader 0.7.3 / SDModKit 1.3.3'
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding utf8
$receipt | ConvertTo-Json

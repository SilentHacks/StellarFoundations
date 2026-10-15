$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dirty=@(& git -C $repo status --porcelain)
if($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0){throw 'Packaging requires a clean, committed repository.'}
$head=(& git -C $repo rev-parse HEAD).Trim()
$manifest=Get-Content -LiteralPath (Join-Path $repo 'mod.json') -Raw | ConvertFrom-Json
if($manifest.id -ne 'stellarfoundations' -or $manifest.author -ne 'SilentHacks' -or $manifest.dll -ne 'StellarFoundations.dll'){throw 'Unexpected package identity.'}
$dll=Join-Path $repo 'src\StellarFoundations\bin\Release\net472\StellarFoundations.dll'
if(!(Test-Path -LiteralPath $dll)){throw 'Build Release output before packaging.'}
$version=[Reflection.AssemblyName]::GetAssemblyName($dll).Version
if($version.ToString(3) -ne $manifest.version){throw 'DLL and manifest versions differ.'}
$info=Join-Path $repo 'src\StellarFoundations\obj\Release\net472\StellarFoundations.AssemblyInfo.cs'
if(!(Test-Path -LiteralPath $info) -or !(Get-Content -LiteralPath $info -Raw).Contains($head)){throw 'Build this committed revision before packaging; pass -p:SourceRevisionId=<HEAD> if automatic Git metadata is unavailable.'}
$binaryTime=(Get-Item -LiteralPath $dll).LastWriteTimeUtc
foreach($source in @(& git -C $repo ls-files 'src/*' 'Directory.Build.props' 'GameReferences.props')){
    if((Get-Item -LiteralPath (Join-Path $repo $source)).LastWriteTimeUtc -gt $binaryTime){throw ('Build output is older than '+$source)}
}
$artifacts=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$zip=Join-Path $artifacts ('StellarFoundations-'+$manifest.version+'-'+$head.Substring(0,8)+'.zip')
if(Test-Path -LiteralPath $zip){throw 'This revision is already packaged.'}
$stage=Join-Path $artifacts ('.package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null
try{
    Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'StellarFoundations.dll')
    foreach($file in @('mod.json','LICENSE','README.md','CHANGELOG.md','CREDITS.md','docs/VALIDATION.md','docs/ARCHITECTURE.md')){
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage $file)
    }
    $provenance=[ordered]@{version=$manifest.version;commit=$head;dllSha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();author='SilentHacks';game='StellarDrive Demo 0.8.2';runtime='Mono / MelonLoader 0.7.3 / SDModKit 1.3.3'}
    [IO.File]::WriteAllText((Join-Path $stage 'SOURCE.json'),($provenance | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $archive=[IO.Compression.ZipFile]::OpenRead($zip)
    try{
        $expected=@('StellarFoundations.dll','mod.json','LICENSE','README.md','CHANGELOG.md','CREDITS.md','docs/VALIDATION.md','docs/ARCHITECTURE.md','SOURCE.json')
        $actual=@($archive.Entries | Where-Object Length -gt 0 | ForEach-Object { $_.FullName.Replace('\','/') })
        if(@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count -ne 0){throw 'Unexpected package contents.'}
        $entry=$archive.Entries | Where-Object FullName -eq 'StellarFoundations.dll'
        $stream=$entry.Open(); $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$sha.Dispose()}
        if($hash -ne $provenance.dllSha256){throw 'Packaged DLL hash differs.'}
    }finally{$archive.Dispose()}
    [pscustomobject]@{Package=$zip;Commit=$head;DLLSHA256=$provenance.dllSha256;ZipSHA256=(Get-FileHash -LiteralPath $zip).Hash;VerifiedEntries=9} | ConvertTo-Json
}finally{
    $resolved=[IO.Path]::GetFullPath($stage)
    if(!$resolved.StartsWith($artifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^\.package-[a-f0-9]{32}$'){throw 'Unsafe staging cleanup path.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}

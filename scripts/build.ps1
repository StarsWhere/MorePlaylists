#requires -Version 7.0
[CmdletBinding()]
param([string]$GameRoot=$env:BEAT_SABER_DIR)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(!$GameRoot){throw 'Specify -GameRoot or BEAT_SABER_DIR.'}
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if((Get-Content -LiteralPath (Join-Path $GameRoot 'BeatSaberVersion.txt') -Raw).Trim() -ne '1.45.2_28848'){throw 'This branch targets Beat Saber 1.45.2_28848.'}
$shared=Join-Path $repo 'vendor/PlaylistManager/Shared/SpriteLoader.cs'
if(!(Test-Path -LiteralPath $shared)){throw 'Run git submodule update --init --recursive first.'}
$output=Join-Path $repo '.tmp/build/bin'
& dotnet build (Join-Path $repo 'MorePlaylists/MorePlaylists.csproj') -c Release --nologo -v minimal ('-p:BeatSaberDir='+$GameRoot) ('-p:ReferenceRoot='+$GameRoot) ('-p:SharedSpriteLoaderPath='+$shared) ('-p:OutputPath='+$output+'/') ('-p:BaseIntermediateOutputPath='+(Join-Path $repo '.tmp/build/obj')+'/') '-p:AppendTargetFrameworkToOutputPath=false' '-p:DisableCopyToGame=true' '-p:DisableCopyToPlugins=true' '-p:DisableZipRelease=true' '-p:SkipGitInfo=true' '-p:ImportDirectoryBuildTargets=false'
if($LASTEXITCODE -ne 0){throw 'MorePlaylists source build failed.'}
$dll=Join-Path $output 'MorePlaylists.dll'
$identity=[Reflection.AssemblyName]::GetAssemblyName($dll)
if($identity.Name -ne 'MorePlaylists'){throw 'Unexpected output assembly.'}
Write-Output $dll

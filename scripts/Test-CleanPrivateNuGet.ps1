<#[
.SYNOPSIS
Validates the tutorial consumer flow against the signed package from the SDK GitLab pipeline.
.DESCRIPTION
Downloads the package produced for the requested SDK commit, verifies its identity and
signature, places it in a local private feed, and tests two fresh consumers with isolated
NuGet caches: a dotnet-new console project using the HelloWorld sources and a copy of the
entire tutorial solution. Model weights and licences are never copied into the test area.
#>
[CmdletBinding()]
param(
    [string]$TutorialsRepository = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory = $true)]
    [string]$SdkRepository,
    [Parameter(Mandatory = $true)]
    [string]$PrivateFeed,
    [string]$CommitSha,
    [Parameter(Mandatory = $true)]
    [string]$GitLabUrl,
    [Parameter(Mandatory = $true)]
    [string]$GitLabProject,
    [ValidateSet('net8.0', 'net9.0', 'net10.0')]
    [string]$TargetFramework = 'net8.0',
    [string]$DotnetPath,
    [string]$RunRoot,
    [string]$LicensePath,
    [string]$ModelCachePath,
    [string]$RuntimeCachePath,
    [switch]$SkipInference
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotnetStep {
    param([string]$Name, [string]$Log, [string[]]$CommandArgs)
    & $script:DotnetPath @CommandArgs *> $Log
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log -Tail 55 | Write-Output
        throw "$Name failed (exit $LASTEXITCODE); log: $Log"
    }
    Write-Output "PASS $Name"
}

function Assert-PackageFromFeed {
    param([string]$PackagesDirectory, [string]$Version, [string]$ExpectedHash)
    $installed = Join-Path $PackagesDirectory "aspose.llm\$Version\aspose.llm.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
        throw "Aspose.LLM $Version was not installed into $PackagesDirectory"
    }
    $actual = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedHash) {
        throw "Installed package differs from GitLab artifact: $installed"
    }
    Write-Output "PASS package hash in $PackagesDirectory"
}

function Read-ZipEntryText {
    param([System.IO.Compression.ZipArchiveEntry]$Entry)
    $reader = [System.IO.StreamReader]::new($Entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

$TutorialsRepository = (Resolve-Path -LiteralPath $TutorialsRepository).Path
$SdkRepository = (Resolve-Path -LiteralPath $SdkRepository).Path
$PrivateFeed = (Resolve-Path -LiteralPath $PrivateFeed).Path
$props = Get-Content -LiteralPath (Join-Path $TutorialsRepository 'Directory.Build.props') -Raw
$versionMatch = [regex]::Match($props, '<AsposeLLMVersion[^>]*>([^<]+)</AsposeLLMVersion>')
if (-not $versionMatch.Success) { throw 'Cannot find AsposeLLMVersion in Directory.Build.props' }
$version = $versionMatch.Groups[1].Value
$targetMajor = [int]([regex]::Match($TargetFramework, '^net(\d+)\.0$').Groups[1].Value)
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $candidates = @((Get-Command dotnet -ErrorAction Stop).Source,
        (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')) | Select-Object -Unique
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $available = @(& $candidate --list-sdks)
        if (@($available | Where-Object { $_ -match "^$targetMajor\." }).Count -gt 0) {
            $DotnetPath = $candidate
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($DotnetPath) -or
    -not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
    throw "Cannot find dotnet.exe with a .NET $targetMajor SDK; pass -DotnetPath"
}
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
$sdkVersions = @(& $DotnetPath --list-sdks | ForEach-Object {
    if ($_ -match '^(\d+\.\d+\.\d+)') { [version]$Matches[1] }
} | Where-Object { $_.Major -eq $targetMajor } | Sort-Object)
if ($sdkVersions.Count -eq 0) { throw "No .NET $targetMajor SDK in $DotnetPath" }
$sdkVersion = $sdkVersions[-1].ToString()

if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    $CommitSha = (& git -C $SdkRepository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve SDK HEAD commit' }
}
if ($CommitSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'CommitSha must be a full Git SHA-1' }
if ([string]::IsNullOrWhiteSpace($env:GITLAB_TOKEN)) {
    throw 'Set GITLAB_TOKEN for read access to GitLab artifacts'
}

if ([string]::IsNullOrWhiteSpace($RunRoot)) {
    $RunRoot = Join-Path $env:TEMP ('aspose-llm-clean-private-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
}
$RunRoot = [System.IO.Path]::GetFullPath($RunRoot)
if (Test-Path -LiteralPath $RunRoot) { throw "RunRoot already exists: $RunRoot" }
New-Item -ItemType Directory -Path $RunRoot | Out-Null
Write-Output "RUN_ROOT=$RunRoot"
Write-Output "SDK_COMMIT=$CommitSha PACKAGE_VERSION=$version TARGET=$TargetFramework DOTNET_SDK=$sdkVersion"

$projectId = [uri]::EscapeDataString($GitLabProject)
$headers = @{ 'PRIVATE-TOKEN' = $env:GITLAB_TOKEN }
$api = "$($GitLabUrl.TrimEnd('/'))/api/v4/projects/$projectId"
$pipelines = Invoke-RestMethod -Uri "$api/pipelines?sha=$CommitSha&per_page=100" -Headers $headers -TimeoutSec 30
$pipeline = $pipelines | Where-Object { $_.sha -eq $CommitSha -and $_.status -eq 'success' } |
    Sort-Object id -Descending | Select-Object -First 1
if ($null -eq $pipeline) { throw "No successful pipeline for SDK commit $CommitSha" }
$jobs = Invoke-RestMethod -Uri "$api/pipelines/$($pipeline.id)/jobs?per_page=100" -Headers $headers -TimeoutSec 30
$job = $jobs | Where-Object {
    $artifactFile = $_.PSObject.Properties['artifacts_file']
    $_.name -eq 'validate-publish-artifacts' -and $_.status -eq 'success' -and
    $null -ne $artifactFile -and -not [string]::IsNullOrWhiteSpace($artifactFile.Value.filename)
} | Sort-Object id -Descending | Select-Object -First 1
if ($null -eq $job) { throw "Pipeline $($pipeline.id) has no successful package artifact job" }
$jobDetails = Invoke-RestMethod -Uri "$api/jobs/$($job.id)" -Headers $headers -TimeoutSec 30
if ($jobDetails.commit.id -ne $CommitSha) { throw 'Artifact job commit does not match requested SHA' }
Write-Output "PIPELINE=$($pipeline.id) JOB=$($job.id)"

$archivePath = Join-Path $RunRoot 'gitlab-artifacts.zip'
Invoke-WebRequest -Uri "$api/jobs/$($job.id)/artifacts" -Headers $headers -OutFile $archivePath -TimeoutSec 120
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
$packagePath = Join-Path $RunRoot "Aspose.LLM.$version.nupkg"
try {
    $matches = @($archive.Entries | Where-Object {
        $_.FullName.EndsWith("/Aspose.LLM.$version.nupkg", [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -ne 1) { throw "Expected one Aspose.LLM.$version.nupkg in GitLab artifacts" }
    $versionEntry = $archive.Entries | Where-Object {
        $_.FullName.EndsWith('/package-version.txt', [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -ne $versionEntry -and (Read-ZipEntryText $versionEntry).Trim() -ne $version) {
        throw 'Pipeline package-version.txt differs from tutorial version'
    }
    $inputStream = $matches[0].Open()
    $outputStream = [System.IO.File]::Create($packagePath)
    try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
} finally { $archive.Dispose() }

$packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $nuspecEntry = $packageArchive.Entries | Where-Object { $_.FullName -eq 'Aspose.LLM.nuspec' } | Select-Object -First 1
    if ($null -eq $nuspecEntry) { throw 'Package nuspec is missing' }
    [xml]$nuspec = Read-ZipEntryText $nuspecEntry
    if ($nuspec.package.metadata.id -ne 'Aspose.LLM' -or $nuspec.package.metadata.version -ne $version) {
        throw 'Package ID or version differs from tutorial pin'
    }
    if ($null -eq ($packageArchive.Entries | Where-Object { $_.FullName -eq '.signature.p7s' } | Select-Object -First 1)) {
        throw 'GitLab package has no NuGet signature'
    }
} finally { $packageArchive.Dispose() }
Invoke-DotnetStep 'NuGet signature' (Join-Path $RunRoot 'verify-signature.log') @('nuget', 'verify', $packagePath, '--all')
$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$feedPackage = Join-Path $PrivateFeed "Aspose.LLM.$version.nupkg"
if (Test-Path -LiteralPath $feedPackage) {
    if ((Get-FileHash -LiteralPath $feedPackage -Algorithm SHA256).Hash -ne $packageHash) {
        throw "Private feed already contains a different Aspose.LLM $version package: $feedPackage"
    }
} else {
    Copy-Item -LiteralPath $packagePath -Destination $feedPackage
}
if ((Get-FileHash -LiteralPath $feedPackage -Algorithm SHA256).Hash -ne $packageHash) {
    throw 'Private feed package hash changed after copy'
}
Write-Output "PACKAGE_SHA256=$packageHash"

[pscustomobject]@{
    sdkCommit = $CommitSha; pipelineId = $pipeline.id; jobId = $job.id
    packageVersion = $version; packageSha256 = $packageHash; feed = $PrivateFeed
    targetFramework = $TargetFramework; dotnetSdk = $sdkVersion
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'provenance.json') -Encoding utf8

@{ sdk = @{ version = $sdkVersion; rollForward = 'latestPatch' } } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $RunRoot 'global.json') -Encoding utf8
$feedXml = [System.Security.SecurityElement]::Escape($PrivateFeed)
$config = Join-Path $RunRoot 'NuGet.Config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="Private" value="$feedXml" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="Private"><package pattern="Aspose.LLM" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8

$envNames = @('NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_CLI_HOME',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO',
    'ASPOSE_LLM_LICENSE', 'ASPOSE_LLM_MODEL_CACHE', 'ASPOSE_LLM_RUNTIME_CACHE_DIR')
$oldEnv = @{}
foreach ($name in $envNames) { $oldEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $RunRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $scratch = Join-Path $RunRoot 'CleanConsumer'
    $env:NUGET_PACKAGES = Join-Path $RunRoot 'scratch-packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $RunRoot 'scratch-http-cache'
    $env:DOTNET_CLI_HOME = Join-Path $RunRoot 'scratch-dotnet-home'
    Invoke-DotnetStep 'dotnet new console' (Join-Path $RunRoot 'new-console.log') @(
        'new', 'console', '--framework', $TargetFramework, '--no-restore', '--name', 'CleanConsumer', '--output', $scratch)
    $scratchProject = Join-Path $scratch 'CleanConsumer.csproj'
    Copy-Item -LiteralPath $config -Destination (Join-Path $scratch 'NuGet.Config')
    Invoke-DotnetStep 'dotnet add package' (Join-Path $RunRoot 'add-package.log') @(
        'add', $scratchProject, 'package', 'Aspose.LLM', '--version', $version, '--no-restore')
    Invoke-DotnetStep 'fresh consumer restore' (Join-Path $RunRoot 'scratch-restore.log') @(
        'restore', $scratchProject, '--configfile', $config, '--no-cache', '-v', 'quiet')
    Assert-PackageFromFeed $env:NUGET_PACKAGES $version $packageHash
    foreach ($name in @('ConsoleHelpers.cs', 'SmallModelPreset.cs', 'TutorialEnvironment.cs')) {
        Copy-Item -LiteralPath (Join-Path $TutorialsRepository "_shared\Aspose.LLM.Tutorials.Common\$name") -Destination $scratch
    }
    Copy-Item -LiteralPath (Join-Path $TutorialsRepository '01-getting-started\HelloWorld\Program.cs') -Destination (Join-Path $scratch 'Program.cs') -Force
    Invoke-DotnetStep 'fresh consumer build with tutorial sources' (Join-Path $RunRoot 'scratch-build.log') @(
        'build', $scratchProject, '-c', 'Release', '--no-restore', '-v', 'quiet')

    if (-not $SkipInference) {
        if (-not [string]::IsNullOrWhiteSpace($LicensePath)) { $env:ASPOSE_LLM_LICENSE = $LicensePath }
        if ([string]::IsNullOrWhiteSpace($env:ASPOSE_LLM_LICENSE) -or
            -not (Test-Path -LiteralPath $env:ASPOSE_LLM_LICENSE -PathType Leaf)) {
            throw 'Set ASPOSE_LLM_LICENSE to a valid licence file, pass -LicensePath, or use -SkipInference'
        }
        if ([string]::IsNullOrWhiteSpace($ModelCachePath)) {
            $ModelCachePath = Join-Path $env:LOCALAPPDATA 'Aspose.LLM\models'
        }
        $ModelCachePath = [System.IO.Path]::GetFullPath($ModelCachePath)
        $modelCandidates = @(
            (Join-Path $ModelCachePath 'Qwen_Qwen2-0.5B-Instruct-GGUF\qwen2-0_5b-instruct-q4_k_m.gguf'),
            (Join-Path $ModelCachePath 'Qwen_Qwen2-0.5B-Instruct-GGUF\Qwen2-0.5B-Instruct.Q4_K_M.gguf'),
            (Join-Path $ModelCachePath 'QuantFactory_Qwen2-0.5B-Instruct-GGUF\Qwen2-0.5B-Instruct.Q4_K_M.gguf')
        )
        if (-not @($modelCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count) {
            throw 'Small tutorial GGUF is not in the chosen cache; obtain it under its model licence or use -SkipInference'
        }
        $env:ASPOSE_LLM_MODEL_CACHE = $ModelCachePath
        if ([string]::IsNullOrWhiteSpace($RuntimeCachePath)) {
            $RuntimeCachePath = Join-Path $env:LOCALAPPDATA 'Aspose.LLM\runtimes'
        }
        $env:ASPOSE_LLM_RUNTIME_CACHE_DIR = [System.IO.Path]::GetFullPath($RuntimeCachePath)
        Invoke-DotnetStep 'HelloWorld inference' (Join-Path $RunRoot 'scratch-run.log') @(
            'run', '--project', $scratchProject, '-c', 'Release', '--no-build', '--no-restore')
        $reply = Get-Content -LiteralPath (Join-Path $RunRoot 'scratch-run.log') |
            Where-Object { $_ -match '^Bot <\s*\S' } | Select-Object -Last 1
        if ($null -eq $reply) { throw 'HelloWorld completed without an assistant reply' }
        Write-Output "PASS $reply"
    }

    $copy = Join-Path $RunRoot 'TutorialsClone'
    New-Item -ItemType Directory -Path $copy | Out-Null
    $tracked = @(& git -C $TutorialsRepository ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list tracked tutorial files' }
    foreach ($relative in $tracked) {
        $source = Join-Path $TutorialsRepository $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $target = Join-Path $copy $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    Copy-Item -LiteralPath $config -Destination (Join-Path $copy 'NuGet.Config')
    $env:NUGET_PACKAGES = Join-Path $RunRoot 'tutorial-packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $RunRoot 'tutorial-http-cache'
    $env:DOTNET_CLI_HOME = Join-Path $RunRoot 'tutorial-dotnet-home'
    $solution = Join-Path $copy 'Aspose.LLM.Tutorials.sln'
    Invoke-DotnetStep 'fresh tutorial solution restore' (Join-Path $RunRoot 'tutorial-restore.log') @(
        'restore', $solution, '--configfile', $config, '--no-cache', "-p:TutorialTargetFramework=$TargetFramework", '-v', 'quiet')
    Assert-PackageFromFeed $env:NUGET_PACKAGES $version $packageHash
    Invoke-DotnetStep 'fresh tutorial solution build' (Join-Path $RunRoot 'tutorial-build.log') @(
        'build', $solution, '-c', 'Release', '--no-restore', "-p:TutorialTargetFramework=$TargetFramework", '-v', 'quiet')
    Write-Output "PASS clean private NuGet consumer test; logs: $RunRoot"
} finally {
    Pop-Location
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $oldEnv[$name], 'Process') }
}

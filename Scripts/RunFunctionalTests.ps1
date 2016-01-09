param (
    [Parameter(Mandatory=$true)]
    [string]$Branch,
    [Parameter(Mandatory=$true)]
    [string]$BuildNumber,
    [Parameter(Mandatory=$true)]
    [string]$CIRoot,
    [Parameter(Mandatory=$true)]
    [string]$PMCCommand,
    [Parameter(Mandatory=$true)]
    [string]$NuGetCIToolsFolder,
    [Parameter(Mandatory=$true)]
    [string]$RootDir,
    [Parameter(Mandatory=$true)]
    [string]$NuGetVSIXID,
    [Parameter(Mandatory=$true)]
    [int]$ResultsTotalWaitTimeInSecs,
    [Parameter(Mandatory=$true)]
    [int]$ResultsPollingFrequencyInSecs,
    [Parameter(Mandatory=$true)]
    [int]$VSIXInstallerWaitTimeInSecs,
    [Parameter(Mandatory=$true)]
    [int]$VSLaunchWaitTimeInSecs,
    [Parameter(Mandatory=$true)]
    [int]$PMCLaunchWaitTimeInSecs,
    [Parameter(Mandatory=$true)]
	[ValidateSet("15.0", "14.0", "12.0", "11.0", "10.0")]
    [string]$VSVersion)

. "$PSScriptRoot\Utils.ps1"
. "$PSScriptRoot\VSUtils.ps1"
. "$PSScriptRoot\NuGetFunctionalTestUtils.ps1"

$success = IsAdminPrompt

if ($success -eq $false)
{
    $errorMessage = 'ERROR: Please re-run this script as an Administrator! ' +
    'Actions such as installing VSIX, uninstalling VSIX and updating registry require admin privileges.'

    Write-Error $errorMessage
    exit 1
}

$NuGetDropPath = Join-Path $CIRoot (Join-Path $Branch (Join-Path $BuildNumber "artifacts"))
$NuGetTestPath = $RootDir+'\EndToEnd'

Write-Host 'NuGetDropPath is ' $NuGetDropPath
Write-Host 'NuGetTestPath is ' $NuGetTestPath

CleanPaths -NuGetTestPath $NuGetTestPath

CleanTempFolder

ExtractEndToEndZip $NuGetDropPath $RootDir

CopyNuGetCITools $NuGetCIToolsFolder $NuGetTestPath

$success = DisableCrashDialog
if ($success -eq $false)
{
    Write-Error 'WARNING: Could not disable crash dialog'
    exit 1
}

Write-Host 'Before starting the functional tests, force delete all the Results.html under the tests folder'
(Get-ChildItem $NuGetTestPath -Recurse Results.html) | Remove-Item -Force

KillRunningInstancesOfVS

$success = InstallNuGetVSIX $NuGetDropPath $NuGetTestPath $NuGetVSIXID $VSVersion $VSIXInstallerWaitTimeInSecs
if ($success -eq $false)
{
    Write-Error 'WARNING: Could not update NuGet VSIX'
    exit 1
}

$dte2 = LaunchVSandGetDTE $VSVersion $VSLaunchWaitTimeInSecs

Write-Host "Launching the Package Manager Console inside VS and waiting for $PMCLaunchWaitTimeInSecs seconds"
ExecuteCommand $dte2 "View.PackageManagerConsole" $null "Opening NuGet Package Manager Console" $PMCLaunchWaitTimeInSecs

Write-Host "Launching the Package Manager Console and executing the necessary command: ""$PMCCommand"""
ExecuteCommand $dte2 "View.PackageManagerConsole" $PMCCommand "Running command: $PMCCommand ..."

Write-Host "Starting functional tests with command '$PMCCommand'. Will wait for results for '$ResultsTotalWaitTimeInSecs' seconds."
$success = WaitForResults $NuGetTestPath $ResultsTotalWaitTimeInSecs $ResultsPollingFrequencyInSecs

if ($success -eq $false)
{
    exit 1
}
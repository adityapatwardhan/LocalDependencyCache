# Local dependency cache for PowerShell Gallery

The purpose of this tool is to create a cache of all the information from PowerShell Gallery into a Local file in the format of a SQL Lite database.
Later PSResourceGet will use this database file to lookup dependencies and perform search against it.

The tool is to be created in C# and use the OData APIs from PowerShell Gallery to create the database cache.
The following is an example PowerShell Script to get the information about metadata that needs to be cached.

```powershell
param (
    [Parameter(Mandatory=$false)]
    [datetime]$LastPublishedDate = (Get-Date).AddHours(-5)
)

$totalPackages = @()

$downloadBatchSize = 100
$processed = 0
$i = 0
$processLastBatch = $false
$firstScan = $true

$checkpoint = $LastPublishedDate.ToString('yyyy-MM-ddTHH:mm:ssZ')

while ($true) {
    $irmParams = @{
		Uri  = 'https://www.powershellgallery.com/api/v2/Search()'
		Body = @{
			'$filter'         = "Published gt datetime'$checkpoint'"
			'$orderby'        = 'Published'
			'$inlinecount'    = 'allpages'
			'$skip'           = $i
			'$top'            = $downloadBatchSize
			includePrerelease = $true
		}
	}

    $packages = Invoke-RestMethod @irmParams

    if (-not $packages) {
        if ($firstScan) {
            Write-Verbose -Verbose "No packages found after $LastPublishedDate"
            return
        }
    }
    else {
        $processed += $packages.Value.Count
        Write-Verbose "Processed $processed packages"

        $totalPackages += $packages

        if ($packages.Value.Count -lt $downloadBatchSize) {
            $processLastBatch = $true
        }
    }

    $firstScan = $false
    $i += $downloadBatchSize

    if ($processLastBatch) {
        break
    }
}

$totalPackages
```

The script is invoked like this:

```
$p = .\Get-PublishedModules.ps1
```

The output from the script is a list of items that look like this:

```
id         : https://www.powershellgallery.com/api/v2/Packages(Id='Fortytwo.IAM.Core.Connector',Version='1.10.1')
category   : category
link       : {link, link}
title      : title
updated    : 2026-06-04T11:00:06Z
author     : author
content    : content
properties : properties
```

We need to access the `properties` property to get the metadata.
The metadata looks like this:

```
Id                       : Fortytwo.IAM.Core.Connector
Version                  : 1.10.1
NormalizedVersion        : 1.10.1
Authors                  : Marius Solbakken Mellum
Copyright                : (c) Fortytwo Technologies AS
Created                  : Created
Dependencies             : EntraIDAccessToken:(, ):
Description              : Connector for Fortytwo Core IAM, responsible for talking to the Connector Data APIs and synchronizing data between connectors the the API.
DownloadCount            : DownloadCount
GalleryDetailsUrl        : https://www.powershellgallery.com/packages/Fortytwo.IAM.Core.Connector/1.10.1
IconUrl                  : IconUrl
IsLatestVersion          : IsLatestVersion
IsAbsoluteLatestVersion  : IsAbsoluteLatestVersion
IsPrerelease             : IsPrerelease
Language                 : Language
LastUpdated              : LastUpdated
Published                : Published
PackageHash              : Apakp5RHcyi6w/Tgib7vaqQjQ0sVEdZRgBNhaLhNwW15Ulmadzyyg7Mg644X8vGrHP/NkU3daG3fvhgc2LufOQ==
PackageHashAlgorithm     : SHA512
PackageSize              : PackageSize
ProjectUrl               : https://github.com/fortytwoservices/powershell-module-fortytwo-iam-core-connector
ReportAbuseUrl           : https://www.powershellgallery.com/packages/Fortytwo.IAM.Core.Connector/1.10.1/ReportAbuse
ReleaseNotes             : https://github.com/fortytwoservices/powershell-module-fortytwo-iam-core-connector/releases
RequireLicenseAcceptance : RequireLicenseAcceptance
Summary                  : Summary
Tags                     : Fortytwo PSModule PSEdition_Core PSCmdlet_Add-ConnectorSyncSessionObject PSCommand_Add-ConnectorSyncSessionObject PSCmdlet_Build-ConnectorObject
                           PSCommand_Build-ConnectorObject PSCmdlet_Build-ConnectorOperation PSCommand_Build-ConnectorOperation PSCmdlet_Complete-ConnectorSyncSessionOperation
                           PSCommand_Complete-ConnectorSyncSessionOperation PSCmdlet_Connect-Connector PSCommand_Connect-Connector PSCmdlet_ConvertFrom-ConnectorXmlToPowerShellObject
                           PSCommand_ConvertFrom-ConnectorXmlToPowerShellObject PSCmdlet_Get-ConnectorAuthSettings PSCommand_Get-ConnectorAuthSettings
                           PSCmdlet_Get-ConnectorConfiguration PSCommand_Get-ConnectorConfiguration PSCmdlet_Get-ConnectorData PSCommand_Get-ConnectorData
                           PSCmdlet_Get-ConnectorSyncSessionOperation PSCommand_Get-ConnectorSyncSessionOperation PSCmdlet_Get-ConnectorSyncSessionOperationStatistics
                           PSCommand_Get-ConnectorSyncSessionOperationStatistics PSCmdlet_Save-ConnectorFile PSCommand_Save-ConnectorFile PSCmdlet_Send-ConnectorFile
                           PSCommand_Send-ConnectorFile PSCmdlet_Send-ConnectorLog PSCommand_Send-ConnectorLog PSCmdlet_Start-ConnectorSyncSession PSCommand_Start-ConnectorSyncSession
                           PSCmdlet_Write-ConnectorError PSCommand_Write-ConnectorError PSCmdlet_Write-ConnectorVerbose PSCommand_Write-ConnectorVerbose PSIncludes_Cmdlet
Title                    : Title
VersionDownloadCount     : VersionDownloadCount
MinClientVersion         : MinClientVersion
LastEdited               : LastEdited
LicenseUrl               : https://github.com/fortytwoservices/powershell-module-fortytwo-iam-core-connector/tree/main?tab=MIT-1-ov-file
LicenseNames             : LicenseNames
LicenseReportUrl         : LicenseReportUrl
ItemType                 : Module
FileList                 : Fortytwo.IAM.Core.Connector.nuspec|Fortytwo.IAM.Core.Connector.psd1|Fortytwo.IAM.Core.Connector.psm1|Happypath1.tests.ps1|Private\Test-ConnectorZipFile.ps1|Pub
                           lic\Add-ConnectorSyncSessionObject.ps1|Public\Add-ConnectorSyncSessionObject.tests.ps1|Public\Build-ConnectorObject.ps1|Public\Build-ConnectorOperation.ps1|Pub
                           lic\Complete-ConnectorSyncSessionOperation.ps1|Public\Connect-Connector.ps1|Public\Connect-Connector.tests.ps1|Public\ConvertFrom-ConnectorXmlToPowerShellObjec
                           t.ps1|Public\ConvertFrom-ConnectorXmlToPowerShellObject.tests.ps1|Public\Get-ConnectorAuthSettings.ps1|Public\Get-ConnectorAuthSettings.tests.ps1|Public\Get-Co
                           nnectorConfiguration.ps1|Public\Get-ConnectorData.ps1|Public\Get-ConnectorData.tests.ps1|Public\Get-ConnectorSyncSessionOperation.ps1|Public\Get-ConnectorSyncS
                           essionOperation.tests.ps1|Public\Get-ConnectorSyncSessionOperationStatistics.ps1|Public\Get-ConnectorSyncSessionOperationStatistics.tests.ps1|Public\Save-Conne
                           ctorFile.ps1|Public\Send-ConnectorFile.ps1|Public\Send-ConnectorLog.ps1|Public\Start-ConnectorSyncSession.ps1|Public\Start-ConnectorSyncSession.tests.ps1|Publi
                           c\Write-ConnectorError.ps1|Public\Write-ConnectorVerbose.ps1
GUID                     : 37fba696-f9d9-4822-9625-e135607b4c99
PowerShellVersion        : 7.1
PowerShellHostVersion    : PowerShellHostVersion
DotNetFrameworkVersion   : DotNetFrameworkVersion
CLRVersion               : CLRVersion
ProcessorArchitecture    : ProcessorArchitecture
CompanyName              : Fortytwo Technologies AS
Owners                   : fortytwo
```

The metadata that we care about for searching include Id, Version and Tags.
PSResourceGet allows to search using command name, which is a tag with `PSCommand_` prefix.

The database file should be as compact as possible.

The metadata has a property called `Dependencies` which contains the dependencies for the package.
This is important for searching.
When PSResourceGet searches with dependencies, it need to use this NuGet style version range information of the dependency to find the match.
This data should also be available in the database to ease the lookup.


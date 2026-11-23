<#
.SYNOPSIS
    Simulates the cache sync's metadata fetch from the PowerShell Gallery Search() API and measures how long it takes.

.DESCRIPTION
    Mirrors GalleryClient.GetPackagesAsync: pages through Search() filtered by Published > checkpoint,
    ordered by Published, using $skip/$top until a page returns fewer entries than the page size.
    Reports per-page timing, payload size, and overall throughput.

.EXAMPLE
    .\tools\Measure-GallerySearch.ps1

.EXAMPLE
    .\tools\Measure-GallerySearch.ps1 -LookBackDays 7 -PageSize 100 -OutputPath .\packages.json
#>
[CmdletBinding()]
param(
    [int]$LookBackDays = 7,
    [ValidateRange(1, 100)]
    [int]$PageSize = 100,
    [string]$Endpoint = 'https://www.powershellgallery.com/api/v2/Search()',
    [int]$MaxRetries = 3,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$checkpoint = [DateTime]::UtcNow.AddDays(-$LookBackDays)
$checkpointText = $checkpoint.ToString('yyyy-MM-ddTHH:mm:ssZ')

$nsm = New-Object System.Xml.XmlNamespaceManager (New-Object System.Xml.NameTable)
$nsm.AddNamespace('a', 'http://www.w3.org/2005/Atom')
$nsm.AddNamespace('m', 'http://schemas.microsoft.com/ado/2007/08/dataservices/metadata')
$nsm.AddNamespace('d', 'http://schemas.microsoft.com/ado/2007/08/dataservices')

Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(2)

function Get-PageUri([int]$Skip) {
    $query = [ordered]@{
        '$filter'         = "Published gt datetime'$checkpointText'"
        '$orderby'        = 'Published'
        '$inlinecount'    = 'allpages'
        '$skip'           = $Skip
        '$top'            = $PageSize
        includePrerelease = 'true'
    }
    $pairs = foreach ($key in $query.Keys) {
        '{0}={1}' -f [Uri]::EscapeDataString($key), [Uri]::EscapeDataString([string]$query[$key])
    }
    "$Endpoint`?$($pairs -join '&')"
}

function Get-NodeText($Node, [string]$Name) {
    $child = $Node.SelectSingleNode("d:$Name", $nsm)
    if ($child) { $child.InnerText }
}

Write-Host "Endpoint     : $Endpoint"
Write-Host "Checkpoint   : $checkpointText ($LookBackDays days back)"
Write-Host "Page size    : $PageSize"
Write-Host ''

$pageStats = [System.Collections.Generic.List[object]]::new()
$packages = [System.Collections.Generic.List[object]]::new()
$totalCount = $null
$skip = 0
$overall = [System.Diagnostics.Stopwatch]::StartNew()

try {
    while ($true) {
        $uri = Get-PageUri -Skip $skip
        $attempt = 0
        while ($true) {
            $attempt++
            $requestTimer = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                $response = $client.GetAsync($uri).GetAwaiter().GetResult()
                $null = $response.EnsureSuccessStatusCode()
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $requestTimer.Stop()
                break
            }
            catch {
                $requestTimer.Stop()
                if ($attempt -ge $MaxRetries) { throw }
                Write-Warning "skip=$skip attempt $attempt failed: $($_.Exception.Message). Retrying..."
                Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
            }
        }

        $parseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $xml = [xml]$body
        if ($null -eq $totalCount) {
            $countNode = $xml.SelectSingleNode('/a:feed/m:count', $nsm)
            if ($countNode) { $totalCount = [int]$countNode.InnerText }
        }
        $entries = $xml.SelectNodes('/a:feed/a:entry/a:content/m:properties | /a:feed/a:entry/m:properties', $nsm)
        foreach ($p in $entries) {
            $packages.Add([pscustomobject]@{
                Id           = Get-NodeText $p 'Id'
                Version      = Get-NodeText $p 'Version'
                Published    = Get-NodeText $p 'Published'
                IsPrerelease = Get-NodeText $p 'IsPrerelease'
                Tags         = Get-NodeText $p 'Tags'
                Dependencies = Get-NodeText $p 'Dependencies'
            })
        }
        $parseTimer.Stop()

        $stat = [pscustomobject]@{
            Skip      = $skip
            Entries   = $entries.Count
            KB        = [Math]::Round($body.Length / 1KB, 1)
            RequestMs = $requestTimer.ElapsedMilliseconds
            ParseMs   = $parseTimer.ElapsedMilliseconds
            Attempts  = $attempt
        }
        $pageStats.Add($stat)

        $progressTotal = if ($totalCount) { "/$totalCount" } else { '' }
        Write-Host ('Page skip={0,6}  entries={1,3}  {2,8} KB  request={3,6} ms  parse={4,4} ms  total={5}{6}' -f `
                $stat.Skip, $stat.Entries, $stat.KB, $stat.RequestMs, $stat.ParseMs, $packages.Count, $progressTotal)

        if ($entries.Count -lt $PageSize) { break }
        $skip += $PageSize
    }
}
finally {
    $overall.Stop()
    $client.Dispose()
}

$elapsed = $overall.Elapsed
$requestStats = $pageStats | Measure-Object -Property RequestMs -Average -Maximum -Minimum -Sum
$totalKB = ($pageStats | Measure-Object -Property KB -Sum).Sum
$distinctIds = ($packages | Select-Object -ExpandProperty Id -Unique | Measure-Object).Count

Write-Host ''
Write-Host '===== Summary ====='
Write-Host ("Server reported count : {0}" -f $(if ($null -ne $totalCount) { $totalCount } else { 'n/a' }))
Write-Host ("Packages fetched      : {0} ({1} distinct ids)" -f $packages.Count, $distinctIds)
Write-Host ("Pages                 : {0}" -f $pageStats.Count)
Write-Host ("Payload               : {0:N1} MB (decompressed)" -f ($totalKB / 1KB))
Write-Host ("Total elapsed         : {0:hh\:mm\:ss\.fff}" -f $elapsed)
Write-Host ("Request time (ms)     : avg {0:N0}, min {1:N0}, max {2:N0}, sum {3:N0}" -f $requestStats.Average, $requestStats.Minimum, $requestStats.Maximum, $requestStats.Sum)
if ($elapsed.TotalSeconds -gt 0) {
    Write-Host ("Throughput            : {0:N1} packages/sec" -f ($packages.Count / $elapsed.TotalSeconds))
}

if ($OutputPath) {
    $packages | ConvertTo-Json -Depth 3 | Set-Content -Path $OutputPath -Encoding utf8
    Write-Host "Saved package metadata to $OutputPath"
}

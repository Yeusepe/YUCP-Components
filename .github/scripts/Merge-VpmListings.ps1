param(
    [Parameter(Mandatory = $true)]
    [string]$PrimaryIndexPath,

    [Parameter(Mandatory = $true)]
    [string]$SecondaryIndexUrl,

    [Parameter(Mandatory = $true)]
    [string]$ListingName,

    [Parameter(Mandatory = $true)]
    [string]$ListingId,

    [Parameter(Mandatory = $true)]
    [string]$ListingUrl,

    [Parameter(Mandatory = $true)]
    [string]$ListingAuthor
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PrimaryIndexPath)) {
    throw "Primary index not found: $PrimaryIndexPath"
}

$primary = Get-Content -LiteralPath $PrimaryIndexPath -Raw | ConvertFrom-Json -AsHashtable
$secondaryResponse = Invoke-WebRequest -Uri $SecondaryIndexUrl
$secondary = $secondaryResponse.Content | ConvertFrom-Json -AsHashtable

if (-not $primary.ContainsKey('packages')) {
    throw "Primary index is missing a 'packages' section."
}

if (-not $secondary.ContainsKey('packages')) {
    throw "Secondary index is missing a 'packages' section."
}

foreach ($packageName in $secondary.packages.Keys) {
    $secondaryPackage = $secondary.packages[$packageName]

    if (-not $primary.packages.ContainsKey($packageName)) {
        $primary.packages[$packageName] = $secondaryPackage
        continue
    }

    if (-not $primary.packages[$packageName].ContainsKey('versions')) {
        $primary.packages[$packageName].versions = @{}
    }

    foreach ($version in $secondaryPackage.versions.Keys) {
        $primary.packages[$packageName].versions[$version] = $secondaryPackage.versions[$version]
    }
}

$primary.name = $ListingName
$primary.id = $ListingId
$primary.url = $ListingUrl
$primary.author = $ListingAuthor

$json = $primary | ConvertTo-Json -Compress -Depth 100
Set-Content -LiteralPath $PrimaryIndexPath -Value $json -NoNewline

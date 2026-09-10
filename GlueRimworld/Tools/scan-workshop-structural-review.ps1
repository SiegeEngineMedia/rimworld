[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$WorkshopRoot,
  [string]$CatalogPath = '',
  [string]$ReviewPlanPath = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
  $CatalogPath = Join-Path $PSScriptRoot '..\Seeds\rimworld-workshop-affordance-catalog.json'
}
if ([string]::IsNullOrWhiteSpace($ReviewPlanPath)) {
  $ReviewPlanPath = Join-Path $PSScriptRoot '..\Seeds\rimworld-workshop-structural-review.json'
}

function Get-UniqueSorted([object[]]$Values) {
  @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Get-XmlValues([xml]$Document, [string]$XPath) {
  if ($null -eq $Document) { return @() }
  @($Document.SelectNodes($XPath) | ForEach-Object { $_.InnerText.Trim() })
}

function Get-RelativePath([string]$BasePath, [string]$Path) {
  $baseUri = [Uri](([IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'))
  $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
  $baseUri.MakeRelativeUri($pathUri).ToString().Replace('/', '\')
}

$catalog = Get-Content -Raw -LiteralPath (Resolve-Path -LiteralPath $CatalogPath) | ConvertFrom-Json
$plan = Get-Content -Raw -LiteralPath (Resolve-Path -LiteralPath $ReviewPlanPath) | ConvertFrom-Json
$root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $WorkshopRoot))
$allowedIds = @($plan.allowedWorkshopIds | ForEach-Object { [string]$_ })
$catalogIds = @($catalog.downloadPlan.workshopIds | ForEach-Object { [string]$_ })
if (@(Compare-Object -ReferenceObject ($allowedIds | Sort-Object) -DifferenceObject ($catalogIds | Sort-Object)).Count -gt 0) {
  throw 'structural review plan and affordance catalog IDs diverge'
}

$catalogById = @{}
foreach ($mod in $catalog.mods) { $catalogById[[string]$mod.workshopId] = $mod }
$capabilityVocabulary = @($plan.capabilityVocabulary | ForEach-Object { [string]$_ })
$entries = @()

foreach ($id in ($allowedIds | Sort-Object)) {
  $expected = $catalogById[$id]
  $modRoot = Join-Path $root $id
  $entry = [ordered]@{
    workshopId = $id
    expectedName = [string]$expected.name
    expectedPackageId = [string]$expected.packageId
    payloadPath = "workshop/content/294100/$id"
    status = 'missing-payload'
    packageId = ''
    displayName = ''
    modVersion = ''
    supportedVersions = @()
    versionDirectories = @()
    has16Directory = $false
    fileCountsByTopLevelDirectory = [ordered]@{}
    assemblyNames = @()
    declaredXmlTags = @()
    capabilityEvidence = @()
    unavailableEvidence = @('workshop-payload-missing')
  }
  if (-not (Test-Path -LiteralPath $modRoot -PathType Container)) {
    $entries += [pscustomobject]$entry
    continue
  }
  $entry.unavailableEvidence = @()

  $aboutPath = Join-Path $modRoot 'About\About.xml'
  $about = $null
  if (Test-Path -LiteralPath $aboutPath -PathType Leaf) {
    try { $about = [xml](Get-Content -Raw -LiteralPath $aboutPath) }
    catch { $entry.unavailableEvidence = @('about-xml-invalid') }
  }
  if ($about) {
    $packages = Get-XmlValues $about '/ModMetaData/packageId'
    $entry.packageId = [string](@($packages | Select-Object -Last 1))
    $entry.displayName = [string](@(Get-XmlValues $about '/ModMetaData/name' | Select-Object -First 1))
    $entry.modVersion = [string](@(Get-XmlValues $about '/ModMetaData/modVersion' | Select-Object -First 1))
    $entry.supportedVersions = @(Get-UniqueSorted (Get-XmlValues $about '//supportedVersions/li'))
    if ($entry.packageId -and $entry.packageId -ne $entry.expectedPackageId) {
      $entry.unavailableEvidence = @($entry.unavailableEvidence) + 'package-id-mismatch'
    }
  } else {
    $entry.unavailableEvidence = @($entry.unavailableEvidence) + 'about-metadata-missing'
  }

  $files = @(Get-ChildItem -LiteralPath $modRoot -Recurse -File)
  $entry.versionDirectories = @(Get-ChildItem -LiteralPath $modRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+$' } |
    Select-Object -ExpandProperty Name | Sort-Object -Unique)
  $entry.has16Directory = $entry.versionDirectories -contains '1.6'
  if (-not $entry.has16Directory) {
    $entry.unavailableEvidence = @($entry.unavailableEvidence) + 'no-1.6-directory'
  }
  if ($entry.supportedVersions.Count -gt 0 -and $entry.supportedVersions -notcontains '1.6') {
    $entry.unavailableEvidence = @($entry.unavailableEvidence) + 'about-does-not-declare-1.6'
  }

  foreach ($file in $files) {
    $relative = Get-RelativePath $modRoot $file.FullName
    $top = ($relative -split '\\')[0]
    if (-not $entry.fileCountsByTopLevelDirectory.Contains($top)) { $entry.fileCountsByTopLevelDirectory[$top] = 0 }
    $entry.fileCountsByTopLevelDirectory[$top]++
  }
  $sortedCounts = [ordered]@{}
  foreach ($pair in ($entry.fileCountsByTopLevelDirectory.GetEnumerator() | Sort-Object Name)) {
    $sortedCounts[$pair.Name] = [int]$pair.Value
  }
  $entry.fileCountsByTopLevelDirectory = $sortedCounts
  $entry.assemblyNames = @(Get-UniqueSorted ($files | Where-Object { $_.DirectoryName -match '\\Assemblies(\\|$)' -and $_.Extension -eq '.dll' } | Select-Object -ExpandProperty Name))

  $tagEvidence = @()
  foreach ($xmlFile in ($files | Where-Object Extension -eq '.xml')) {
    try {
      $content = Get-Content -Raw -LiteralPath $xmlFile.FullName
      foreach ($token in $capabilityVocabulary) {
        if ($content -match ('<' + [regex]::Escape($token) + '(?:\s|>)') -or
            $content -match ('\b' + [regex]::Escape($token) + '\b')) { $tagEvidence += $token }
      }
    } catch { }
  }
  foreach ($token in $capabilityVocabulary) {
    if ($files.Name -match [regex]::Escape($token)) { $tagEvidence += $token }
  }
  $entry.declaredXmlTags = @(Get-UniqueSorted $tagEvidence)
  $entry.capabilityEvidence = @($entry.declaredXmlTags)
  $entry.unavailableEvidence = @(Get-UniqueSorted $entry.unavailableEvidence)
  if ($entry.unavailableEvidence.Count -eq 0) { $entry.status = 'ready-for-1.6-structural-review' }
  elseif ($entry.has16Directory -and $entry.packageId) { $entry.status = 'payload-present-with-review-gaps' }
  else { $entry.status = 'source-or-payload-review-only' }
  $entries += [pscustomobject]$entry
}

[ordered]@{
  schema = [string]$plan.receipt.schema
  bounded = [bool]$plan.receipt.bounded
  reviewId = [string]$plan.reviewId
  catalogId = [string]$catalog.catalogId
  scanPolicy = [string]$plan.scanPolicy
  scannedWorkshopIds = @($allowedIds | Sort-Object)
  entries = @($entries | Sort-Object workshopId)
  assertions = [ordered]@{
    knownCatalogIdsOnly = $true
    deterministicOrdering = $true
    missingPayloadsExplicit = @($entries | Where-Object status -eq 'missing-payload' | Select-Object -ExpandProperty workshopId)
    thirdPartyFilesCopied = $false
  }
} | ConvertTo-Json -Depth 20

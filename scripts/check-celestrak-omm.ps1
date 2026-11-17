param(
  [string]$CsvUrl = 'https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=CSV',
  [string]$JsonUrl = 'https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=JSON'
)

$ErrorActionPreference = 'Stop'
$headers = @{ 'User-Agent' = 'SkyRoof/OrbitSourceContractCheck (https://github.com/Iu-yang1/SkyRoof-Plus)' }

function Get-ValidatedFeed([string]$Url, [string]$Format) {
  Write-Host "Checking official CelesTrak GP $Format endpoint: $Url"
  $response = Invoke-WebRequest -Uri $Url -Headers $headers -TimeoutSec 75 -MaximumRedirection 4
  if ($response.StatusCode -ne 200) {
    throw "Unexpected CelesTrak $Format HTTP status: $($response.StatusCode)"
  }
  $body = [string]$response.Content
  if ($body.Length -lt 1000 -or $body.TrimStart().StartsWith('<')) {
    throw "CelesTrak $Format returned a short or HTML/error response"
  }
  if ($Format -eq 'CSV') {
    if (-not $body.StartsWith('OBJECT_NAME,') -and -not $body.StartsWith('"OBJECT_NAME",')) {
      throw 'CelesTrak OMM CSV first header field is not OBJECT_NAME'
    }
    return ,@($body | ConvertFrom-Csv)
  }
  if (-not $body.TrimStart().StartsWith('[')) {
    throw 'CelesTrak OMM JSON does not begin with an array'
  }
  return ,@($body | ConvertFrom-Json)
}

$csv = Get-ValidatedFeed $CsvUrl CSV
$json = Get-ValidatedFeed $JsonUrl JSON
if ($csv.Count -lt 20 -or $json.Count -lt 20) {
  throw "CelesTrak amateur group too small: CSV=$($csv.Count), JSON=$($json.Count)"
}

$required = @('NORAD_CAT_ID', 'EPOCH', 'MEAN_MOTION', 'ECCENTRICITY',
  'INCLINATION', 'RA_OF_ASC_NODE', 'ARG_OF_PERICENTER', 'MEAN_ANOMALY')
foreach ($field in $required) {
  if (-not ($csv[0].PSObject.Properties.Name -contains $field) -or
      -not ($json[0].PSObject.Properties.Name -contains $field)) {
    throw "CelesTrak $field is absent in CSV or JSON OMM response"
  }
}

$csvIds = [System.Collections.Generic.HashSet[string]]::new()
foreach ($item in $csv) {
  [void]$csvIds.Add([string]$item.NORAD_CAT_ID)
}
$shared = 0
foreach ($item in $json) {
  if ($csvIds.Contains([string]$item.NORAD_CAT_ID)) { $shared++ }
}
$ratio = $shared / [Math]::Max($csv.Count, $json.Count)
if ($ratio -lt 0.90) {
  throw "CelesTrak JSON and CSV amateur feeds disagree: $shared matching catalog IDs of $($csv.Count)/$($json.Count)"
}
if (-not $csvIds.Contains('22825')) {
  throw 'Reference AO-27 (NORAD 22825) missing from CelesTrak amateur GP data'
}

Write-Host "CELESTRAK_OMM_OK CSV=$($csv.Count) JSON=$($json.Count) matching=$shared ratio=$([Math]::Round($ratio,4)) AO27=22825"
Write-Host "CELESTRAK_OMM_SAMPLE CSV $(($csv | Select-Object -First 1).NORAD_CAT_ID) EPOCH=$(($csv | Select-Object -First 1).EPOCH)"

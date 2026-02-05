[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$BaseUri,

    [Parameter(Mandatory = $true)]
    [string]$ContainerName,

    [Parameter(Mandatory = $true)]
    [string]$ApiKey,

    [Parameter(Mandatory = $false)]
    [string[]]$Exclude = @(),

    [Parameter(Mandatory = $false)]
    [switch]$Recurse
)

$ErrorActionPreference = 'Stop';

# Normalize paths
$SourcePath = (Resolve-Path $SourcePath).Path;
$BaseUri = $BaseUri.TrimEnd('/');

# Get local files
$gciParams = @{
    Path = $SourcePath;
    File = $true;
};
if ($Recurse) {
    $gciParams.Recurse = $true;
}
if ($Exclude.Count -gt 0) {
    $gciParams.Exclude = $Exclude;
}
$localFiles = Get-ChildItem @gciParams;

Write-Host -ForegroundColor Cyan "Found $($localFiles.Count) local files to process";

# Get existing blobs from server
$listUri = "$BaseUri/blob/$ContainerName";
$headers = @{ 'X-Api-Key' = $ApiKey };

try {
    $existingBlobs = Invoke-RestMethod -Uri $listUri -Headers $headers -Method Get;
    Write-Host -ForegroundColor Cyan "Found $($existingBlobs.Count) existing blobs in container";
}
catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        $existingBlobs = @();
        Write-Host -ForegroundColor Cyan "Container does not exist yet, will be created on first upload";
    }
    else {
        throw;
    }
}

# Build lookup of existing blobs by name
$existingBlobMap = @{};
foreach ($blob in $existingBlobs) {
    $existingBlobMap[$blob.name] = $blob;
}

# Counters
$uploaded = 0;
$skipped = 0;
$overwritten = 0;

# Process each file
foreach ($file in $localFiles) {
    # Compute relative path for blob name (use forward slashes)
    $relativePath = $file.FullName.Substring($SourcePath.Length).TrimStart('\', '/');
    $blobName = $relativePath.Replace('\', '/');

    # Compute MD5 hash
    $md5 = [System.Security.Cryptography.MD5]::Create();
    $fileStream = [System.IO.File]::OpenRead($file.FullName);
    try {
        $hashBytes = $md5.ComputeHash($fileStream);
        $localHash = [System.BitConverter]::ToString($hashBytes).Replace('-', '').ToLower();
    }
    finally {
        $fileStream.Close();
        $md5.Dispose();
    }

    # Check if blob exists with same hash
    if ($existingBlobMap.ContainsKey($blobName)) {
        $existingBlob = $existingBlobMap[$blobName];
        if ($existingBlob.contentHash -eq $localHash) {
            Write-Host -ForegroundColor DarkGray "Skipping (unchanged): $blobName";
            $skipped++;
            continue;
        }
        else {
            Write-Host -ForegroundColor Yellow "Overwriting (hash changed): $blobName";
            $overwritten++;
        }
    }
    else {
        Write-Host -ForegroundColor Green "Uploading (new): $blobName";
        $uploaded++;
    }

    # Upload the file
    $uploadUri = "$BaseUri/blob/$ContainerName/$blobName";
    $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName);
    $null = Invoke-RestMethod -Uri $uploadUri -Headers $headers -Method Post -Body $fileBytes -ContentType 'application/octet-stream';
}

# Summary
Write-Host;
Write-Host -ForegroundColor Cyan '=== Summary ===';
Write-Host -ForegroundColor Green "Uploaded:    $uploaded";
Write-Host -ForegroundColor Yellow "Overwritten: $overwritten";
Write-Host -ForegroundColor DarkGray "Skipped:     $skipped";
Write-Host -ForegroundColor Cyan "Total:       $($uploaded + $overwritten + $skipped)";

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$BaseUri,

  [Parameter(Mandatory = $true)]
  [string]$ContainerName,

  [Parameter(Mandatory = $true)]
  [string]$ApiKey,

  [Parameter(Mandatory = $true)]
  [scriptblock]$BlobNameMapper,

  [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
  [System.IO.FileInfo[]]$Files
)

# begin/process/end blocks enable pipeline input (e.g. Get-ChildItem | .\Upload-Blobs.ps1).
# - begin: runs once before any pipeline objects arrive (initialization)
# - process: runs once per pipeline object (collecting files)
# - end: runs once after all pipeline objects have been received (actual work)
begin {
  $ErrorActionPreference = 'Stop';

  # Normalize paths
  $BaseUri = $BaseUri.TrimEnd('/');

  Write-Host 'ARGS:';
  Write-Host "BaseUri:        $BaseUri";
  Write-Host "ContainerName:  $ContainerName";
  Write-Host "ApiKey:         $ApiKey";
  Write-Host;
  Start-Sleep -Seconds 1;

  # Collect all pipeline input
  $localFiles = @();
}

process {
  $localFiles += $Files;
}

end {
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
  Start-Sleep -Seconds 1;

  # Build lookup of existing blobs by name
  $existingBlobMap = @{};
  foreach ($blob in $existingBlobs) {
    $existingBlobMap[$blob.name] = $blob;
  }

  # Counters
  $uploaded = 0;
  $skipped = 0;
  $overwritten = 0;
  $deleted = 0;

  # Track local blob names for deletion check
  $localBlobNames = @{};

  # Process each file
  $fileIndex = 0;
  $fileCount = $localFiles.Count;
  foreach ($file in $localFiles) {
    $fileIndex++;
    # Compute blob name using the provided mapper
    $blobName = (& $BlobNameMapper $file).Replace('\', '/');
    Write-Progress -Activity 'Processing blobs' -Status "$fileIndex of $fileCount : $blobName" -PercentComplete (($fileIndex / $fileCount) * 100);

    # Track this blob name
    $localBlobNames[$blobName] = $true;

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
    Invoke-RestMethod -Uri $uploadUri -Headers $headers -Method Post -Body $fileBytes -ContentType 'application/octet-stream' | Out-Null;
  }

  Write-Progress -Activity 'Processing blobs' -Completed;

  # Delete blobs that no longer exist locally
  $deleteIndex = 0;
  $deleteCount = ($existingBlobs | Where-Object { -not $localBlobNames.ContainsKey($_.name) }).Count;
  foreach ($blob in $existingBlobs) {
    if (-not $localBlobNames.ContainsKey($blob.name)) {
      $deleteIndex++;
      Write-Progress -Activity 'Deleting blobs' -Status "$deleteIndex of $deleteCount : $($blob.name)" -PercentComplete (($deleteIndex / [Math]::Max($deleteCount, 1)) * 100);
      Write-Host -ForegroundColor Red "Deleting: $($blob.name)";
      $deleteUri = "$BaseUri/blob/$ContainerName/$($blob.name)";
      Invoke-RestMethod -Uri $deleteUri -Headers $headers -Method Delete | Out-Null;
      $deleted++;
    }
  }
  Write-Progress -Activity 'Deleting blobs' -Completed;

  # Summary
  Write-Host;
  Write-Host -ForegroundColor Cyan '=== Summary ===';
  Write-Host -ForegroundColor Green "Uploaded:    $uploaded";
  Write-Host -ForegroundColor Yellow "Overwritten: $overwritten";
  Write-Host -ForegroundColor DarkGray "Skipped:     $skipped";
  Write-Host -ForegroundColor Red "Deleted:     $deleted";
  Write-Host -ForegroundColor Cyan "Total:       $($uploaded + $overwritten + $skipped + $deleted)";
  Write-Host;
}

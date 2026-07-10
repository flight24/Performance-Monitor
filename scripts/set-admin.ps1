# PowerShell script to add requireAdministrator manifest to the exe
param($exePath)

$manifest = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
"@

$manifestPath = "$env:TEMP\admin-manifest.xml"
$manifest | Out-File -FilePath $manifestPath -Encoding UTF8

# Try to use mt.exe from Windows SDK if available
$mt = Get-Command mt.exe -ErrorAction SilentlyContinue
if ($mt) {
    & mt.exe -manifest $manifestPath -outputresource:$exePath
    Write-Host "Admin manifest applied via mt.exe"
} else {
    Write-Host "mt.exe not found, manifest not applied"
}

Remove-Item $manifestPath -ErrorAction SilentlyContinue
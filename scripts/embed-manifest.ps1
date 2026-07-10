$exe = $args[0]
if (-not $exe) { $exe = "C:\Users\13367\Documents\OpenCode\system-monitor-widget\dist\系统监控.exe" }

$csharp = @'
using System;
using System.Runtime.InteropServices;
public class ExeManifest {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
}
'@

Add-Type -TypeDefinition $csharp

$manifest = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="requireAdministrator" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>'

$bytes = [System.Text.Encoding]::UTF8.GetBytes($manifest)
$RT_MANIFEST = [IntPtr]24

$h = [ExeManifest]::BeginUpdateResource($exe, $false)
if ($h -ne [IntPtr]::Zero) {
    [ExeManifest]::UpdateResource($h, $RT_MANIFEST, [IntPtr]1, 0, $bytes, $bytes.Length) | Out-Null
    [ExeManifest]::EndUpdateResource($h, $false) | Out-Null
    Write-Host "Admin manifest embedded"
} else {
    Write-Host "Failed: BeginUpdateResource returned 0"
}
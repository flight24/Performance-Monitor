import ctypes
from ctypes import wintypes
import sys

kernel32 = ctypes.windll.kernel32

def embed_manifest(exe_path):
    manifest = b'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="requireAdministrator" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>'
    
    print(f"Opening: {exe_path}")
    h = kernel32.BeginUpdateResourceW(exe_path, False)
    print(f"Handle: {h}, Error: {kernel32.GetLastError()}")
    if h == 0:
        return False
    
    # RT_MANIFEST = 24, ID = 1
    kernel32.UpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.WORD, wintypes.LPVOID, wintypes.DWORD]
    kernel32.UpdateResourceW.restype = wintypes.BOOL
    lpType = ctypes.cast(ctypes.c_void_p(24), wintypes.LPCWSTR)
    lpName = ctypes.cast(ctypes.c_void_p(1), wintypes.LPCWSTR)
    data_buf = ctypes.create_string_buffer(manifest, len(manifest))
    result = kernel32.UpdateResourceW(h, lpType, lpName, 0, data_buf, len(manifest))
    print(f"UpdateResource result: {result}, Error: {kernel32.GetLastError()}")
    if not result:
        kernel32.EndUpdateResourceW(h, True)
        return False
    
    result = kernel32.EndUpdateResourceW(h, False)
    print(f"EndUpdateResource result: {result}, Error: {kernel32.GetLastError()}")
    return bool(result)

if __name__ == "__main__":
    exe = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\13367\Documents\OpenCode\system-monitor-widget\dist\系统监控.exe"
    embed_manifest(exe)
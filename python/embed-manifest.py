import ctypes
from ctypes import wintypes
import sys

kernel32 = ctypes.windll.kernel32

def embed_manifest(exe_path):
    manifest = b'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="requireAdministrator" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>'
    h = kernel32.BeginUpdateResourceW(exe_path, False)
    if h == 0:
        return False
    kernel32.UpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.WORD, wintypes.LPVOID, wintypes.DWORD]
    kernel32.UpdateResourceW.restype = wintypes.BOOL
    lpType = ctypes.cast(ctypes.c_void_p(24), wintypes.LPCWSTR)
    lpName = ctypes.cast(ctypes.c_void_p(1), wintypes.LPCWSTR)
    data_buf = ctypes.create_string_buffer(manifest, len(manifest))
    result = kernel32.UpdateResourceW(h, lpType, lpName, 0, data_buf, len(manifest))
    if not result:
        kernel32.EndUpdateResourceW(h, True)
        return False
    result = kernel32.EndUpdateResourceW(h, False)
    return bool(result)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        embed_manifest(sys.argv[1])
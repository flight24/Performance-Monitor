# -*- mode: python ; coding: utf-8 -*-
import os

_base = os.path.abspath(SPECPATH)
_dll_dir = os.path.join(os.path.dirname(_base), 'dll')

a = Analysis(
    [os.path.join(_base, 'monitor.py')],
    pathex=[_dll_dir],
    binaries=[],
    datas=[
        (os.path.join(_dll_dir, 'LibreHardwareMonitorLib.dll'), '.'),
        (os.path.join(_dll_dir, 'HidSharp.dll'), '.'),
    ],
    hiddenimports=[
        'clr',
        'pythonnet',
        'pythonnet.runtime',
        'psutil',
        'GPUtil',
        'wmi',
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='monitor_backend',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
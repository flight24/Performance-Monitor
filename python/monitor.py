import json
import sys
import time
import os
import ctypes
import subprocess
import clr

# ── 抑制所有子进程的控制台窗口 ───────────────────────────
try:
    ctypes.windll.kernel32.FreeConsole()
except Exception:
    pass

_subprocess_Popen = subprocess.Popen
class _SilentPopen:
    def __init__(self, *args, **kwargs):
        kwargs.setdefault('creationflags', subprocess.CREATE_NO_WINDOW)
        self._popen = _subprocess_Popen(*args, **kwargs)
    def __getattr__(self, name):
        return getattr(self._popen, name)
subprocess.Popen = _SilentPopen

# ── 加载 LibreHardwareMonitor ──────────────────────────────
_script_dir = os.path.dirname(os.path.abspath(__file__))
if getattr(sys, 'frozen', False):
    _script_dir = sys._MEIPASS
_dll_candidates = [
    _script_dir,
    os.path.join(os.path.dirname(_script_dir), "dll"),
    os.path.join(_script_dir, "..", "dll"),
]
_dll_dir = _script_dir
for p in _dll_candidates:
    if os.path.exists(os.path.join(p, "LibreHardwareMonitorLib.dll")):
        _dll_dir = p
        break
sys.path.insert(0, _dll_dir)
os.environ["PATH"] = _dll_dir + ";" + _script_dir + ";" + os.environ.get("PATH", "")
clr.AddReference(os.path.join(_dll_dir, "LibreHardwareMonitorLib"))
from LibreHardwareMonitor.Hardware import Computer

import psutil

try:
    import wmi as wmi_module
    _wmi = wmi_module
    WMI_OK = True
except Exception:
    WMI_OK = False

# ── LibreHardwareMonitor ────────────────────────────────────
_lhm = None
_lhm_cpu = None
_lhm_gpu = None

def _init_lhm():
    global _lhm, _lhm_cpu, _lhm_gpu
    if _lhm is not None:
        return
    try:
        _lhm = Computer()
        _lhm.IsCpuEnabled = True
        _lhm.IsGpuEnabled = True
        _lhm.Open()
        for hw in _lhm.Hardware:
            if str(hw.HardwareType) == "Cpu":
                _lhm_cpu = hw
            elif str(hw.HardwareType) in ("GpuNvidia", "GpuAmd", "GpuIntel"):
                _lhm_gpu = hw
    except Exception:
        pass

def _get_sensor(hw, name):
    if hw is None:
        return None
    hw.Update()
    for s in hw.Sensors:
        if s.Name == name and s.Value is not None:
            return float(s.Value)
    return None

# ── WMI ─────────────────────────────────────────────────────
_wmi_conn = None

def _get_wmi():
    global _wmi_conn
    if _wmi_conn is None and WMI_OK:
        try:
            _wmi_conn = _wmi.WMI(namespace="root\\cimv2")
        except Exception:
            pass
    return _wmi_conn

def get_cpu_freq():
    if not WMI_OK:
        return 0
    try:
        c = _get_wmi()
        if c is None:
            return 0
        for p in c.Win32_PerfFormattedData_Counters_ProcessorInformation():
            if p.Name == "0,0":
                base = int(p.ProcessorFrequency) if p.ProcessorFrequency else 2000
                perf = int(p.PercentProcessorPerformance) if p.PercentProcessorPerformance else 100
                return (base * perf / 100.0) / 1000.0
    except Exception:
        pass
    return 0

def get_disk_io():
    if not WMI_OK:
        return {"read": 0, "write": 0, "active": 0}
    try:
        c = _get_wmi()
        if c is None:
            return {"read": 0, "write": 0, "active": 0}
        for d in c.Win32_PerfFormattedData_PerfDisk_PhysicalDisk():
            if " C:" in str(d.Name):
                return {
                    "read": (int(d.DiskReadBytesPerSec) if d.DiskReadBytesPerSec else 0) / (1024*1024),
                    "write": (int(d.DiskWriteBytesPerSec) if d.DiskWriteBytesPerSec else 0) / (1024*1024),
                    "active": int(d.PercentDiskTime) if d.PercentDiskTime else 0
                }
    except Exception:
        pass
    return {"read": 0, "write": 0, "active": 0}

# ── 主循环 ─────────────────────────────────────────────────
def main():
    _init_lhm()

    while True:
        try:
            cpu = psutil.cpu_percent(interval=0.1)
            cpu_freq = get_cpu_freq()
            cpu_temp = _get_sensor(_lhm_cpu, "CPU Package")
            mem = psutil.virtual_memory().percent
            disk = psutil.disk_usage("/").percent
            disk_io = get_disk_io()

            gpu_load = 0
            gpu_temp = _get_sensor(_lhm_gpu, "GPU Core")
            try:
                import GPUtil
                gpus = GPUtil.getGPUs()
                if gpus:
                    gpu_load = gpus[0].load * 100
                    if gpu_temp is None:
                        gpu_temp = gpus[0].temperature
            except Exception:
                pass

            data = {
                "cpu": cpu,
                "cpuFreq": cpu_freq,
                "cpuTemp": cpu_temp,
                "gpu": gpu_load,
                "gpuTemp": gpu_temp,
                "mem": mem,
                "disk": disk,
                "diskIO": disk_io
            }
            print(json.dumps(data), flush=True)
        except (BrokenPipeError, OSError):
            break
        except Exception as e:
            try:
                print(json.dumps({"error": str(e)}), flush=True)
            except (BrokenPipeError, OSError):
                break
        time.sleep(0.5)

if __name__ == "__main__":
    main()
import sys
import os
import ctypes
import json
import time
import subprocess
import threading
import math

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

_clr_ok = False
try:
    import clr
    _clr_ok = True
except Exception:
    pass

import psutil

try:
    import GPUtil as _GPUtil
except Exception:
    _GPUtil = None

_wmi_ok = False
try:
    import wmi as wmi_module
    _wmi = wmi_module
    _wmi_ok = True
except Exception:
    pass


_cached_cpu_temp = None
_cached_gpu_temp = None
_cached_cpu_voltage = None
_cached_gpu_mem = None

def _read_temps():
    global _cached_cpu_temp, _cached_gpu_temp, _cached_cpu_voltage
    _cached_cpu_temp = get_cpu_temp()
    _cached_gpu_temp = get_gpu_via_lhm_temp()
    _cached_cpu_voltage = get_cpu_voltage()

_lhm = None
_lhm_cpu = None
_lhm_gpu = None
_lhm_ok = False

def _init_lhm():
    global _lhm, _lhm_cpu, _lhm_gpu, _lhm_ok
    if not _clr_ok or _lhm_ok:
        return
    try:
        lhm_dll = os.path.join(_dll_dir, "LibreHardwareMonitorLib")
        clr.AddReference(lhm_dll)
        from LibreHardwareMonitor.Hardware import Computer as LHMComputer
        _lhm = LHMComputer()
        _lhm.IsCpuEnabled = True
        _lhm.IsGpuEnabled = True
        _lhm.Open()
        for hw in _lhm.Hardware:
            ht = str(hw.HardwareType)
            if ht == "Cpu":
                _lhm_cpu = hw
            elif ht in ("GpuNvidia", "GpuAmd", "GpuIntel"):
                _lhm_gpu = hw
                break
        if _lhm_gpu is None:
            for hw in _lhm.Hardware:
                for sub in hw.SubHardware:
                    if str(sub.HardwareType) in ("GpuNvidia", "GpuAmd", "GpuIntel"):
                        _lhm_gpu = sub
                        break
                if _lhm_gpu: break
        _lhm_ok = True
    except Exception:
        pass

def _get_sensor(hw, names):
    if hw is None:
        return None
    try:
        hw.Update()
    except Exception:
        return None
    if isinstance(names, str):
        names = [names]
    for name in names:
        for s in hw.Sensors:
            try:
                if s.Name == name and s.Value is not None:
                    return float(s.Value)
            except Exception:
                pass
    return None

def _get_sensor_by_type(hw, sensor_type):
    if hw is None:
        return None
    try:
        hw.Update()
    except Exception:
        return None
    for s in hw.Sensors:
        try:
            if str(s.SensorType) == sensor_type and s.Value is not None:
                return float(s.Value)
        except Exception:
            pass
    return None

def get_cpu_temp():
    if _lhm_cpu is None:
        return None
    try:
        _lhm_cpu.Update()
        for s in _lhm_cpu.Sensors:
            try:
                if str(s.SensorType) == "Temperature" and s.Name == "CPU Package" and s.Value is not None:
                    v = float(s.Value)
                    if math.isfinite(v):
                        return v
            except Exception:
                pass
    except Exception:
        pass
    return None

def get_gpu_via_lhm_load():
    if _lhm_gpu is None:
        return None
    try:
        _lhm_gpu.Update()
        best = None
        for s in _lhm_gpu.Sensors:
            try:
                if str(s.SensorType) == "Load" and s.Value is not None:
                    v = float(s.Value)
                    if math.isfinite(v) and v > 0:
                        if best is None or v > best:
                            best = v
            except Exception:
                pass
        return best
    except Exception:
        return None

def get_gpu_via_lhm_temp():
    if _lhm_gpu is None:
        return None
    try:
        _lhm_gpu.Update()
        for s in _lhm_gpu.Sensors:
            try:
                if str(s.SensorType) == "Temperature" and s.Value is not None:
                    v = float(s.Value)
                    if math.isfinite(v):
                        return v
            except Exception:
                pass
    except Exception:
        pass
    return None

def get_cpu_voltage():
    if _lhm_cpu is None:
        return None
    try:
        _lhm_cpu.Update()
        for s in _lhm_cpu.Sensors:
            try:
                if str(s.SensorType) == "Voltage" and s.Value is not None:
                    name = s.Name.lower()
                    if "vcore" in name or "v core" in name or "cpu core" in name or "vid" in name:
                        v = float(s.Value)
                        if math.isfinite(v) and v > 0:
                            return v
            except Exception:
                pass
        for s in _lhm_cpu.Sensors:
            try:
                if str(s.SensorType) == "Voltage" and s.Value is not None:
                    v = float(s.Value)
                    if math.isfinite(v) and 0.5 < v < 2.0:
                        return v
            except Exception:
                pass
    except Exception:
        pass
    return None

def get_gpu_mem():
    if _GPUtil:
        try:
            gpus = _GPUtil.getGPUs()
            if gpus:
                g = gpus[0]
                return {"used": g.memoryUsed, "total": g.memoryTotal}
        except Exception:
            pass
    if _lhm_gpu:
        try:
            _lhm_gpu.Update()
            used = None
            total = None
            for s in _lhm_gpu.Sensors:
                try:
                    if s.Value is not None:
                        name = s.Name.lower()
                        if "memory used" in name or "gpu memory used" in name or "d3d shared" in name:
                            v = float(s.Value)
                            if math.isfinite(v):
                                used = v
                        elif "memory total" in name or "gpu memory total" in name:
                            v = float(s.Value)
                            if math.isfinite(v):
                                total = v
                except Exception:
                    pass
            if used is not None:
                return {"used": used, "total": total}
        except Exception:
            pass
    return None

_wmi_conn = None

def _get_wmi():
    global _wmi_conn
    if _wmi_conn is None and _wmi_ok:
        try:
            _wmi_conn = _wmi.WMI(namespace="root\\cimv2")
        except Exception:
            pass
    return _wmi_conn

def get_cpu_freq():
    if _cpu_perf_handle and _cpu_base_freq:
        try:
            pdh = ctypes.windll.pdh
            pdh.PdhCollectQueryData(_pdh_hQuery)
            dt = ctypes.c_ulong()
            v = ctypes.c_double()
            if pdh.PdhGetFormattedCounterValue(_cpu_perf_handle, 0x00000200, ctypes.byref(dt), ctypes.byref(v)) == 0:
                if v.value and v.value > 0:
                    return (v.value / 100.0) * _cpu_base_freq / 1000.0
        except Exception:
            pass
    if _wmi_ok:
        try:
            c = _get_wmi()
            if c:
                for p in c.Win32_PerfFormattedData_Counters_ProcessorInformation():
                    if p.Name == "0,0":
                        base = int(p.ProcessorFrequency) if p.ProcessorFrequency else 2000
                        perf = int(p.PercentProcessorPerformance) if p.PercentProcessorPerformance else 100
                        return (base * perf / 100.0) / 1000.0
        except Exception:
            pass
    return 0

_last_disk = None
_last_disk_time = 0
_last_net = None
_last_net_time = 0

def get_net_io():
    global _last_net, _last_net_time
    now = time.time()
    try:
        n = psutil.net_io_counters()
        if _last_net is None:
            _last_net = n
            _last_net_time = now
            return {"pct": 0, "down": 0, "up": 0}
        dt = now - _last_net_time
        if dt <= 0:
            return {"pct": 0, "down": 0, "up": 0}
        down_bps = (n.bytes_recv - _last_net.bytes_recv) / dt
        up_bps = (n.bytes_sent - _last_net.bytes_sent) / dt
        down_mbps = down_bps * 8 / 1_000_000
        up_mbps = up_bps * 8 / 1_000_000
        total_mbps = down_mbps + up_mbps
        pct = min(total_mbps / 10, 100)
        _last_net = n
        _last_net_time = now
        return {"pct": pct, "down": down_mbps, "up": up_mbps}
    except Exception:
        return {"pct": 0, "down": 0, "up": 0}

def get_disk_io():
    global _last_disk, _last_disk_time
    now = time.time()
    try:
        d = psutil.disk_io_counters()
        if d is None:
            return {"read": 0, "write": 0}
        if _last_disk is None:
            _last_disk = d
            _last_disk_time = now
            return {"read": 0, "write": 0}
        dt = now - _last_disk_time
        if dt <= 0:
            return {"read": 0, "write": 0}
        result = {
            "read": (d.read_bytes - _last_disk.read_bytes) / dt / (1024*1024),
            "write": (d.write_bytes - _last_disk.write_bytes) / dt / (1024*1024)
        }
        _last_disk = d
        _last_disk_time = now
        return result
    except Exception:
        return {"read": 0, "write": 0}

_pdh_hQuery = None
_pdh_handles = []
_pdh_ok = False
_cpu_base_freq = 0
_cpu_perf_handle = None

def _init_pdh():
    global _pdh_hQuery, _pdh_handles, _pdh_ok, _cpu_base_freq, _cpu_perf_handle
    if _pdh_ok:
        return
    try:
        pdh = ctypes.windll.pdh
        _pdh_hQuery = ctypes.c_void_p()
        if pdh.PdhOpenQueryW(None, 0, ctypes.byref(_pdh_hQuery)) != 0:
            return

        # GPU Engine counters
        obj_name = "GPU Engine"
        cnt_buf = ctypes.c_ulong(0)
        inst_buf = ctypes.c_ulong(0)
        pdh.PdhEnumObjectItemsW(None, None, obj_name, None,
            ctypes.byref(cnt_buf), None, ctypes.byref(inst_buf), 0x4000, 0)
        if cnt_buf.value > 0:
            counters = ctypes.create_unicode_buffer(cnt_buf.value)
            pdh.PdhEnumObjectItemsW(None, None, obj_name, None,
                ctypes.byref(cnt_buf), counters, ctypes.byref(inst_buf), 0x4000, 0)
            util_name = None
            for name in counters.value.split('\0'):
                name = name.strip()
                if not name:
                    continue
                if 'utiliz' in name.lower():
                    util_name = name
                    break
            if util_name is None:
                obj_name = "GPU Adapter"
                pdh.PdhEnumObjectItemsW(None, None, obj_name, None,
                    ctypes.byref(cnt_buf), None, ctypes.byref(inst_buf), 0x4000, 0)
                if cnt_buf.value > 0:
                    counters2 = ctypes.create_unicode_buffer(cnt_buf.value)
                    pdh.PdhEnumObjectItemsW(None, None, obj_name, None,
                        ctypes.byref(cnt_buf), counters2, ctypes.byref(inst_buf), 0x4000, 0)
                    for name in counters2.value.split('\0'):
                        name = name.strip()
                        if not name:
                            continue
                        if 'utiliz' in name.lower():
                            util_name = name
                            break
            if util_name:
                path = f"\\{obj_name}(*)\\{util_name}"
                buf = ctypes.c_ulong(0)
                pdh.PdhExpandWildCardPathW(None, path, None, ctypes.byref(buf), 0)
                if buf.value > 0:
                    expanded = ctypes.create_unicode_buffer(buf.value)
                    if pdh.PdhExpandWildCardPathW(None, path, expanded, ctypes.byref(buf), 0) == 0:
                        paths = [p.strip() for p in expanded.value.split('\0') if p.strip()]
                        for p in paths:
                            h = ctypes.c_void_p()
                            if pdh.PdhAddCounterW(_pdh_hQuery, p, 0, ctypes.byref(h)) == 0:
                                _pdh_handles.append(h)

        # CPU frequency counter (core 0)
        try:
            base = psutil.cpu_freq()
            _cpu_base_freq = int(base.max) if base and base.max else 0
        except Exception:
            _cpu_base_freq = 0
        if _cpu_base_freq:
            h = ctypes.c_void_p()
            path = "\\Processor Information(0,0)\\% Processor Performance"
            if pdh.PdhAddCounterW(_pdh_hQuery, path, 0, ctypes.byref(h)) != 0:
                path = "\\Processor(0)\\% Processor Performance"
                if pdh.PdhAddCounterW(_pdh_hQuery, path, 0, ctypes.byref(h)) != 0:
                    h = None
            _cpu_perf_handle = h

        if _pdh_handles or _cpu_perf_handle:
            pdh.PdhCollectQueryData(_pdh_hQuery)
            _pdh_ok = True
    except Exception:
        pass

def get_gpu_via_pdh():
    if not _pdh_ok:
        return None
    try:
        pdh = ctypes.windll.pdh
        pdh.PdhCollectQueryData(_pdh_hQuery)
        best = 0.0
        for h in _pdh_handles:
            dt = ctypes.c_ulong()
            v = ctypes.c_double()
            if pdh.PdhGetFormattedCounterValue(h, 0x00000200, ctypes.byref(dt), ctypes.byref(v)) == 0:
                if v.value > best:
                    best = v.value
        return best
    except Exception:
        return None

def main():
    threading.Thread(target=_init_lhm, daemon=True).start()
    _init_pdh()
    _read_temps()
    last_gpu = 0
    tick = 0
    cpu_freq = get_cpu_freq()
    disk_io = get_disk_io()
    gpu_lhm_tick = 0
    gpu_wmi_tick = 0

    psutil.cpu_percent(interval=None)
    while True:
        tick_start = time.time()
        try:
            cpu = psutil.cpu_percent(interval=None)
            mem = psutil.virtual_memory().percent
            disk = psutil.disk_usage("/").percent
            tick += 1

            gpu_load = 0

            # 1) GPUtil — NVIDIA
            if _GPUtil:
                try:
                    gpus = _GPUtil.getGPUs()
                    if gpus:
                        gpu_load = gpus[0].load * 100
                except Exception:
                    pass

            # 2) PDH — Intel/AMD
            if gpu_load == 0:
                try:
                    pdh_load = get_gpu_via_pdh()
                    if pdh_load is not None and pdh_load > 0:
                        gpu_load = pdh_load
                except Exception:
                    pass

            # 3) LHM GPU — Intel Iris Xe
            if gpu_load == 0 and _lhm_gpu and tick - gpu_lhm_tick >= 1:
                try:
                    l = get_gpu_via_lhm_load()
                    if l is not None and l > 0:
                        gpu_load = l
                        gpu_lhm_tick = tick
                except Exception:
                    pass

            # 4) WMI GPU — last resort (every 3 ticks)
            if gpu_load == 0 and _wmi_ok and tick - gpu_wmi_tick >= 3:
                try:
                    c = _get_wmi()
                    if c:
                        for ge in c.Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine():
                            v = float(getattr(ge, 'UtilizationPercentage', 0) or 0)
                            if v > gpu_load:
                                gpu_load = v
                except Exception:
                    pass
                gpu_wmi_tick = tick
            if gpu_load > 100:
                gpu_load = 100
            if gpu_load == 0 and last_gpu > 0:
                gpu_load = last_gpu
            last_gpu = gpu_load

            # Temperature, freq, disk IO — every tick
            _read_temps()
            cpu_freq = get_cpu_freq()
            disk_io = get_disk_io()
            net_io = get_net_io()
            _cached_gpu_mem = get_gpu_mem()

            data = {
                "cpu": cpu, "cpuFreq": cpu_freq, "cpuTemp": _cached_cpu_temp, "cpuVoltage": _cached_cpu_voltage,
                "gpu": gpu_load, "gpuTemp": _cached_gpu_temp, "gpuMem": _cached_gpu_mem,
                "mem": mem, "disk": disk, "diskIO": disk_io, "net": net_io
            }
            print(json.dumps(data), flush=True)
        except (BrokenPipeError, OSError):
            break
        except Exception:
            pass
        elapsed = time.time() - tick_start
        if elapsed < 1.0:
            time.sleep(1.0 - elapsed)

if __name__ == "__main__":
    main()
import sys
import os
import ctypes
import json
import time
import subprocess
import threading
import math

_subprocess_Popen = subprocess.Popen
class _SilentPopen:
    def __init__(self, *args, **kwargs):
        kwargs.setdefault('creationflags', subprocess.CREATE_NO_WINDOW)
        self._popen = _subprocess_Popen(*args, **kwargs)
    def __getattr__(self, name):
        return getattr(self._popen, name)
subprocess.Popen = _SilentPopen

try:
    ctypes.windll.kernel32.FreeConsole()
except Exception:
    pass

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

_psutil = None
_psutil_event = threading.Event()

def _get_psutil():
    _psutil_event.wait()
    return _psutil

_GPUtil = None
_GPUtil_event = threading.Event()

def _get_gputil():
    _GPUtil_event.wait()
    return _GPUtil if _GPUtil else None

_wmi_ok = False
_wmi = None
_wmi_event = threading.Event()

def _preload_all():
    # Load psutil
    try:
        global _psutil
        import psutil
        _psutil = psutil
    finally:
        _psutil_event.set()
    # Load GPUtil
    try:
        global _GPUtil
        import GPUtil as g
        _GPUtil = g
    except Exception:
        _GPUtil = False
    finally:
        _GPUtil_event.set()
    # Load wmi
    global _wmi, _wmi_ok
    try:
        import wmi as wmi_module
        _wmi = wmi_module
        _wmi_ok = True
    except Exception:
        pass
    finally:
        _wmi_event.set()


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
    if _lhm_ok:
        return
    try:
        import clr
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
    gputil = _get_gputil()
    if gputil:
        try:
            gpus = gputil.getGPUs()
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
    global _wmi_conn, _wmi, _wmi_ok
    if _wmi_conn is None:
        _wmi_event.wait()
        if _wmi_ok:
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
    c = _get_wmi()
    if c:
        try:
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
        n = _get_psutil().net_io_counters()
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
        d = _get_psutil().disk_io_counters()
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
        ctypes.windll.ole32.CoInitializeEx(0, 0)
    except Exception:
        pass
    try:
        pdh = ctypes.windll.pdh
        _pdh_hQuery = ctypes.c_void_p()
        if pdh.PdhOpenQueryW(None, 0, ctypes.byref(_pdh_hQuery)) != 0:
            return

        # CPU frequency counter (core 0) — fast setup
        try:
            base = _get_psutil().cpu_freq()
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

        pdh.PdhCollectQueryData(_pdh_hQuery)
        _pdh_ok = True

        # GPU enumeration — may be slow, do on background thread
        threading.Thread(target=_init_pdh_gpu, daemon=True).start()
    except Exception:
        pass

def _init_pdh_gpu():
    global _pdh_handles
    try:
        pdh = ctypes.windll.pdh
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

_data_cache = {
    "cpu": 0, "cpuFreq": 0, "cpuTemp": None, "cpuVoltage": None,
    "gpu": 0, "gpuTemp": None, "gpuMem": None,
    "mem": 0, "disk": 0, "diskIO": {"read": 0, "write": 0}, "net": {"pct": 0, "down": 0, "up": 0}
}

def _poll_fast():
    _get_psutil().cpu_percent(interval=None)
    while True:
        try:
            _read_temps()
            _data_cache["cpuFreq"] = get_cpu_freq()
            _data_cache["cpuTemp"] = _cached_cpu_temp
            _data_cache["cpuVoltage"] = _cached_cpu_voltage
            _data_cache["gpuTemp"] = _cached_gpu_temp
        except Exception:
            pass
        time.sleep(1.0)

def _poll_gpu():
    last_gpu = 0
    gpu_lhm_tick = 0
    gpu_wmi_tick = 0
    tick = 0
    _gputil_failed = False
    while True:
        try:
            gpu_load = 0
            _data_cache["gpuMem"] = None

            if not _gputil_failed:
                try:
                    gputil = _get_gputil()
                    if gputil:
                        gpus = gputil.getGPUs()
                        if gpus:
                            g = gpus[0]
                            gpu_load = g.load * 100
                            _data_cache["gpuMem"] = {"used": g.memoryUsed, "total": g.memoryTotal}
                        else:
                            _gputil_failed = True
                except Exception:
                    _gputil_failed = True

            if gpu_load == 0:
                try:
                    pdh_load = get_gpu_via_pdh()
                    if pdh_load is not None and pdh_load > 0:
                        gpu_load = pdh_load
                except Exception:
                    pass

            if gpu_load == 0 and _lhm_gpu and tick - gpu_lhm_tick >= 1:
                try:
                    l = get_gpu_via_lhm_load()
                    if l is not None and l > 0:
                        gpu_load = l
                        gpu_lhm_tick = tick
                except Exception:
                    pass

            if gpu_load == 0 and tick - gpu_wmi_tick >= 3:
                c = _get_wmi()
                if c:
                    try:
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

            _data_cache["gpu"] = gpu_load
            tick += 1
        except Exception:
            pass
        time.sleep(1.0)

def main():
    print(json.dumps(_data_cache), flush=True)
    # Start preload immediately so _get_psutil() doesn't deadlock
    threading.Thread(target=_preload_all, daemon=True).start()
    threading.Thread(target=_init_lhm, daemon=True).start()
    threading.Thread(target=_init_pdh, daemon=True).start()
    threading.Thread(target=_poll_gpu, daemon=True).start()
    threading.Thread(target=_poll_fast, daemon=True).start()
    # Wait for psutil then seed
    _get_psutil().cpu_percent(interval=None)
    _read_temps()
    while True:
        tick_start = time.time()
        _data_cache["cpu"] = _get_psutil().cpu_percent(interval=None)
        _data_cache["mem"] = _get_psutil().virtual_memory().percent
        _data_cache["disk"] = _get_psutil().disk_usage("/").percent
        _data_cache["diskIO"] = get_disk_io()
        _data_cache["net"] = get_net_io()
        try:
            print(json.dumps(_data_cache), flush=True)
        except OSError:
            break
        elapsed = time.time() - tick_start
        if elapsed < 1.0:
            time.sleep(1.0 - elapsed)

if __name__ == "__main__":
    main()
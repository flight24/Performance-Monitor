import tkinter as tk
import threading
import time
import ctypes
from ctypes import wintypes
import sys
import os
import clr

# ── 加载 LibreHardwareMonitor ──────────────────────────────
_script_dir = os.path.dirname(os.path.abspath(__file__))
if getattr(sys, 'frozen', False):
    _script_dir = sys._MEIPASS
_dll_candidates = [
    _script_dir,
    os.path.join(os.path.dirname(os.path.dirname(_script_dir)), "dll"),
    os.path.join(_script_dir, "..", "..", "dll"),
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
    import pythoncom
    COM_OK = True
except Exception:
    COM_OK = False

try:
    import wmi as wmi_module
    _wmi = wmi_module
    WMI_OK = True
except Exception:
    WMI_OK = False

# ── 配置 ───────────────────────────────────────────────────
REFRESH    = 0.5
TRANS_KEY  = "#010203"
BG         = "#111122"
TEXT_COLOR = "#ffffff"
C_CPU      = "#00d2ff"
C_GPU      = "#7b2ff7"
C_MEM      = "#ff6b6b"
C_DISK     = "#ffd93d"
RING_W     = 12
RING_BG    = "#1e1e35"
GAUGE_SIZE = 120
PAD        = 8
FONT_PCT   = ("Microsoft YaHei UI", 20, "bold")
FONT_TMP   = ("Microsoft YaHei UI", 10, "bold")
FONT_SUB   = ("Microsoft YaHei UI", 9, "bold")


# ── 硬件监控 (LibreHardwareMonitor) ─────────────────────────
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

def _get_lhm_sensor(hw, name):
    if hw is None:
        return None
    hw.Update()
    for sensor in hw.Sensors:
        if sensor.Name == name and sensor.Value is not None:
            return float(sensor.Value)
    return None

def get_cpu_temp_lhm():
    _init_lhm()
    return _get_lhm_sensor(_lhm_cpu, "CPU Package")

def get_gpu_temp_lhm():
    _init_lhm()
    return _get_lhm_sensor(_lhm_gpu, "GPU Core")


# ── WMI 采集 ────────────────────────────────────────────────
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
        procs = c.Win32_PerfFormattedData_Counters_ProcessorInformation()
        for p in procs:
            if p.Name == "0,0":
                base = int(p.ProcessorFrequency) if p.ProcessorFrequency else 2000
                perf = int(p.PercentProcessorPerformance) if p.PercentProcessorPerformance else 100
                return (base * perf / 100.0) / 1000.0
    except Exception:
        pass
    return 0

def get_disk_io_rate():
    if not WMI_OK:
        return 0, 0, 0, psutil.disk_usage("/").percent
    try:
        c = _get_wmi()
        if c is None:
            return 0, 0, 0, psutil.disk_usage("/").percent
        disks = c.Win32_PerfFormattedData_PerfDisk_PhysicalDisk()
        for d in disks:
            if " C:" in str(d.Name):
                active = int(d.PercentDiskTime) if d.PercentDiskTime else 0
                read_bps = int(d.DiskReadBytesPerSec) if d.DiskReadBytesPerSec else 0
                write_bps = int(d.DiskWriteBytesPerSec) if d.DiskWriteBytesPerSec else 0
                read_mb = read_bps / (1024 * 1024)
                write_mb = write_bps / (1024 * 1024)
                return read_mb, write_mb, active, psutil.disk_usage("/").percent
    except Exception:
        pass
    return 0, 0, 0, psutil.disk_usage("/").percent


# ── 环形仪表盘 ────────────────────────────────────────────
class RingGauge(tk.Canvas):
    def __init__(self, parent, label, color, show_temp=False, **kw):
        super().__init__(parent, width=GAUGE_SIZE, height=GAUGE_SIZE,
                         bg=BG, highlightthickness=0, **kw)
        self.color = color
        self.label = label
        self.show_temp = show_temp
        self._pct = 0
        self._temp = None
        self._sub = ""
        self.pack_propagate(False)

    def update(self, percent, temp=None, sub=""):
        self._pct = max(0, min(100, percent))
        self._temp = temp
        self._sub = sub
        self.delete("all")

        cx = GAUGE_SIZE // 2
        cy = GAUGE_SIZE // 2
        r = GAUGE_SIZE // 2 - RING_W - 2
        x1, y1 = cx - r, cy - r
        x2, y2 = cx + r, cy + r

        self.create_arc(x1, y1, x2, y2, start=0, extent=359.9,
                        style="arc", width=RING_W, outline=RING_BG)
        if self._pct > 0:
            angle = -360 * self._pct / 100.0
            self.create_arc(x1, y1, x2, y2, start=90, extent=angle,
                            style="arc", width=RING_W, outline=self.color)
        self.create_text(cx, cy - 4, text=f"{self._pct:.0f}%",
                         font=FONT_PCT, fill=self.color)
        label_text = self.label
        if self.show_temp and self._temp is not None:
            label_text = f"{self.label}  {self._temp:.0f}°C"
        self.create_text(cx, cy + 18, text=label_text,
                         font=FONT_TMP, fill="#cccccc")
        if self._sub:
            self.create_text(cx, cy + 32, text=self._sub,
                             font=FONT_SUB, fill="#999999")


# ── 主窗口 ───────────────────────────────────────────────
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("SystemMonitor")
        self.overrideredirect(False)
        self.attributes("-topmost", True)
        self.configure(bg="#000000")

        self._drag_x = 0
        self._drag_y = 0

        cols, rows = 2, 2
        win_w = cols * GAUGE_SIZE + PAD * 3
        win_h = rows * GAUGE_SIZE + PAD * 3 + 28
        x = self.winfo_screenwidth() - win_w - 30
        y = 50
        self.geometry(f"{win_w}x{win_h}+{x}+{y}")

        self._build_ui(win_w)

        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

        self.after(100, self._setup_glass)

    def _setup_glass(self):
        try:
            self.update_idletasks()
            self.update()
            hwnd = self.winfo_id()
            user32 = ctypes.windll.user32
            dwmapi = ctypes.windll.dwmapi

            # 去掉标题栏和边框
            GWL_STYLE = -16
            WS_CAPTION = 0x00C00000
            WS_THICKFRAME = 0x00040000
            style = user32.GetWindowLongW(hwnd, GWL_STYLE)
            style &= ~WS_CAPTION
            style &= ~WS_THICKFRAME
            user32.SetWindowLongW(hwnd, GWL_STYLE, style)

            # 隐藏任务栏图标
            GWL_EXSTYLE = -20
            WS_EX_TOOLWINDOW = 0x00000080
            WS_EX_APPWINDOW = 0x00040000
            ex = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            ex |= WS_EX_TOOLWINDOW
            ex &= ~WS_EX_APPWINDOW
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex)

            # 扩展玻璃效果到整个客户区
            margins = wintypes.MARGINS(-1, -1, -1, -1)
            dwmapi.DwmExtendFrameIntoClientArea(hwnd, ctypes.byref(margins))

            # 启用亚克力效果
            class ACCENTPOLICY(ctypes.Structure):
                _fields_ = [
                    ("AccentState", ctypes.c_uint),
                    ("AccentFlags", ctypes.c_uint),
                    ("GradientColor", ctypes.c_uint),
                    ("AnimationId", ctypes.c_uint),
                ]
            class WINCOMPATTR(ctypes.Structure):
                _fields_ = [
                    ("Attribute", ctypes.c_int),
                    ("Data", ctypes.POINTER(ACCENTPOLICY)),
                    ("SizeOfData", ctypes.c_size_t),
                ]
            func = user32.SetWindowCompositionAttribute
            func.argtypes = [wintypes.HWND, ctypes.POINTER(WINCOMPATTR)]
            func.restype = wintypes.BOOL
            accent = ACCENTPOLICY()
            accent.AccentState = 4
            accent.GradientColor = 0x99000000
            attr = WINCOMPATTR()
            attr.Attribute = 19
            attr.SizeOfData = ctypes.sizeof(accent)
            attr.Data = ctypes.pointer(accent)
            func(hwnd, ctypes.byref(attr))

            # 刷新窗口布局
            SWP_FRAMECHANGED = 0x0020
            SWP_NOMOVE = 0x0002
            SWP_NOSIZE = 0x0001
            SWP_NOZORDER = 0x0004
            user32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
                                SWP_FRAMECHANGED | SWP_NOMOVE |
                                SWP_NOSIZE | SWP_NOZORDER)
        except Exception:
            pass

    def _build_ui(self, win_w):
        # 标题栏
        bar = tk.Frame(self, bg=BG, height=28)
        bar.place(x=0, y=0, width=win_w, height=28)

        cb = tk.Label(bar, text="✕", font=("Microsoft YaHei UI", 11),
                      fg="#cccccc", bg=BG, cursor="hand2")
        cb.place(x=10, y=4)
        cb.bind("<Button-1>", lambda e: self._close())
        cb.bind("<Enter>", lambda e: cb.config(fg="#ff4444"))
        cb.bind("<Leave>", lambda e: cb.config(fg="#cccccc"))

        # 自启动开关
        self._autostart = self._check_autostart()
        self._btn_auto = tk.Label(bar, text="◇", font=("Microsoft YaHei UI", 11),
                                  fg="#cccccc", bg=BG, cursor="hand2")
        self._btn_auto.place(x=36, y=4)
        self._btn_auto.bind("<Button-1>", lambda e: self._toggle_autostart())
        self._btn_auto.bind("<Enter>", lambda e: self._btn_auto.config(fg="#cccccc"))
        self._btn_auto.bind("<Leave>", lambda e: self._btn_auto.config(fg="#cccccc"))
        self._update_autostart_ui()

        tl = tk.Label(bar, text="系统监控", font=("Microsoft YaHei UI", 9, "bold"),
                      fg=TEXT_COLOR, bg=BG)
        tl.place(x=62, y=6)

        for w in (bar, tl, cb, self._btn_auto):
            w.bind("<Button-1>", self._drag_start)
            w.bind("<B1-Motion>", self._drag_move)

        # 仪表盘
        grid = tk.Frame(self, bg=BG)
        grid.place(x=PAD, y=28 + PAD)

        self.g_cpu  = RingGauge(grid, "CPU",  C_CPU,  show_temp=False)
        self.g_gpu  = RingGauge(grid, "GPU",  C_GPU,  show_temp=True)
        self.g_mem  = RingGauge(grid, "MEM",  C_MEM,  show_temp=False)
        self.g_disk = RingGauge(grid, "DISK", C_DISK, show_temp=False)

        self.g_cpu.grid(row=0, column=0, padx=2, pady=2)
        self.g_gpu.grid(row=0, column=1, padx=2, pady=2)
        self.g_mem.grid(row=1, column=0, padx=2, pady=2)
        self.g_disk.grid(row=1, column=1, padx=2, pady=2)

        menu = tk.Menu(self, tearoff=0, bg="#2a2a3e", fg=TEXT_COLOR)
        menu.add_command(label="退出", command=self._close)
        self.bind("<Button-3>", lambda e: menu.post(e.x_root, e.y_root))

    def _close(self):
        self._running = False
        if _lhm:
            try:
                _lhm.Close()
            except Exception:
                pass
        self.destroy()

    def _check_autostart(self):
        try:
            import winreg
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Software\Microsoft\Windows\CurrentVersion\Run", 0,
                                 winreg.KEY_READ)
            val, _ = winreg.QueryValueEx(key, "SystemMonitor")
            winreg.CloseKey(key)
            return os.path.exists(val)
        except Exception:
            return False

    def _toggle_autostart(self):
        try:
            import winreg
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Software\Microsoft\Windows\CurrentVersion\Run", 0,
                                 winreg.KEY_SET_VALUE)
            if self._autostart:
                winreg.DeleteValue(key, "SystemMonitor")
                self._autostart = False
            else:
                script = os.path.abspath(sys.argv[0])
                bat = os.path.join(os.path.dirname(script), "system_monitor.bat")
                if script.endswith(".exe"):
                    target = script
                elif os.path.exists(bat):
                    target = bat
                else:
                    target = script
                winreg.SetValueEx(key, "SystemMonitor", 0, winreg.REG_SZ, target)
                self._autostart = True
            winreg.CloseKey(key)
        except Exception:
            pass
        self._update_autostart_ui()

    def _update_autostart_ui(self):
        text = "◆" if self._autostart else "◇"
        color = "#00d2ff" if self._autostart else "#666666"
        self._btn_auto.config(text=text, fg=color)

    def _drag_start(self, event):
        self._drag_x = event.x
        self._drag_y = event.y

    def _drag_move(self, event):
        x = self.winfo_x() + event.x - self._drag_x
        y = self.winfo_y() + event.y - self._drag_y
        self.geometry(f"+{x}+{y}")

    def _loop(self):
        if COM_OK:
            pythoncom.CoInitialize()
        while self._running:
            try:
                cpu = psutil.cpu_percent(interval=0.1)
                cpu_freq = get_cpu_freq()
                cpu_temp = get_cpu_temp_lhm()
                mem = psutil.virtual_memory().percent
                read_mb, write_mb, disk_active, disk_usage = get_disk_io_rate()

                gpu_load = 0
                gpu_temp = None
                try:
                    gpu_temp = get_gpu_temp_lhm()
                except Exception:
                    pass

                try:
                    import GPUtil
                    gpus = GPUtil.getGPUs()
                    if gpus:
                        gpu_load = gpus[0].load * 100
                        if gpu_temp is None:
                            gpu_temp = gpus[0].temperature
                except Exception:
                    pass

                self.after(0, self._update,
                           cpu, cpu_freq, cpu_temp, gpu_load, gpu_temp, mem,
                           disk_active, disk_usage, read_mb, write_mb)
            except Exception:
                pass
            time.sleep(REFRESH)
        if COM_OK:
            pythoncom.CoUninitialize()

    def _update(self, cpu, cpu_freq, cpu_temp, gpu_load, gpu_temp, mem, disk_active, disk_usage, read_mb, write_mb):
        sub = f"{cpu_freq:.1f} GHz"
        if cpu_temp is not None:
            sub += f"  {cpu_temp:.0f}°C"
        self.g_cpu.update(cpu, None, sub)
        self.g_gpu.update(gpu_load, gpu_temp)
        self.g_mem.update(mem, None)
        sub = f"R:{read_mb:.1f}  W:{write_mb:.1f} MB/s"
        self.g_disk.update(disk_active, None, sub)


if __name__ == "__main__":
    app = App()
    app.mainloop()
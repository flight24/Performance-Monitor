# 🖥️ 性能监视器 Performance Monitor

Windows 桌面硬件监控小组件，实时显示 CPU、GPU、内存、磁盘、网络的各项参数。
A sleek desktop widget for real-time hardware monitoring on Windows.


<img width="572" height="678" alt="image" src="https://github.com/user-attachments/assets/a2ed1912-9314-4865-8ffe-3c3cc84956dc" />



**WPF 原生实现**：LibreHardwareMonitorLib + PDH + WMI 直接采集，
DWM 毛玻璃 + 自绘色调，单进程单 exe。

## ✨ 功能

| 指标 📊 | 来源 🔧 |
|---|---|
| CPU 使用率 / 频率 / 温度 / 电压 🔥 | PDH → LibreHardwareMonitor → WMI |
| GPU 使用率 / 温度 / 显存 🎮 | LHM GPU Core → PDH → WMI |
| 内存使用率 🧠 | GlobalMemoryStatusEx |
| 磁盘使用率 / I/O 💾 | DriveInfo / PDH |
| 网络上下行 / 占用率 🔄 | PDH（按网卡实例成对统计） |

- 🪟 半透明毛玻璃（`ACCENT_ENABLE_BLURBEHIND`，色调自绘）
- ⬆️ 窗口置顶开关 / 隐藏任务栏 / 拖拽移动 / 位置记忆
- 🚀 开机自启（schtasks 计划任务，最高权限）
- 🛡️ 管理员清单（读取温度传感器需要）
- 🖥️ 兼容 Win10（旧版接口回退）与 Win11 24H2+

## 🏗️ 构建

```powershell
.\build-wpf.ps1          # 自包含单文件 exe（无需安装 .NET 运行时）
.\build-wpf.ps1 -Lite    # 轻量版（需系统安装 .NET 9 Desktop Runtime）
```

产物：`dist\PerformanceMonitor.exe`（自包含版约 58MB，Lite 版 <1MB）。

要求：.NET 9 SDK（`dotnet --list-sdks` 检查）。依赖 NuGet 包
`LibreHardwareMonitorLib`、`System.Management` 还原时自动拉取。

## 📁 项目结构

```
├── PerformanceMonitor.Wpf/        🎨 WPF 主项目
│   ├── MainWindow.xaml(.cs)       🪟 毛玻璃窗口、拖拽、按钮、数据循环
│   ├── Controls/GaugeRing.xaml    ⭕ 环形表盘控件（虚线弧 + 缓动动画，RingSize 可调）
│   ├── Services/
│   │   ├── HardwareMonitorService.cs 📡 数据采集（PDH→LHM→WMI 降级链）
│   │   ├── PdhInterop.cs          🔧 PDH P/Invoke（PdhAddEnglishCounterW 免本地化）
│   │   ├── AutoStartService.cs    🚀 schtasks 开机自启
│   │   └── ConfigStore.cs         💾 位置/置顶配置持久化
│   ├── app.manifest               🛡️ 管理员 UAC 清单
│   └── icon.ico
├── TestGlass/                     🧪 像素级视觉回归测试（白底透光率验证）
├── TestHarness/                   🧪 采集服务控制台诊断工具
├── build-wpf.ps1                  📦 一键发布脚本
└── .gitignore
```

> 💡 已知坑（改代码前必读）：
> - `MainWindow.ApplyGlass` 里的 `CompositionTarget.BackgroundColor = Transparent` 不能删——删了 WPF 会把透明渲染成不透明黑，毛玻璃直接失效；
> - Win11 24H2 上旧版 `ACCENT_ENABLE_ACRYLICBLURBEHIND` 会渲染纯黑，务必使用 `ACCENT_ENABLE_BLURBEHIND (3)`；
> - PDH 计数器读取必须用 `PDH_FMT_COUNTERVALUE` 结构体封送，用 `out double` 会内存越界。

---

## 🇬🇧 English

# 🖥️ Performance Monitor

A sleek desktop widget for real-time hardware monitoring on Windows — live CPU, GPU, memory, disk and network stats at a glance.

**Native WPF implementation**: sensors are read directly via LibreHardwareMonitorLib + PDH + WMI. DWM frosted-glass blur with a self-painted tint, single process, single exe.

## ✨ Features

| Metric 📊 | Source 🔧 |
|---|---|
| CPU usage / frequency / temperature / voltage 🔥 | PDH → LibreHardwareMonitor → WMI |
| GPU usage / temperature / VRAM 🎮 | LHM GPU Core → PDH → WMI |
| Memory usage 🧠 | GlobalMemoryStatusEx |
| Disk usage / I/O 💾 | DriveInfo / PDH |
| Network up/down / utilization 🔄 | PDH (paired per-NIC instances) |

- 🪟 Semi-transparent frosted glass (`ACCENT_ENABLE_BLURBEHIND`, self-painted tint)
- ⬆️ Always-on-top toggle / hidden taskbar / drag to move / position memory
- 🚀 Auto-start on boot (schtasks scheduled task, highest privileges)
- 🛡️ Administrator manifest (required for temperature sensors)
- 🖥️ Compatible with Win10 (legacy fallback) and Win11 24H2+

## 🏗️ Build

```powershell
.\build-wpf.ps1          # self-contained single-file exe (no .NET runtime needed)
.\build-wpf.ps1 -Lite    # lightweight (requires .NET 9 Desktop Runtime)
```

Output: `dist\PerformanceMonitor.exe` (~58MB self-contained, <1MB Lite).

Requires the .NET 9 SDK (check with `dotnet --list-sdks`). NuGet packages
(`LibreHardwareMonitorLib`, `System.Management`) are restored automatically.

## 📁 Project Structure

```
├── PerformanceMonitor.Wpf/        🎨 Main WPF project
│   ├── MainWindow.xaml(.cs)       🪟 Glass window, drag, buttons, data loop
│   ├── Controls/GaugeRing.xaml    ⭕ Ring gauge control (dash arc + easing, adjustable RingSize)
│   ├── Services/
│   │   ├── HardwareMonitorService.cs 📡 Data collection (PDH→LHM→WMI fallback chain)
│   │   ├── PdhInterop.cs          🔧 PDH P/Invoke (locale-independent English counters)
│   │   ├── AutoStartService.cs    🚀 schtasks auto-start
│   │   └── ConfigStore.cs         💾 Position/pin config persistence
│   ├── app.manifest               🛡️ Administrator UAC manifest
│   └── icon.ico
├── TestGlass/                     🧪 Pixel-level visual regression test
├── TestHarness/                   🧪 Console diagnostic tool for the sensor service
├── build-wpf.ps1                  📦 One-click publish script
└── .gitignore
```

> 💡 Known pitfalls (read before modifying the code):
> - Do **not** remove `CompositionTarget.BackgroundColor = Transparent` in `MainWindow.ApplyGlass` — without it WPF renders transparency as opaque black and the frosted glass breaks completely;
> - On Windows 11 24H2 the legacy `ACCENT_ENABLE_ACRYLICBLURBEHIND` renders solid black — always use `ACCENT_ENABLE_BLURBEHIND (3)`;
> - PDH counter values must be marshalled through the `PDH_FMT_COUNTERVALUE` struct; marshalling as `out double` causes a buffer overrun.

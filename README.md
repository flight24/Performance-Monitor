# 🖥️ 性能监视器 Performance Monitor

Windows 桌面硬件监控小组件，实时显示 CPU、GPU、内存、磁盘、网络的各项参数。
A sleek desktop widget for real-time hardware monitoring on Windows.


<img width="580" height="676" alt="image" src="https://github.com/user-attachments/assets/46a68e42-632b-4fb6-96b4-8904a5edbd5e" />



**性能监视器** — 轻量级桌面硬件监控工具，实时掌握你的电脑状态。

### ✨ 功能

| 指标 📊 | 来源 🔧 |
|---|---|
| CPU 使用率 / 频率 / 温度 / 电压 🔥 | psutil / PDH / WMI / LibreHardwareMonitor |
| GPU 使用率 / 温度 / 显存 🎮 | GPUtil (NVIDIA) → PDH → LHM → WMI |
| 内存使用率 🧠 | psutil |
| 磁盘使用率 / I/O 💾 | psutil |
| 网络上下行 / 占用率 🔄 | psutil |

- 🪟 毛玻璃 / 亚克力视觉效果
- ⬆️ 窗口置顶、隐藏任务栏
- 🖱️ 拖拽移动
- 🚀 开机自启（注册表 / Electron LoginItem）
- 📌 一键置顶开关

### 🏗️ 构建

```bash
npm install
npm run build:backend  # PyInstaller 打包 Python 后端
npm run build          # electron-builder 打包为 exe
```

需先创建 conda 环境并安装依赖：
```bash
conda create -n performance-monitor python=3.11
conda activate performance-monitor
pip install pyinstaller psutil gputil pythonnet wmi
```

### 📁 项目结构

```
├── app/                  🎨 Electron 前端
│   ├── main.js           ⚙️ 主进程（窗口、子进程管理、IPC）
│   ├── preload.js        🔗 安全桥接
│   └── index.html        🖌️ 渲染器（SVG 环形表盘 + 毛玻璃）
├── python/
│   ├── monitor.py        📡 Python 数据采集后端（三线程架构）
│   ├── monitor.spec      📦 PyInstaller 打包配置
│   └── embed-manifest.py 🔧 嵌入式清单工具（备用）
├── dll/                  📚 LibreHardwareMonitorLib & HidSharp（.NET 传感器库）
├── scripts/
│   ├── embed-admin.js    🛡️ afterPack 钩子：嵌入管理员 UAC 清单
│   ├── embed-manifest.ps1🔧 PowerShell 清单嵌入脚本
│   ├── build-backend.ps1 📦 一键编译脚本
│   └── set-admin.ps1     🛡️ 备用管理员请求脚本
├── package.json          📋 项目配置（electron-builder + requestExecutionLevel）
└── .gitignore
```


**Performance Monitor** — A lightweight desktop hardware monitoring widget that keeps an eye on your PC in real time.

### ✨ Features

| Metric 📊 | Source 🔧 |
|---|---|
| CPU Usage / Frequency / Temperature / Voltage 🔥 | psutil / PDH / WMI / LibreHardwareMonitor |
| GPU Usage / Temperature / VRAM 🎮 | GPUtil (NVIDIA) → PDH → LHM → WMI |
| Memory Usage 🧠 | psutil |
| Disk Usage / I/O 💾 | psutil |
| Network Up/Down / Utilization 🔄 | psutil |

- 🪟 Frosted glass / acrylic visual effects
- ⬆️ Always-on-top, hidden taskbar
- 🖱️ Drag to move
- 🚀 Auto-start on boot (Registry / Electron LoginItem)
- 📌 One-click pin toggle

### 🏗️ Build

```bash
npm install
npm run build:backend  # PyInstaller for Python backend
npm run build          # electron-builder for portable exe
```

Requires conda env with dependencies:
```bash
conda create -n performance-monitor python=3.11
conda activate performance-monitor
pip install pyinstaller psutil gputil pythonnet wmi
```

### 📁 Project Structure

```
├── app/                  🎨 Electron frontend
│   ├── main.js           ⚙️ Main process (window, child process, IPC)
│   ├── preload.js        🔗 Secure bridge
│   └── index.html        🖌️ Renderer (SVG ring gauges + glassmorphism)
├── python/
│   ├── monitor.py        📡 Python backend (3-thread architecture)
│   ├── monitor.spec      📦 PyInstaller build config
│   └── embed-manifest.py 🔧 Manifest embedding utility (fallback)
├── dll/                  📚 LibreHardwareMonitorLib & HidSharp (.NET sensor libs)
├── scripts/
│   ├── embed-admin.js    🛡️ afterPack hook to embed UAC admin manifest
│   ├── embed-manifest.ps1🔧 PowerShell manifest embedding script
│   ├── build-backend.ps1 📦 One-click build script
│   └── set-admin.ps1     🛡️ Fallback admin request script
├── package.json          📋 Project config (electron-builder + requestExecutionLevel)
└── .gitignore
```

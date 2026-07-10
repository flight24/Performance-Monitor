# 🖥️ 性能监视器 Performance Monitor

<img width="580" height="634" alt="image" src="https://github.com/user-attachments/assets/656d2913-723e-4ef4-8b5f-829a6df61191" />

Windows 桌面硬件监控小组件，实时显示 CPU、GPU、内存、磁盘的使用率和温度。
A sleek desktop widget for real-time hardware monitoring on Windows.


**性能监视器** — 轻量级桌面硬件监控工具，实时掌握你的电脑状态。

### ✨ 功能

| 指标 📊 | 来源 🔧 |
|---|---|
| CPU 使用率 / 频率 / 温度 / 电压 🔥 | psutil / PDH / WMI / LibreHardwareMonitor |
| GPU 使用率 / 温度 / 显存 🎮 | GPUtil (NVIDIA) → PDH → LHM → WMI |
| 内存使用率 🧠 | psutil |
| 磁盘使用率 / I/O 💾 | psutil |

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

需要：`pip install pyinstaller psutil gputil pythonnet wmi`

### 📁 项目结构

```
├── app/                  🎨 Electron 前端
│   ├── main.js           ⚙️ 主进程（窗口、子进程管理、IPC）
│   ├── preload.js        🔗 安全桥接
│   ├── set-admin.js      🛡️ UAC 管理员请求
│   └── index.html        🖌️ 渲染器（SVG 环形表盘 + 毛玻璃）
├── python/
│   ├── monitor.py        📡 Python 数据采集后端
│   ├── monitor.spec      📦 PyInstaller 打包配置
│   └── embed-manifest.py 🔧 嵌入清单工具
├── dll/                  📚 LibreHardwareMonitorLib & HidSharp
├── scripts/              🛠️ 辅助脚本
└── package.json          📋 项目配置
```


**Performance Monitor** — A lightweight desktop hardware monitoring widget that keeps an eye on your PC in real time.

### ✨ Features

| Metric 📊 | Source 🔧 |
|---|---|
| CPU Usage / Frequency / Temperature / Voltage 🔥 | psutil / PDH / WMI / LibreHardwareMonitor |
| GPU Usage / Temperature / VRAM 🎮 | GPUtil (NVIDIA) → PDH → LHM → WMI |
| Memory Usage 🧠 | psutil |
| Disk Usage / I/O 💾 | psutil |

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

Requires: `pip install pyinstaller psutil gputil pythonnet wmi`

### 📁 Project Structure

```
├── app/                  🎨 Electron frontend
│   ├── main.js           ⚙️ Main process (window, child process, IPC)
│   ├── preload.js        🔗 Secure bridge
│   ├── set-admin.js      🛡️ UAC admin request
│   └── index.html        🖌️ Renderer (SVG ring gauges + glassmorphism)
├── python/
│   ├── monitor.py        📡 Python data collection backend
│   ├── monitor.spec      📦 PyInstaller build config
│   └── embed-manifest.py 🔧 Manifest embedding utility
├── dll/                  📚 LibreHardwareMonitorLib & HidSharp
├── scripts/              🛠️ Helper scripts
└── package.json          📋 Project config
```

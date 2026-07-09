# 🖥️ 性能监视器 Performance Monitor

<img width="296" height="323" alt="image" src="https://github.com/user-attachments/assets/7be196d0-976b-4e33-b3d0-2f1eafeb2970" /> <img width="294" height="324" alt="image" src="https://github.com/user-attachments/assets/704568db-f531-4491-8226-f9fb28edfaa5" />


Windows 桌面硬件监控小组件，实时显示 CPU、GPU、内存、磁盘的使用率和温度。
A sleek desktop widget for real-time hardware monitoring on Windows.

## 📖 中文 🇨🇳

**性能监视器** — 轻量级桌面硬件监控工具，实时掌握你的电脑状态。

### ✨ 功能

| 指标 📊 | 来源 🔧 |
|---|---|
| CPU 使用率 / 频率 / 温度 🔥 | psutil / WMI / LibreHardwareMonitor |
| GPU 使用率 / 温度 🎮 | GPUtil / LibreHardwareMonitor |
| 内存使用率 🧠 | psutil |
| 磁盘使用率 / I/O 💾 | psutil / WMI |

- 🪟 毛玻璃 / 亚克力视觉效果
- ⬆️ 窗口置顶、隐藏任务栏
- 🖱️ 拖拽移动
- 🚀 开机自启（注册表 / Electron LoginItem）
- 📌 一键置顶开关

### 🏗️ 版本

#### 🖥️ Electron 版（主推 ⭐）

无边框毛玻璃窗口，双进程架构。后端已打包为独立 exe，目标电脑无需安装 Python。

```bash
npm install
npm start              # 开发运行（需要本机 Python + pip 包）
npm run build:backend  # 先用 PyInstaller 打包后端
npm run build          # 打包为 dist/系统监控.exe（一键包含后端）
```

> 依赖：`pip install pyinstaller psutil gputil pythonnet wmi`（仅构建时需要）。

#### 🐍 Tkinter 版

纯 Python 单进程，轻量独立。

```bash
cd python\tkinter
pip install psutil gputil pythonnet wmi
python system_monitor.py
```

打包为 exe：

```bash
cd python\tkinter
pyinstaller SystemMonitor.spec
# 输出 dist/SystemMonitor.exe
```

### 📁 项目结构

```
├── app/                  🎨 Electron 前端
│   ├── main.js           ⚙️ 主进程（窗口、子进程、IPC）
│   ├── preload.js        🔗 安全桥接
│   └── index.html        🖌️ 渲染器（SVG 环形表盘 + 毛玻璃）
├── python/
│   ├── monitor.py        📡 Electron 版数据采集后端
│   ├── monitor.spec      📦 PyInstaller 打包配置
│   └── tkinter/          🐍 Tkinter 独立版
│       ├── system_monitor.py
│       └── SystemMonitor.spec
├── dll/                  📚 LibreHardwareMonitorLib 及依赖
├── scripts/              🛠️ 辅助脚本
└── package.json          📋 项目配置
```

---

## 📖 English 🇬🇧

**Performance Monitor** — A lightweight desktop hardware monitoring widget that keeps an eye on your PC in real time.

### ✨ Features

| Metric 📊 | Source 🔧 |
|---|---|
| CPU Usage / Frequency / Temperature 🔥 | psutil / WMI / LibreHardwareMonitor |
| GPU Usage / Temperature 🎮 | GPUtil / LibreHardwareMonitor |
| Memory Usage 🧠 | psutil |
| Disk Usage / I/O 💾 | psutil / WMI |

- 🪟 Frosted glass / acrylic visual effects
- ⬆️ Always-on-top, hidden taskbar
- 🖱️ Drag to move
- 🚀 Auto-start on boot (Registry / Electron LoginItem)
- 📌 One-click pin toggle

### 🏗️ Versions

#### 🖥️ Electron (Recommended ⭐)

Borderless glass-morphism window with a dual-process architecture. The backend is bundled as a standalone exe — no Python required on the target machine.

```bash
npm install
npm start              # Development mode (requires local Python + pip packages)
npm run build:backend  # Package backend with PyInstaller first
npm run build          # Build dist/系统监控.exe (backend included)
```

> Dependencies: `pip install pyinstaller psutil gputil pythonnet wmi` (build only).

#### 🐍 Tkinter

Pure Python single-process lightweight alternative.

```bash
cd python\tkinter
pip install psutil gputil pythonnet wmi
python system_monitor.py
```

Package as exe:

```bash
cd python\tkinter
pyinstaller SystemMonitor.spec
# Output: dist/SystemMonitor.exe
```

### 📁 Project Structure

```
├── app/                  🎨 Electron frontend
│   ├── main.js           ⚙️ Main process (window, child process, IPC)
│   ├── preload.js        🔗 Secure bridge
│   └── index.html        🖌️ Renderer (SVG ring gauges + glassmorphism)
├── python/
│   ├── monitor.py        📡 Data collection backend (Electron)
│   ├── monitor.spec      📦 PyInstaller build config
│   └── tkinter/          🐍 Standalone Tkinter version
│       ├── system_monitor.py
│       └── SystemMonitor.spec
├── dll/                  📚 LibreHardwareMonitorLib & dependencies
├── scripts/              🛠️ Helper scripts
└── package.json          📋 Project config
```

---

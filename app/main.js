const { app, BrowserWindow, screen, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');

const configPath = path.join(app.getPath('userData'), 'monitor-config.json');

function loadConfig() {
  try { return JSON.parse(fs.readFileSync(configPath, 'utf-8')); } catch { return {}; }
}
function saveConfig(data) {
  try { fs.writeFileSync(configPath, JSON.stringify({ ...loadConfig(), ...data })); } catch {}
}

let mainWindow, pythonProcess;

function findPython() {
  const paths = [
    'C:\\ProgramData\\anaconda3\\python.exe',
    'C:\\Users\\' + require('os').userInfo().username + '\\AppData\\Local\\Programs\\Python\\Python313\\python.exe',
    'C:\\Python313\\python.exe', 'python.exe', 'python3.exe'
  ];
  for (const p of paths) { if (fs.existsSync(p)) return p; }
  return 'python.exe';
}

function startPython() {
  let exePath, args, cwd;

  if (app.isPackaged) {
    exePath = path.join(process.resourcesPath, 'monitor_backend.exe');
    args = [];
    cwd = process.resourcesPath;
  } else {
    const pythonPath = findPython();
    exePath = pythonPath;
    args = [path.join(__dirname, 'monitor.py')];
    cwd = __dirname;
  }

  pythonProcess = spawn(exePath, args, {
    cwd: cwd, stdio: ['pipe', 'pipe', 'ignore'],
    windowsHide: true, shell: false,
    env: { ...process.env, PATH: cwd + ';' + (process.env.PATH || '') }
  });

  let buffer = '';
  pythonProcess.stdout.on('data', (data) => {
    buffer += data.toString();
    const lines = buffer.split('\n'); buffer = lines.pop();
    for (const line of lines) {
      if (line.trim()) {
        try { const p = JSON.parse(line); if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('system-data', p); } catch {}
      }
    }
  });
  pythonProcess.on('close', () => { pythonProcess = null; });
}

function stopPython() {
  if (pythonProcess) {
    try { pythonProcess.kill(); } catch {}
    pythonProcess = null;
  }
}

app.whenReady().then(() => {
  const config = loadConfig();
  const { width: screenW } = screen.getPrimaryDisplay().workAreaSize;

  if (app.getLoginItemSettings().openAtLogin) {
    app.setLoginItemSettings({ openAtLogin: true, name: '系统监控', path: process.execPath });
  }

  mainWindow = new BrowserWindow({
    width: 280, height: 310, frame: false, transparent: true, type: 'toolbar',
    alwaysOnTop: config.alwaysOnTop ?? false, skipTaskbar: true, resizable: false,
    webPreferences: { nodeIntegration: false, contextIsolation: true, preload: path.join(__dirname, 'preload.js') },
    x: config.x ?? screenW - 300, y: config.y ?? 50
  });

  const htmlPath = app.isPackaged ? path.join(process.resourcesPath, 'app.html') : path.join(__dirname, 'index.html');
  mainWindow.loadFile(htmlPath);

  mainWindow.on('moved', () => { const [x, y] = mainWindow.getPosition(); saveConfig({ x, y }); });
  mainWindow.on('closed', () => { mainWindow = null; });
  startPython();
});

app.on('before-quit', stopPython);
app.on('window-all-closed', () => app.quit());

ipcMain.handle('close-widget', () => { stopPython(); app.quit(); });
ipcMain.handle('get-auto-start', () => app.getLoginItemSettings().openAtLogin);
ipcMain.handle('set-auto-start', (e, enabled) => {
  app.setLoginItemSettings({ openAtLogin: enabled, name: '系统监控', path: process.execPath });
  return enabled;
});
ipcMain.handle('get-always-on-top', () => {
  return mainWindow && !mainWindow.isDestroyed() ? mainWindow.isAlwaysOnTop() : false;
});
ipcMain.handle('set-always-on-top', (e, enabled) => {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.setAlwaysOnTop(enabled);
  saveConfig({ alwaysOnTop: enabled });
  return enabled;
});
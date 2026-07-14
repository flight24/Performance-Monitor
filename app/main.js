const { app, BrowserWindow, screen, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn, spawnSync } = require('child_process');

const configPath = path.join(app.getPath('userData'), 'monitor-config.json');
const TASK_NAME = 'SystemMonitor';
const APP_DIR = path.join(app.getPath('appData'), TASK_NAME);

function loadConfig() {
  try { return JSON.parse(fs.readFileSync(configPath, 'utf-8')); } catch { return {}; }
}
function saveConfig(data) {
  try { fs.writeFileSync(configPath, JSON.stringify({ ...loadConfig(), ...data })); } catch {}
}

let mainWindow, pythonProcess;
let crashCount = 0;
let restartTimer = null;

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
    args = [path.join(__dirname, '..', 'python', 'monitor.py')];
    cwd = path.join(__dirname, '..', 'python');
  }

  pythonProcess = spawn(exePath, args, {
    cwd: cwd, stdio: ['pipe', 'pipe', 'pipe'],
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

  pythonProcess.stderr.on('data', (data) => {
    console.error('[backend]', data.toString().trim());
  });

  pythonProcess.on('close', (code) => {
    console.error('[backend] exited with code', code);
    pythonProcess = null;
    if (mainWindow && !mainWindow.isDestroyed()) {
      crashCount++;
      const delay = Math.min(1000 * crashCount, 10000);
      restartTimer = setTimeout(() => { restartTimer = null; startPython(); }, delay);
    }
  });

  pythonProcess.on('error', (err) => {
    console.error('[backend] error:', err.message);
  });
}

function stopPython() {
  if (restartTimer) { clearTimeout(restartTimer); restartTimer = null; }
  if (pythonProcess) {
    try { pythonProcess.kill(); } catch {}
    pythonProcess = null;
  }
}

app.whenReady().then(() => {
  const config = loadConfig();
  const { width: screenW } = screen.getPrimaryDisplay().workAreaSize;

  if (app.getLoginItemSettings().openAtLogin) {
    app.setLoginItemSettings({ openAtLogin: false });
  }
  const legacyStartup = path.join(app.getPath('appData'), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup', getExeName());
  try { if (fs.statSync(legacyStartup).isFile()) fs.unlinkSync(legacyStartup); } catch {}

  mainWindow = new BrowserWindow({
    width: 280, height: 330, frame: false, transparent: true, type: 'toolbar',
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

function getExeName() {
  const portableExe = process.env.PORTABLE_EXECUTABLE_FILE;
  if (portableExe) return path.basename(portableExe);
  return path.basename(process.execPath);
}

ipcMain.handle('close-widget', () => { stopPython(); app.quit(); });
ipcMain.handle('get-auto-start', () => {
  const r = spawnSync('schtasks.exe', ['/query', '/tn', TASK_NAME, '/fo', 'list'], { timeout: 5000 });
  return r.status === 0;
});
ipcMain.handle('set-auto-start', (e, enabled) => {
  if (enabled) {
    const src = process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;
    try {
      fs.mkdirSync(APP_DIR, { recursive: true });
      const target = path.join(APP_DIR, getExeName());
      fs.copyFileSync(src, target);
      const r = spawnSync('schtasks.exe', [
        '/create', '/tn', TASK_NAME, '/tr', target,
        '/sc', 'onlogon', '/it', '/rl', 'highest', '/f'
      ], { timeout: 10000, encoding: 'utf-8' });
      if (r.status !== 0) {
        console.error('task create failed:', r.stderr.trim());
        return false;
      }
      console.log('task created');
    } catch (err) {
      console.error('set-auto-start failed:', err.message);
      return false;
    }
  } else {
    spawnSync('schtasks.exe', ['/delete', '/tn', TASK_NAME, '/f'], { timeout: 10000 });
    try { fs.rmSync(APP_DIR, { recursive: true, force: true }); } catch {}
  }
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
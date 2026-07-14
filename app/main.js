const { app, BrowserWindow, screen, ipcMain, dialog } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn, spawnSync } = require('child_process');

const configPath = path.join(app.getPath('userData'), 'monitor-config.json');
const TASK_NAME = 'SystemMonitor';
const APP_DIR = path.join(app.getPath('appData'), TASK_NAME);
const SCHTASKS = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'schtasks.exe');

function loadConfig() {
  try { return JSON.parse(fs.readFileSync(configPath, 'utf-8')); } catch { return {}; }
}
function saveConfig(data) {
  try { fs.writeFileSync(configPath, JSON.stringify({ ...loadConfig(), ...data })); } catch {}
}

let isSecondaryInstance = false;

const gotTheLock = app.requestSingleInstanceLock();
if (!gotTheLock) {
  isSecondaryInstance = true;
  app.whenReady().then(() => {
    dialog.showMessageBoxSync({ type: 'warning', title: '提示', message: '程序已在运行中', detail: '请勿重复运行' });
    app.quit();
  });
}

let mainWindow, pythonProcess;
let alwaysOnTopEnabled = false;
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
  if (isSecondaryInstance) return;
  const config = loadConfig();
  const { width: screenW } = screen.getPrimaryDisplay().workAreaSize;

  const legacyStartup = path.join(app.getPath('appData'), 'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup', getExeName());
  try { if (fs.statSync(legacyStartup).isFile()) fs.unlinkSync(legacyStartup); } catch {}

  alwaysOnTopEnabled = config.alwaysOnTop ?? false;

  mainWindow = new BrowserWindow({
    width: 280, height: 330, frame: false, transparent: true, type: 'toolbar',
    alwaysOnTop: alwaysOnTopEnabled, skipTaskbar: true, resizable: false,
    webPreferences: { nodeIntegration: false, contextIsolation: true, preload: path.join(__dirname, 'preload.js') },
    x: config.x ?? screenW - 300, y: config.y ?? 50
  });

  if (!alwaysOnTopEnabled) mainWindow.blur();

  const htmlPath = app.isPackaged ? path.join(process.resourcesPath, 'app.html') : path.join(__dirname, 'index.html');
  mainWindow.loadFile(htmlPath);

  mainWindow.on('focus', () => { if (!alwaysOnTopEnabled) mainWindow.blur(); });
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
  const r = spawnSync(SCHTASKS, ['/query', '/tn', TASK_NAME, '/fo', 'list'], { timeout: 5000 });
  return r.status === 0;
});
ipcMain.handle('set-auto-start', (e, enabled) => {
  if (enabled) {
    const src = process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;
    try {
      fs.mkdirSync(APP_DIR, { recursive: true });
      const exeTarget = path.join(APP_DIR, getExeName());
      fs.copyFileSync(src, exeTarget);
      const r = spawnSync(SCHTASKS, [
        '/create', '/tn', TASK_NAME, '/tr', exeTarget,
        '/sc', 'onlogon', '/it', '/rl', 'highest', '/f'
      ], { timeout: 10000, encoding: 'utf-8' });
      if (r.status !== 0) {
        console.error('task create failed:', r.stderr.trim());
        try { fs.rmSync(APP_DIR, { recursive: true, force: true }); } catch {}
        return false;
      }
      console.log('task created');
    } catch (err) {
      console.error('set-auto-start failed:', err.message);
      try { fs.rmSync(APP_DIR, { recursive: true, force: true }); } catch {}
      return false;
    }
  } else {
    const r = spawnSync(SCHTASKS, ['/delete', '/tn', TASK_NAME, '/f'], { timeout: 10000 });
    return r.status === 0;
  }
  return true;
});
ipcMain.handle('get-always-on-top', () => alwaysOnTopEnabled);
ipcMain.handle('set-always-on-top', (e, enabled) => {
  alwaysOnTopEnabled = enabled;
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.setAlwaysOnTop(enabled);
    if (!enabled) mainWindow.blur();
  }
  saveConfig({ alwaysOnTop: enabled });
  return enabled;
});
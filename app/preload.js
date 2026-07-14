const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  onSystemData: (callback) => {
    ipcRenderer.on('system-data', (event, data) => callback(data));
  },
  closeWidget: () => {
    ipcRenderer.invoke('close-widget');
  },
  getAutoStart: () => {
    return ipcRenderer.invoke('get-auto-start');
  },
  setAutoStart: (enabled) => {
    return ipcRenderer.invoke('set-auto-start', enabled);
  },
  getAlwaysOnTop: () => {
    return ipcRenderer.invoke('get-always-on-top');
  },
  setAlwaysOnTop: (enabled) => {
    return ipcRenderer.invoke('set-always-on-top', enabled);
  }
});
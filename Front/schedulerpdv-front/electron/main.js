const { app, BrowserWindow } = require('electron');
const { spawn } = require('child_process');
const fs = require('fs');
const http = require('http');
const path = require('path');

const isDev = !app.isPackaged;
let backendProcess = null;
let logStream = null;

function openLog() {
  if (logStream) {
    return logStream;
  }

  const exeDir = path.dirname(app.getPath('exe'));
  const logPath = path.join(exeDir, 'schedulerpdv.log');
  logStream = fs.createWriteStream(logPath, { flags: 'a' });
  return logStream;
}

function log(message) {
  const line = `[${new Date().toISOString()}] ${message}\n`;
  if (isDev) {
    console.log(line.trim());
    return;
  }

  const stream = openLog();
  stream.write(line);
}

function getBackendPath() {
  if (isDev) {
    return null;
  }

  return path.join(process.resourcesPath, 'Backend', 'SchedulerPDV.Api.exe');
}

function startBackend() {
  const backendPath = getBackendPath();
  if (!backendPath) {
    return;
  }

  log(`Starting backend: ${backendPath}`);
  if (!fs.existsSync(backendPath)) {
    log(`Backend exe not found at ${backendPath}`);
    return;
  }
  backendProcess = spawn(backendPath, {
    windowsHide: true,
    stdio: 'ignore',
  });

  backendProcess.on('exit', (code) => {
    log(`Backend exited with code ${code}`);
  });
}

function stopBackend() {
  if (backendProcess) {
    backendProcess.kill();
    backendProcess = null;
    log('Backend stopped');
  }
}

function waitForBackend(url, timeoutMs = 10000) {
  const start = Date.now();
  return new Promise((resolve, reject) => {
    const check = () => {
      const req = http.get(url, (res) => {
        res.resume();
        if (res.statusCode && res.statusCode >= 200 && res.statusCode < 300) {
          resolve();
          return;
        }
        retry();
      });
      req.on('error', retry);
    };

    const retry = () => {
      if (Date.now() - start > timeoutMs) {
        reject(new Error('Backend not ready'));
        return;
      }
      setTimeout(check, 300);
    };

    check();
  });
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    backgroundColor: '#0f172a',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });

  if (isDev) {
    win.loadURL('http://localhost:4200');
    win.webContents.openDevTools({ mode: 'detach' });
  } else {
    const indexPath = path.join(app.getAppPath(), 'dist', 'schedulerpdv-front', 'browser', 'index.html');
    if (!fs.existsSync(indexPath)) {
      log(`Index not found at ${indexPath}`);
    }
    win.loadFile(indexPath);
  }

  win.webContents.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL) => {
    log(`Load failed (${errorCode}): ${errorDescription} - ${validatedURL}`);
  });
}

app.whenReady().then(async () => {
  startBackend();

  if (!isDev) {
    try {
      await waitForBackend('http://localhost:5031/api/health');
    } catch (err) {
      log(`Backend not ready: ${(err && err.message) || err}`);
    }
  }

  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    stopBackend();
    app.quit();
  }
});

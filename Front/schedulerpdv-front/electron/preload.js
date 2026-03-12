const { contextBridge } = require('electron');

contextBridge.exposeInMainWorld('schedulerApi', {
  backendUrl: 'http://localhost:5031',
});

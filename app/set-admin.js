const { execSync } = require('child_process');
const path = require('path');

module.exports = async function(context) {
  const exePath = path.join(context.appOutDir, '系统监控.exe');
  try {
    execSync(`powershell -Command "Set-Content -Path '${exePath}:manifest' -Value ''"`, { stdio: 'ignore' });
  } catch {}
  try {
    const rcedit = require('rcedit');
    await rcedit(exePath, {
      'requested-execution-level': 'requireAdministrator'
    });
    console.log('Admin manifest applied');
  } catch (e) {
    console.log('rcedit failed:', e.message);
  }
};
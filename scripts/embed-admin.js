exports.default = async function (context) {
  const path = require('path');
  const exeName = context.packager.appInfo.productFilename;
  const exePath = path.join(context.appOutDir, `${exeName}.exe`);
  await require('rcedit')(exePath, {
    'requested-execution-level': 'requireAdministrator'
  });
  console.log('Admin manifest embedded:', exePath);
};

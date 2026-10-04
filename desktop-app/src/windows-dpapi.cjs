'use strict';

// Windows PowerShell 5.1's ConvertFrom-SecureString uses the current Windows
// user's DPAPI key when no -Key is provided. Plaintext travels ONLY via the
// child process's stdin/stdout (never command-line arguments or Git files).
const { execFileSync } = require('node:child_process');
const SCRIPT = String.raw`
$ErrorActionPreference = 'Stop'
$inputText = [Console]::In.ReadToEnd()
if ($env:BAZINO_DPAPI_MODE -eq 'protect') {
  $secure = ConvertTo-SecureString -String $inputText -AsPlainText -Force
  [Console]::Out.Write((ConvertFrom-SecureString -SecureString $secure))
} elseif ($env:BAZINO_DPAPI_MODE -eq 'unprotect') {
  $secure = ConvertTo-SecureString -String $inputText
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { [Console]::Out.Write([Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
} else { throw 'Unknown DPAPI action' }
`;
const encoded = Buffer.from(SCRIPT, 'utf16le').toString('base64');
function run(action, value) {
  if (process.platform !== 'win32') throw new Error('Credential protection requires Windows');
  try {
    return execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', encoded], {
      input:value, encoding:'utf8', windowsHide:true, timeout:10000, maxBuffer:512*1024,
      env:{ ...process.env, BAZINO_DPAPI_MODE:action }, stdio:['pipe','pipe','pipe']
    });
  } catch {
    // Never surface the PowerShell stderr: it can contain part of a bad secret.
    throw new Error('Windows credential protection failed for this user. No secret was saved.');
  }
}
const windowsStorage = {
  isEncryptionAvailable: () => process.platform === 'win32',
  encryptString: text => Buffer.from(run('protect', text), 'utf8'),
  decryptString: bytes => run('unprotect', bytes.toString('utf8'))
};
module.exports = { windowsStorage };

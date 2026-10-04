'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {execFileSync}=require('node:child_process');
const root=path.resolve(__dirname,'..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('Windows build uses a native launcher and official MCP, never Electron or a regional CLI',()=>{
  const pkg=require('../package.json');
  const workflow=read('workflow-build-marketing.yml');
  assert.equal(pkg.dependencies['@modelcontextprotocol/sdk'],'1.30.1');
  assert.equal(pkg.dependencies.electron,undefined);
  assert.equal(pkg.devDependencies.electron,undefined);
  assert.match(workflow,/windows-latest/);
  assert.match(workflow,/Invoke-PS2EXE/);
  assert.match(workflow,/node\.exe/);
  assert.match(workflow,/npm test/);
  assert.match(workflow,/actions\/upload-artifact/);
  assert.ok(!workflow.includes('desktop-app/relay/'));
  assert.ok(!workflow.includes('agent-client.cjs'));
  assert.equal(fs.existsSync(path.join(root,'src','main.cjs')),false);
  assert.equal(fs.existsSync(path.join(root,'src','preload.cjs')),false);
  assert.equal(fs.existsSync(path.join(root,'src','server.cjs')),true);
  assert.equal(fs.existsSync(path.join(root,'src','restart-existing.cjs')),true);
  assert.equal(fs.existsSync(path.join(root,'src','server-address.cjs')),true);
  assert.equal(fs.existsSync(path.join(root,'src','youtube-oauth.cjs')),false);
  assert.match(read('src/constants.cjs'),/https:\/\/kling\.ai\/mcp/);
  assert.match(read('src/constants.cjs'),/https:\/\/bazino\.pro\/admin\/content/);
  const html=read('src/index.html');
  for(const forbidden of ['data-secret="youtubeClientSecret"','data-secret="telegramBotToken"',
    'data-secret="manusApiKey"','id="upload-youtube"','id="send-telegram-media"'])
    assert.ok(!html.includes(forbidden),`Obsolete direct integration: ${forbidden}`);
});

test('local PowerShell build has separate targets and no GitHub artifact upload',()=>{
  const script=read('build-local.ps1');
  assert.match(script,/ValidateSet\('Studio', 'Bridge', 'All'\)/);
  assert.match(script,/npm\.cmd/);
  assert.match(script,/ci', '--omit=dev'/);
  assert.match(script,/BazinoMarketing-Windows-x64\.zip/);
  assert.match(script,/BazinoStudioBridge\.exe/);
  assert.match(script,/Test-ExeSelfTest/);
  assert.match(script,/src\\restart-existing\.cjs/);
  assert.match(script,/Get-FileHash/);
  assert.doesNotMatch(script,/actions\/upload-artifact|gh\s+(?:api|run|workflow)\b|Invoke-RestMethod/i);
  const launcher=read('BazinoMarketing.ps1');
  assert.match(launcher,/Local\\BazinoMarketingStudioStartup/);
  assert.match(launcher,/\$success = \(Test-Path \$server\) -and \(Test-Path \$restart\)/);
  const preflight=launcher.indexOf('$preflight = & $node $restart');
  const spawn=launcher.indexOf('$running = Start-Process');
  const ready=launcher.indexOf('$ready = & $node $restart --wait-ready');
  const unlock=launcher.indexOf('$startupLock.ReleaseMutex()');
  assert.ok(preflight>0&&spawn>preflight&&ready>spawn&&unlock>ready,
    'serialized startup must stop old server before spawning and verify new server before unlocking');
  assert.doesNotMatch(launcher,/Stop-Process|Get-NetTCPConnection/);
  if(process.platform==='win32'){
    const file=path.join(root,'build-local.ps1').replaceAll("'","''");
    const command=`$tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile('${file}',[ref]$tokens,[ref]$errors) | Out-Null; if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }`;
    execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-Command',command],{timeout:15000});
  }
});

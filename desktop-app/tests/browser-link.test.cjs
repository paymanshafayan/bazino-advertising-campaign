'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {BRANCH,BUS,STUDIO_URL,safeNav}=require('../browser-link/agent-session.cjs');
const script=fs.readFileSync(path.resolve(__dirname,'../browser-link/BazinoStudioBridge.ps1'),'utf8');
const workflow=fs.readFileSync(path.resolve(__dirname,'../browser-link/workflow-build-studio-bridge.yml'),'utf8');
const root=path.resolve(__dirname,'../../..');

test('browser adapter and agent client are pinned to the tracked branch and an isolated path',()=>{
  assert.equal(BRANCH,'arena/01a0d4ee-bazino-gamenet-portal');
  assert.equal(BUS,'marketing-browser-bus');
  assert.ok(fs.existsSync(path.join(root,BUS,'cmd/.gitkeep')));
  assert.ok(fs.existsSync(path.join(root,BUS,'res/.gitkeep')));
  assert.match(script,/BusBranch\s+= 'arena\/01a0d4ee-bazino-gamenet-portal'/);
  assert.match(script,/BusRoot\s+= 'marketing-browser-bus'/);
  assert.match(script,/contents\/\{2\}\/cmd\?ref=\{3\}/);
  assert.match(script,/contents\/\{2\}\/\{3\}\?ref=\{4\}/);
  assert.match(script,/contents\/\{2\}\/cmd\/\{3\}\.json/);
  assert.match(script,/SessionStarted/);
  assert.match(script,/TotalMinutes -ge 30/);
  assert.match(script,/SENSITIVE_RESULT_BLOCKED/);
  assert.match(script,/private -ne \$true/);
  assert.doesNotMatch(script,/BusBranch\s+= 'cdp-bus'/);
  assert.match(script,/ChromePort\s+= 9334/);
  assert.match(script,/software\s+= 'BazinoStudioBridge'/);
  assert.match(script,/Bazino Studio Bridge - Settings/);
  assert.match(script,/Bazino Studio Bridge - \$\(\$script:Cfg.Repo\)/);
  assert.match(script,/Global\\BazinoStudioBridge-portal/);
  assert.match(workflow,/\.github\/workflows\/build-studio-bridge\.yml/);
  assert.match(workflow,/BazinoStudioBridge\.exe/);
  assert.match(workflow,/BazinoStudioBridge-Windows/);
  assert.doesNotMatch(workflow,/cdp-bus/);
});

test('browser navigation is restricted to official sites without OAuth codes in URL',()=>{
  assert.equal(STUDIO_URL,'http://127.0.0.1:59670/');
  assert.equal(safeNav('https://github.com/paymanshafayan/bazino-gamenet-portal'),
    'https://github.com/paymanshafayan/bazino-gamenet-portal');
  for(const value of [STUDIO_URL,'http://github.com','https://evil.example','https://github.com.evil.example',
    'https://kling.ai/callback?code=SENSITIVE','https://github.com/?access_token=ABC']){
    assert.throws(()=>safeNav(value));
  }
});

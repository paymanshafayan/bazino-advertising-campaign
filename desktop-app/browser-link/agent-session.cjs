#!/usr/bin/env node
'use strict';
// Browser adapter client: commands/results on the FIXED Arena branch only.
// No cdp-bus branch, API credentials, cookies or OAuth codes in this channel.
const { execFileSync }=require('node:child_process');
const fs=require('node:fs');
const path=require('node:path');
const { BRANCH,REPO }=require('../src/constants.cjs');
const ROOT=path.resolve(__dirname,'../../..');
const BUS='marketing-browser-bus';
const EXPECTED='arena/01a0d4ee-bazino-gamenet-portal';
if(BRANCH!==EXPECTED)throw new Error('This client may write only the tracked Arena session branch');
const sleep=ms=>Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,ms);
function run(bin,args,options={}){return execFileSync(bin,args,{cwd:ROOT,encoding:'utf8',stdio:['ignore','pipe','pipe'],maxBuffer:8*1024*1024,...options}).trim();}
function api(item){return `repos/${REPO}/contents/${BUS}/${item}?ref=${encodeURIComponent(BRANCH)}`;}
function gh(pathname){return run('gh',['api',pathname]);}
function read(item){
  try{
    const base64=run('gh',['api',api(item),'--jq','.content']);
    return JSON.parse(Buffer.from(base64.replace(/\s+/g,''),'base64').toString('utf8'));
  }catch(e){if(/404|Not Found/.test(String(e.message)))return null;throw e;}
}
function list(dir){
  try{return JSON.parse(gh(api(dir)));}
  catch(e){if(/404|Not Found/.test(String(e.message)))return [];throw e;}
}
function status(){
  const s=read('status.json');
  if(!s){console.log('WAITING: session-branch browser adapter has not connected');return false;}
  const age=Math.round((Date.now()-Number(s.lastCycleMs||0))/1000);
  const alive=s.bridge==='connected'&&age>=0&&age<=180;
  console.log(`adapter: ${s.bridge||'?'} · Chrome ${s.chrome||'?'} · heartbeat ${age}s ago · ${alive?'LIVE':'STALE'}`);
  return alive;
}
function assertCurrent(){
  if(run('git',['branch','--show-current'])!==BRANCH)throw new Error('Wrong branch: cannot send browser commands');
  if(run('git',['status','--porcelain']))throw new Error('Commit/finish all other work before using browser bus');
  run('git',['fetch','-q','origin',BRANCH]);
  if(run('git',['rev-parse','HEAD'])!==run('git',['rev-parse','FETCH_HEAD']))run('git',['merge','--ff-only','FETCH_HEAD']);
}
function nextSequence(){
  const files=[...list('cmd'),...list('res')];
  const nums=files.map(file=>/^(\d+)\.json$/.exec(file.name)).filter(Boolean).map(m=>Number(m[1]));
  return Math.max(0,...nums)+1;
}
function publish(seq,cmd){
  assertCurrent();
  const relative=`${BUS}/cmd/${seq}.json`,local=path.join(ROOT,relative);
  fs.writeFileSync(local,JSON.stringify(cmd)+'\n',{flag:'wx',mode:0o600});
  run('git',['add','--',relative]);
  run('git',['-c','user.name=bazino-agent','-c','user.email=agent@bazino.local','commit','-m',`browser bus: command ${seq}`]);
  for(let attempt=0;attempt<6;attempt++){
    try{run('git',['push','origin',BRANCH]);return;}
    catch(e){
      if(attempt===5)throw e;
      sleep((attempt+1)*200);
      run('git',['fetch','-q','origin',BRANCH]);
      run('git',['rebase','FETCH_HEAD']);
    }
  }
}
function dispatch(method,params={},timeoutMs=120000){
  if(!status())throw new Error('Session-branch adapter is not live. Start BazinoStudioBridge.ps1 and press Connect first.');
  const seq=nextSequence();
  const id=Date.now()%1000000000;
  publish(seq,{id,method,params});
  const until=Date.now()+timeoutMs;
  while(Date.now()<until){
    const value=read(`res/${seq}.json`);
    if(value){
      const reply=Array.isArray(value)?value.find(x=>x&&typeof x.ok==='boolean'):value;
      if(!reply||reply.id!==id)throw new Error('Wrong browser response ID: investigate before retrying');
      if(reply.ok!==true)throw new Error('Browser command failed: '+String(reply.error||'unknown'));
      return reply.result;
    }
    sleep(2000);
  }
  throw new Error(`No response to browser command ${seq}; do not blindly repeat a write`);
}
function safeNav(raw){
  const u=new URL(raw);
  if(u.protocol!=='https:'||!['github.com','kling.ai'].includes(u.hostname)||u.username||u.password)
    throw new Error('Browser navigation restricted to github.com and kling.ai');
  if([...u.searchParams.keys()].some(key=>/code|token|secret|password|key/i.test(key)))
    throw new Error('Never put authorization codes or credentials on the browser bus');
  return u.href;
}
function main(){
  const [action,...args]=process.argv.slice(2);
  if(action==='status'){status();return;}
  if(action==='targets'){console.log(JSON.stringify(dispatch('Bridge.targets'),null,2));return;}
  if(action==='select'){
    if(!/^[0-9A-F]{16,64}$/i.test(args[0]||''))throw new Error('Expected a Chrome target ID');
    console.log(JSON.stringify(dispatch('Bridge.select',{targetId:args[0]}),null,2));return;
  }
  if(action==='nav'){console.log(JSON.stringify(dispatch('Page.navigate',{url:safeNav(args[0])}),null,2));return;}
  if(action==='eval'){
    const expr=args[0];
    if(typeof expr!=='string'||expr.length>12000)throw new Error('Expected a short JavaScript expression');
    if(/(?:document\.cookie|localStorage|sessionStorage|authorization|access_token|refresh_token|password|client_secret)/i.test(expr))
      throw new Error('Sensitive browser state must never be copied into the Git bus');
    const reply=dispatch('Runtime.evaluate',{expression:expr,returnByValue:true,awaitPromise:true});
    const result=reply?.result;
    if(!result||reply.exceptionDetails)throw new Error('Browser evaluation failed (no data logged)');
    console.log(JSON.stringify(result.value===undefined?null:result.value));return;
  }
  throw new Error('Usage: node agent-session.cjs status|targets|select <targetId>|nav <https://github.com/...>|eval <js>');
}
if(require.main===module)try{main();}catch(e){console.error(String(e.message||e));process.exitCode=1;}
module.exports={BUS,BRANCH,safeNav,status};

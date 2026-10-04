'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {JSDOM}=require('jsdom');
const {createService}=require('../src/server.cjs');
const {BUILD_ID,createDiagnostics,failureReason}=require('../src/diagnostics.cjs');

test('copyable diagnostics are bounded and allowlist-only, never raw errors or owner data',()=>{
  assert.match(BUILD_ID,/^studio-logs-/);
  const log=createDiagnostics({limit:2,now:()=>new Date('2026-09-26T12:00:00Z')});
  log.add('operation','OP_FAILED',{kind:'research',reason:'NETWORK',status:503});
  log.add('owner-token=VERY_PRIVATE','https://example.com/?code=OWNER_CODE',{
    action:'relay:connect?token=PRIVATE',kind:'api OWNER_KEY',reason:'NET_OWNER_KEY',
    status:'401 OWNER_SECRET'});
  let rows=log.list();
  assert.equal(rows.length,2);
  assert.deepEqual(rows[0],{seq:1,at:'2026-09-26T12:00:00.000Z',
    component:'operation',code:'OP_FAILED',kind:'research',status:503,reason:'NETWORK'});
  assert.deepEqual(rows[1],{seq:2,at:'2026-09-26T12:00:00.000Z',
    component:'server',code:'REQUEST_FAILED',reason:'OTHER'});
  log.add('relay','RELAY_CONNECTED');
  rows=log.list();
  assert.deepEqual(rows.map(row=>row.seq),[2,3], 'old events are dropped');
  assert.doesNotMatch(JSON.stringify(rows),/OWNER|PRIVATE|https|token|code=/i);
});

test('network and authorization failures become safe categories instead of private exception text',()=>{
  const secret='SECRET_IN_ERROR_SHOULD_NOT_LEAK';
  assert.equal(failureReason(Object.assign(new TypeError(`fetch failed ${secret}`),
    {cause:Object.assign(new Error(secret),{code:'ECONNRESET'})})),'NET_ECONNRESET');
  assert.equal(failureReason(new Error(`GitHub 403 on GET /private/${secret}`)),'HTTP_403');
  assert.equal(failureReason(new Error(`OAuth token expired ${secret}`)),'AUTH');
  assert.equal(failureReason(new Error(`unrecognized ${secret}`)),'OTHER');
});

test('real local server and browser scripts show copyable diagnostics with no live credentials',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-log-browser-'));
  const storage={isEncryptionAvailable:()=>true,encryptString:text=>Buffer.from(text),
    decryptString:bytes=>bytes.toString('utf8')};
  const secret='PRIVATE_FEED_ERROR_MUST_NOT_COPY';
  const app=createService({baseDir:dir,port:0,storage,
    researchFactory:()=>({scan:async()=>({ok:false,sources:[
      {source:'vgc',items:[],error:`fetch failed ${secret}`} ]})})});
  let dom;
  const until=async(predicate)=>{
    const deadline=Date.now()+5000;
    while(Date.now()<deadline){
      if(predicate())return;
      await new Promise(resolve=>setTimeout(resolve,30));
    }
    throw new Error('Browser diagnostics did not become ready');
  };
  try{
    await app.listen();
    const url=`http://127.0.0.1:${app.server.address().port}/`;
    dom=await JSDOM.fromURL(url,{resources:'usable',runScripts:'dangerously',
      pretendToBeVisual:true,beforeParse(window){
        window.scrollTo=()=>{};window.confirm=()=>false;
        window.fetch=(relative,options={})=>fetch(new URL(relative,window.location.href),{
          ...options,headers:{...options.headers,Origin:window.location.origin}
        });
      }});
    const {window}=dom;
    await until(()=>window.document.querySelector('#diagnostics-text')?.value.includes('UI_BUILD='));
    const result=await window.marketing.run({kind:'research',action:'scan',sourceIds:['vgc']});
    assert.equal(result.ok,false);
    window.document.querySelector('[data-nav="diagnostics"]').click();
    await until(()=>window.document.querySelector('#diagnostics-text').value.includes('OP_FAILED kind=research'));
    const log=window.document.querySelector('#diagnostics-text').value;
    assert.match(log,/UI_BUILD=studio-logs-2026-09-26\.1/);
    assert.match(log,/SERVER_BUILD=studio-logs-2026-09-26\.1/);
    assert.match(log,/STYLE=CSS_OK/);
    assert.match(log,/OP_FAILED kind=research reason=NETWORK/);
    assert.doesNotMatch(log,new RegExp(secret));
    assert.ok(!log.includes(app.session),'the browser session must not appear in copied logs');
  }finally{dom?.window.close();await app.close();fs.rmSync(dir,{recursive:true,force:true});}
});

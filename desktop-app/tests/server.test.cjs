'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {createService,monitorLauncher}=require('../src/server.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:text=>Buffer.from('ENC:'+Buffer.from(text).toString('base64')),
  decryptString:bytes=>Buffer.from(bytes.toString('utf8').slice(4),'base64').toString('utf8')};

test('local browser server isolates session, requires same-origin token, and approves writes natively',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-http-test-'));
  const calls=[],opened=[];let approve=false;
  const app=createService({baseDir:dir,port:0,storage,approval:async()=>approve,launch:async url=>opened.push(url),
    klingFactory:()=>({close:async()=>{},execute:async()=>({ok:true})}),
    researchFactory:()=>({scan:async(ids)=>({ok:true,sourceIds:ids,sources:[]})}),
    providersFactory:()=>({api:async op=>{calls.push(op);return {ok:true,body:{name:'mock account'}};},
      access:async()=>({accounts:[{accountId:'ig-one',platform:'instagram',connected:true,canPost:true}]}),
      engagementAccess:async id=>({ok:true,accountId:id,commentsReadable:true,messagesReadable:false})})});
  try{
    await app.listen();const port=app.server.address().port;
    const origin=`http://127.0.0.1:${port}`;
    let res=await fetch(`${origin}/`);
    assert.equal(res.status,200);
    assert.match(res.headers.get('content-security-policy'),/connect-src 'self'/);
    assert.match(res.headers.get('x-frame-options'),/DENY/);
    const html=await res.text();
    assert.ok(html.includes(`content="${app.session}"`));
    assert.ok(!html.includes('nodeIntegration'));
    const post=(action,arg,headers={})=>fetch(`${origin}/api/dispatch`,{method:'POST',
      headers:{'Content-Type':'application/json','Origin':origin,'X-Bazino-Session':app.session,...headers},
      body:JSON.stringify({action,arg})});
    res=await fetch(`${origin}/api/dispatch`,{method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify({action:'startup'})});assert.equal(res.status,403);
    res=await post('startup',undefined,{Origin:'https://evil.example'});assert.equal(res.status,403);
    res=await post('startup',undefined,{'X-Bazino-Session':'wrong'});assert.equal(res.status,403);
    res=await post('startup');assert.equal(res.status,200);
    const startup=await res.json();assert.equal(startup.settings.klingAuthorized,false);
    assert.equal(startup.settings.configured.youtubeRefreshToken,undefined);
    assert.ok(startup.researchFeeds.some(feed=>feed.id==='merlin'));
    assert.equal(startup.editorialCount,0);
    assert.equal((await(await post('operation:run',{kind:'zernio-access'})).json()).accounts[0].accountId,'ig-one');
    assert.equal((await(await post('operation:run',{kind:'zernio-engagement-check',accountId:'ig-one'})).json()).messagesReadable,false);
    assert.deepEqual((await(await post('operation:run',{kind:'research',action:'scan',sourceIds:['vgc']})).json()).sourceIds,['vgc']);
    const saved=await(await post('operation:run',{kind:'editorial',action:'save',report:{
      title:'Test unverified lead',game:'Game',category:'news',sources:[],facts:'',variants:{}}})).json();
    assert.equal(saved.ok,true);assert.equal(saved.report.verifiedBySoftware,false);
    assert.equal((await(await post('operation:run',{kind:'editorial',action:'list'})).json()).reports.length,1);
    assert.ok(!fs.readFileSync(path.join(dir,'editorial.json'),'utf8').includes('Test unverified lead'));
    assert.equal((await(await post('startup')).json()).editorialCount,1);
    assert.equal((await post('external:open','https://bazino.pro/admin/content')).status,200);
    assert.deepEqual(opened,['https://bazino.pro/admin/content']);
    assert.equal((await post('external:open','https://bazino.pro/api/admin/integration-secrets/cloudflare_api_token')).status,400);
    assert.equal((await post('external:open','https://kling.ai/mcp?code=secret')).status,400);
    assert.deepEqual(opened,['https://bazino.pro/admin/content']);
    const op={kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts'};
    const read=await(await post('operation:run',op)).json();
    assert.equal(read.body.name,'mock account');assert.equal(calls.length,1);
    const write={...op,method:'POST',path:'/v1/posts',body:{content:'draft'}};
    assert.equal((await(await post('operation:run',write)).json()).error,'DECLINED_BY_OWNER');
    assert.equal(calls.length,1);
    approve=true;assert.equal((await(await post('operation:run',write)).json()).ok,true);
    assert.equal(calls.length,2);
    res=await fetch(`${origin}/api/assets/import`,{method:'POST',
      headers:{Origin:origin,'X-Bazino-Session':app.session,'X-Bazino-Filename':'test.jpg',
        'Content-Type':'application/octet-stream'},body:Buffer.from('mock jpeg')});
    assert.equal(res.status,200);
    const asset=await res.json();assert.equal(asset.name,'test.jpg');assert.equal(app.assets.list().length,1);
    res=await post('events',0);assert.ok((await res.json()).rows.length>=3);
    const attack=await new Promise((resolve,reject)=>require('node:http').get({host:'127.0.0.1',port,path:'/',headers:{Host:'evil.example'}},resolve).on('error',reject));
    assert.equal(attack.statusCode,403);attack.resume();
  }finally{await app.close();fs.rmSync(dir,{recursive:true,force:true});}
});

test('closing the last authenticated browser tab releases the port; a reload or another tab cancels it',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-close-test-'));
  let stops=0;
  const app=createService({baseDir:dir,port:0,storage,tabCloseGraceMs:30,
    onStop:()=>{stops++;return app.close();},
    klingFactory:()=>({close:async()=>{}}),gatewayFactory:()=>({close:async()=>{}})});
  const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  try{
    await app.listen();
    const port=app.server.address().port,origin=`http://127.0.0.1:${port}`;
    const post=async(action,arg,headers={})=>{
      const res=await fetch(`${origin}/api/dispatch`,{method:'POST',
        headers:{'Content-Type':'application/json',Origin:origin,'X-Bazino-Session':app.session,...headers},
        body:JSON.stringify({action,arg})});
      await res.text();return res;
    };
    const a='tab-aaaaaaaaaa',b='tab-bbbbbbbbbb';
    assert.equal((await post('startup',{tabId:a})).status,200);
    assert.equal((await post('startup',{tabId:b})).status,200);
    assert.equal((await post('ui:leave',{tabId:a}, {'X-Bazino-Session':'wrong'})).status,403);
    assert.equal((await post('ui:leave',{tabId:'bad'})).status,400);
    assert.equal((await post('ui:leave',{tabId:a})).status,200);
    await wait(50);assert.equal(stops,0,'second tab remains open');
    assert.equal((await post('ui:leave',{tabId:b})).status,200);
    await wait(10);
    assert.equal((await post('startup',{tabId:a})).status,200); // a reload within grace
    await wait(50);assert.equal(stops,0,'reload cancelled the pending stop');
    app.kling.activeCallback={}; // a browser may have navigated to Kling in the same tab
    assert.equal((await post('ui:leave',{tabId:a})).status,200);
    await wait(80);assert.equal(stops,0,'a pending callback must not be shut down');
    app.kling.activeCallback=null;
    await wait(100);assert.equal(stops,1);
    await app.close();assert.equal(app.server.listening,false);
    const probe=http.createServer();
    await new Promise((resolve,reject)=>probe.once('error',reject).listen(port,'127.0.0.1',resolve));
    await new Promise(resolve=>probe.close(resolve));
  }finally{await app.close();fs.rmSync(dir,{recursive:true,force:true});}
});

test('launcher disappearance requests shutdown once; developer mode is untouched',async()=>{
  let stops=0;
  assert.equal(monitorLauncher([],()=>stops++),null);
  const timer=monitorLauncher(['--bazino-launcher-pid=424242'],()=>stops++,{
    intervalMs:5,probe:()=>{const e=new Error('missing');e.code='ESRCH';throw e;}});
  await new Promise(resolve=>setTimeout(resolve,35));
  clearInterval(timer);
  assert.equal(stops,1);
});

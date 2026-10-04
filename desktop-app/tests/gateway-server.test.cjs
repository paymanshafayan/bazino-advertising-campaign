'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {createService}=require('../src/server.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:s=>Buffer.from(`ENCRYPTED:${Buffer.from(s).toString('base64')}`),
  decryptString:b=>Buffer.from(b.toString('utf8').slice(10),'base64').toString('utf8')};

test('only local, same-origin owner can register/remove/login; agent sees only shared metadata and API writes need approval',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-gateway-server-'));
  let allow=false,invocations=0,oauthCalls=0;
  const prompts=[],closed=[];
  const app=createService({baseDir:dir,port:0,storage,approval:async data=>{prompts.push(data);return allow;},
    launch:()=>{},klingFactory:()=>({close:async()=>{},execute:async()=>({ok:true})}),
    gatewayFactory:({vault})=>({close:async()=>{},changed:async id=>closed.push(id),
      readPage:async op=>({ok:true,url:op.url,text:'Unverified public page'}),
      request:async op=>{invocations++;return {ok:true,connectorId:op.connectorId,
        body:{token:'ONLY_OWNER_KEY_123456',message:`Bearer ${vault.connector(op.connectorId).credential}`}};},
      executeMcp:async op=>{invocations++;return {ok:true,action:op.action}},
      authorizeMcp:async()=>{oauthCalls++;return {ok:true,message:'Connected'};}})});
  try{
    await app.listen();const origin=`http://127.0.0.1:${app.server.address().port}`;
    const request=(action,arg,other={})=>fetch(`${origin}/api/dispatch`,{
      method:'POST',headers:{Origin:origin,'X-Bazino-Session':app.session,
        'Content-Type':'application/json',...other},body:JSON.stringify({action,arg})});
    const json=async(action,arg)=>await(await request(action,arg)).json();
    const row={id:'vendor-api',name:'Vendor',kind:'api',url:'https://api.vendor.com/v1',
      auth:'bearer',credential:'ONLY_OWNER_KEY_123456',agentAccess:'read'};
    assert.equal((await request('gateway:save',row,{Origin:'https://evil.vendor.com'})).status,403);
    assert.equal((await request('gateway:save',row,{'X-Bazino-Session':'invalid'})).status,403);
    assert.equal((await json('gateway:save',row)).error,'DECLINED_BY_OWNER');
    assert.deepEqual((await json('gateway:list')).connectors,[]);
    allow=true;const saved=await json('gateway:save',row);
    assert.equal(saved.connector.hasCredential,true);
    assert.equal(saved.connector.credential,undefined);
    assert.ok(!JSON.stringify(saved).includes('ONLY_OWNER_KEY_123456'));
    assert.ok(prompts.every(p=>!p.message.includes('ONLY_OWNER_KEY_123456')));
    const startup=await json('startup');
    assert.equal(startup.connectors.length,1);
    assert.equal(startup.settings.connectors,undefined);
    assert.ok(!JSON.stringify(startup).includes('ONLY_OWNER_KEY_123456'));
    assert.deepEqual((await app.relay.service({kind:'gateway-list'})).connectors.map(r=>r.id),['vendor-api']);
    const read={kind:'gateway-api',connectorId:'vendor-api',method:'GET',path:'/items'};
    const local=await json('operation:run',read);
    assert.equal(local.body.token,'[redacted]');
    assert.equal(local.body.message,'Bearer [redacted]');
    assert.equal((await app.relay.service(read)).ok,true);
    const write={...read,method:'POST',body:{action:'example'}};
    assert.equal((await app.relay.service(write)).error,'CONNECTOR_NOT_SHARED_WITH_AGENT');
    const before=invocations;
    allow=false;assert.equal((await json('operation:run',write)).error,'DECLINED_BY_OWNER');
    assert.ok(prompts.some(p=>p.message.includes('https://api.vendor.com/v1')&&
      p.message.includes('"method":"POST"')));
    assert.equal(invocations,before);
    allow=true;
    const shared=await json('gateway:save',{...row,credential:'',agentAccess:'approved-writes'});
    assert.equal(shared.connector.hasCredential,true);
    assert.equal((await app.relay.service(write)).ok,true);
    assert.equal((await json('operation:run',write)).ok,true);
    const mcp={id:'remote-mcp',name:'Remote',kind:'mcp',url:'https://mcp.vendor.com/rpc',
      auth:'oauth',agentAccess:'none'};
    assert.equal((await json('gateway:save',mcp)).ok,true);
    assert.equal((await app.relay.service({kind:'gateway-mcp',connectorId:'remote-mcp',action:'tools'})).error,
      'CONNECTOR_NOT_SHARED_WITH_AGENT');
    assert.equal((await json('gateway:authorize','remote-mcp')).ok,true);
    assert.equal(oauthCalls,1);
    assert.equal((await app.relay.service({kind:'gateway-list'})).connectors.length,1);
    assert.equal((await app.relay.service({kind:'web-fetch',url:'https://news.vendor.com'})).ok,true);
    allow=false;assert.equal((await json('gateway:remove','remote-mcp')).error,'DECLINED_BY_OWNER');
    assert.ok(app.vault.connector('remote-mcp'));
    allow=true;assert.equal((await json('gateway:remove','remote-mcp')).ok,true);
    assert.equal(app.vault.connector('remote-mcp'),null);
    assert.ok(closed.includes('remote-mcp'));
  }finally{await app.close();fs.rmSync(dir,{recursive:true,force:true});}
});

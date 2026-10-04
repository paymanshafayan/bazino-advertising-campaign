'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {Vault}=require('../src/vault.cjs');
const {Relay}=require('../src/relay.cjs');
const {newIdentity,newSigningIdentity,fingerprint,signRequest,verifyReply,seal,unseal}=require('../src/crypto.cjs');
const {REPO,BRANCH,RELAY_ROOT}=require('../src/constants.cjs');
const storage={isEncryptionAvailable:()=>true,encryptString:s=>Buffer.from(s),decryptString:b=>b.toString()};
function repoMock(){
  const files=new Map(),sent=[];
  files.set(`${RELAY_ROOT}/README.md`,'# relay');
  const fetcher=async(target,options)=>{
    const u=new URL(target),prefix=`/repos/${REPO}/contents/`;
    if(u.pathname===`/repos/${REPO}`)return Response.json({private:true});
    if(!u.pathname.startsWith(prefix))return Response.json({error:'unknown'}, {status:404});
    const file=decodeURIComponent(u.pathname.slice(prefix.length));
    if(options.method==='PUT'){
      const body=JSON.parse(options.body);
      assert.equal(body.branch,BRANCH);
      const content=Buffer.from(body.content,'base64').toString('utf8');
      sent.push({file,content});
      files.set(file,content);
      return Response.json({content:{path:file}}, {status:201});
    }
    if(file===`${RELAY_ROOT}/requests`){
      const rows=[...files.keys()].filter(x=>x.startsWith(`${file}/`)).map(x=>({name:x.slice(file.length+1)}));
      return Response.json(rows);
    }
    if(!files.has(file))return Response.json({message:'Not Found'},{status:404});
    return Response.json({content:Buffer.from(files.get(file)).toString('base64'),sha:'test'});
  };
  return {files,sent,fetcher};
}
function sampleRequest(desktop,agentKey,replyKey,op){
  const id=require('node:crypto').randomUUID(),now=Date.now();
  const request={v:1,id,issuedAt:new Date(now).toISOString(),expiresAt:new Date(now+600000).toISOString(),
    replyPublicKey:replyKey.publicKey,payload:seal(op,desktop.publicKey,id)};
  request.signature=signRequest(request,agentKey.privateKey);
  return request;
}

test('private GitHub relay only executes signed paired requests, encrypts replies and never stores raw API body',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'relay-test-'));
  const mock=repoMock();
  try{
    const vault=new Vault(path.join(dir,'settings.json'),storage);vault.load();vault.update({secrets:{githubToken:'GH_SECRET',zernioKey:'ZN_SECRET'}});
    const desktop=vault.identity(),signing=newSigningIdentity(),replyKey=newIdentity();
    const calls=[];
    const relay=new Relay({vault,statePath:path.join(dir,'state.json'),fetcher:mock.fetcher,intervalMs:999999,
      service:async op=>{calls.push(op);return {ok:true,body:{name:'campaign',secret:'ZN_SECRET'}};},approve:async()=>true});
    await relay.connect();
    assert.ok(mock.files.get(`${RELAY_ROOT}/desktop-identity.json`).includes(desktop.fingerprint));
    const read=sampleRequest(desktop,signing,replyKey,{kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts'});
    mock.files.set(`${RELAY_ROOT}/requests/${read.id}.json`,JSON.stringify(read));
    await relay.poll();assert.equal(calls.length,0,'no operations before pairing');
    vault.pairAgent(signing.publicKey);
    await relay.poll();assert.equal(calls.length,1);
    const row=mock.files.get(`${RELAY_ROOT}/responses/${read.id}.json`);
    assert.ok(!row.includes('campaign'));assert.ok(!row.includes('ZN_SECRET'));
    const signed=JSON.parse(row);
    assert.equal(verifyReply(signed,desktop.signingPublicKey),true);
    assert.equal(verifyReply({...signed,createdAt:'1970-01-01'},desktop.signingPublicKey),false);
    const reply=unseal(signed.payload,replyKey.privateKey,read.id);
    assert.equal(reply.body.name,'campaign');assert.equal(reply.body.secret,'[redacted]');
    await relay.poll();assert.equal(calls.length,1,'exactly once for same request');
    const invalid=sampleRequest(desktop,newSigningIdentity(),replyKey,{kind:'api',provider:'zernio',method:'GET',path:'/v1/posts'});
    mock.files.set(`${RELAY_ROOT}/requests/${invalid.id}.json`,JSON.stringify(invalid));
    await relay.poll();assert.equal(calls.length,1,'wrong signature rejected');
    assert.ok(relay.state.rejected[invalid.id]);
    assert.ok(mock.sent.every(x=>!x.content.includes('GH_SECRET')&&!x.content.includes('ZN_SECRET')));
    relay.disconnect();
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('mutating operations need desktop approval; uncertain prior attempts are not replayed',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'relay-test-'));
  const mock=repoMock();
  try{
    const vault=new Vault(path.join(dir,'settings.json'),storage);vault.load();vault.update({secrets:{githubToken:'GH_SECRET'}});
    const desktop=vault.identity(),signing=newSigningIdentity(),replyKey=newIdentity();vault.pairAgent(signing.publicKey);
    let executed=0,prompts=0;
    const relay=new Relay({vault,statePath:path.join(dir,'state.json'),fetcher:mock.fetcher,intervalMs:999999,
      service:async()=>{executed++;return {ok:true};},approve:async()=>{prompts++;return false;}});
    await relay.connect();
    const op={kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',body:{publishNow:true}};
    const request=sampleRequest(desktop,signing,replyKey,op);
    mock.files.set(`${RELAY_ROOT}/requests/${request.id}.json`,JSON.stringify(request));
    await relay.poll();assert.equal(prompts,1);assert.equal(executed,0);
    const response=JSON.parse(mock.files.get(`${RELAY_ROOT}/responses/${request.id}.json`));
    assert.equal(unseal(response.payload,replyKey.privateKey,request.id).error,'DECLINED_BY_OWNER');
    const uncertain=sampleRequest(desktop,signing,replyKey,op);
    mock.files.set(`${RELAY_ROOT}/requests/${uncertain.id}.json`,JSON.stringify(uncertain));
    relay.state.inProgress[uncertain.id]={startedAt:new Date().toISOString(),operation:'write'};
    relay.saveState();
    await relay.poll();assert.equal(executed,0);assert.equal(prompts,1);
    const stopped=JSON.parse(mock.files.get(`${RELAY_ROOT}/responses/${uncertain.id}.json`));
    assert.match(unseal(stopped.payload,replyKey.privateKey,uncertain.id).error,/UNCERTAIN_PREVIOUS_ATTEMPT/);
    relay.disconnect();
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('engagement and outbound messaging always request native owner approval even with autoApproveWrites',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'relay-approval-'));
  const mock=repoMock();
  try{
    const vault=new Vault(path.join(dir,'settings.json'),storage);vault.load();
    vault.update({secrets:{githubToken:'GH_SECRET'},autoApproveReads:true,autoApproveWrites:true});
    const desktop=vault.identity(),signing=newSigningIdentity(),reply=newIdentity();vault.pairAgent(signing.publicKey);
    const approved=[],executed=[];
    const relay=new Relay({vault,statePath:path.join(dir,'state.json'),fetcher:mock.fetcher,
      intervalMs:999999,service:async op=>{executed.push(op);return {ok:true};},
      approve:async op=>{approved.push(op);return false;}});
    await relay.connect();
    const cases=[
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',body:{name:'example'}},
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/inbox/conversations',body:{}},
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/messages/send',body:{}},
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/posts/123/comments',body:{}},
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/broadcasts',body:{}},
      {kind:'api',provider:'zernio',method:'POST',path:'/v1/sequences',body:{}}
    ];
    for(const op of cases){
      const request=sampleRequest(desktop,signing,reply,op);
      mock.files.set(`${RELAY_ROOT}/requests/${request.id}.json`,JSON.stringify(request));
      await relay.poll();
      const response=JSON.parse(mock.files.get(`${RELAY_ROOT}/responses/${request.id}.json`));
      assert.equal(unseal(response.payload,reply.privateKey,request.id).error,'DECLINED_BY_OWNER');
    }
    assert.equal(approved.length,cases.length);
    assert.equal(executed.length,0);
    relay.disconnect();
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('shared API/MCP connector calls use encrypted relay, deny unshared access, and require independent approval despite autoApproveWrites',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'relay-gateway-test-'));
  const mock=repoMock();let relay;
  try{
    const vault=new Vault(path.join(dir,'settings.json'),storage);vault.load();
    vault.update({secrets:{githubToken:'RELAY_GH_SECRET_123456'},autoApproveReads:true,autoApproveWrites:true});
    const api={id:'vendor-api',name:'Vendor',kind:'api',url:'https://api.vendor.com/v1',
      auth:'bearer',credential:'OWNER_API_SECRET_123456',agentAccess:'none'};
    const mcp={id:'vendor-mcp',name:'Remote',kind:'mcp',url:'https://mcp.vendor.com/rpc',
      auth:'none',agentAccess:'approved-writes'};
    vault.saveConnector(api);vault.saveConnector(mcp);
    const desktop=vault.identity(),signing=newSigningIdentity(),replyKey=newIdentity();
    vault.pairAgent(signing.publicKey);
    let allowed=false,onPrompt=()=>{};const approved=[],executed=[];
    relay=new Relay({vault,statePath:path.join(dir,'state.json'),fetcher:mock.fetcher,intervalMs:999999,
      approve:async op=>{approved.push(op);onPrompt(op);return allowed;},
      service:async op=>{
        if(op.kind==='gateway-list')return {ok:true,connectors:vault.listConnectors()
          .filter(row=>row.agentAccess!=='none')};
        executed.push(op);return {ok:true,body:{token:'OWNER_API_SECRET_123456',
          echo:'Bearer OWNER_API_SECRET_123456'}};
      }});
    await relay.connect();
    async function send(op){
      const request=sampleRequest(desktop,signing,replyKey,op);
      mock.files.set(`${RELAY_ROOT}/requests/${request.id}.json`,JSON.stringify(request));
      await relay.poll();
      const signed=JSON.parse(mock.files.get(`${RELAY_ROOT}/responses/${request.id}.json`));
      assert.equal(verifyReply(signed,desktop.signingPublicKey),true);
      return unseal(signed.payload,replyKey.privateKey,request.id);
    }
    const read={kind:'gateway-api',connectorId:'vendor-api',method:'GET',path:'/games'};
    const write={...read,method:'POST',body:{topic:'gaming'}};
    assert.equal((await send(read)).error,'CONNECTOR_NOT_SHARED_WITH_AGENT');
    assert.equal(executed.length,0);assert.equal(approved.length,0);
    vault.saveConnector({...api,credential:'',agentAccess:'read'});
    assert.equal((await send({kind:'gateway-list'})).connectors.length,2);
    const result=await send(read);assert.equal(result.ok,true);
    assert.equal(result.body.token,'[redacted]');
    assert.equal(result.body.echo,'Bearer [redacted]');
    assert.equal(executed.length,1);assert.equal(approved.length,0);
    assert.equal((await send(write)).error,'CONNECTOR_NOT_SHARED_WITH_AGENT');
    vault.saveConnector({...api,credential:'',agentAccess:'approved-writes'});
    assert.equal((await send(write)).error,'DECLINED_BY_OWNER');
    assert.equal(approved.length,1);assert.equal(executed.length,1);
    assert.equal(approved[0].connectorId,'vendor-api');
    allowed=true;assert.equal((await send(write)).ok,true);
    assert.equal(approved.length,2);assert.equal(executed.length,2);
    assert.equal((await send({kind:'gateway-mcp',connectorId:'vendor-mcp',action:'tools'})).ok,true);
    assert.equal(approved.length,2,'listing MCP tools can be read with registered read access');
    assert.equal((await send({kind:'gateway-mcp',connectorId:'vendor-mcp',action:'call',
      tool:'lookup',args:{q:'game'}})).ok,true);
    assert.equal(approved.length,3,'every tools/call needs its own owner approval');
    const count=executed.length;
    onPrompt=()=>vault.saveConnector({...api,url:'https://other.vendor.com/v1',
      credential:'',agentAccess:'approved-writes'});
    assert.equal((await send(write)).error,'CONNECTOR_CHANGED_DURING_APPROVAL');
    assert.equal(executed.length,count,'a changed destination after approval cannot receive the request');
    assert.ok(mock.sent.every(item=>!item.content.includes('OWNER_API_SECRET_123456')&&
      !item.content.includes('RELAY_GH_SECRET_123456')));
  }finally{relay?.disconnect();fs.rmSync(dir,{recursive:true,force:true});}
});

'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {createHash}=require('node:crypto');
const {Vault}=require('../src/vault.cjs');
const {Kling,oauthCallback}=require('../src/kling.cjs');
const {KLING_ENDPOINT}=require('../src/constants.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:s=>Buffer.from('ENC:'+Buffer.from(s).toString('base64')),
  decryptString:b=>Buffer.from(b.toString('utf8').slice(4),'base64').toString('utf8')};
const json=(body,opts={})=>Response.json(body,opts);
const resource='https://kling.ai/.well-known/oauth-protected-resource/mcp';
const auth='https://auth.example.test';
const challenge=s=>createHash('sha256').update(s).digest('base64url');

function fakeRemote(){
  let authChallenge, registration=0, identityCalls=0, generated=0;
  const fetcher=async(input,init={})=>{
    const url=new URL(input);const method=init.method||'GET';
    if(url.href===resource)return json({resource:KLING_ENDPOINT,authorization_servers:[auth],
      scopes_supported:['mcp:read','mcp:write']});
    if(url.href===`${auth}/.well-known/oauth-authorization-server`)return json({
      issuer:auth,authorization_endpoint:`${auth}/authorize`,token_endpoint:`${auth}/token`,
      registration_endpoint:`${auth}/register`,
      response_types_supported:['code'],grant_types_supported:['authorization_code','refresh_token'],
      code_challenge_methods_supported:['S256']
    });
    if(url.href===`${auth}/register`&&method==='POST'){
      registration++;
      const body=JSON.parse(init.body);assert.ok(body.redirect_uris[0].startsWith('http://127.0.0.1:'));
      return json({...body,client_id:'client-test',token_endpoint_auth_method:'none'});
    }
    if(url.href===`${auth}/token`&&method==='POST'){
      const params=new URLSearchParams(init.body);
      assert.equal(params.get('client_id'),'client-test');
      if(params.get('grant_type')==='authorization_code'){
        assert.equal(params.get('code'),'only-a-test-code');
        assert.equal(challenge(params.get('code_verifier')),authChallenge);
      }
      return json({access_token:'TEST_ACCESS_ONLY',refresh_token:'TEST_REFRESH_ONLY',
        token_type:'Bearer',expires_in:3600});
    }
    if(url.href===KLING_ENDPOINT){
      const headers=new Headers(init.headers);
      if(headers.get('authorization')!=='Bearer TEST_ACCESS_ONLY')return new Response('Unauthorized',{
        status:401,headers:{'WWW-Authenticate':`Bearer resource_metadata="${resource}"`}});
      if(method==='GET')return new Response(null,{status:405});
      const request=JSON.parse(init.body);
      if(!('id'in request))return new Response(null,{status:202});
      let result;
      if(request.method==='initialize')result={protocolVersion:'2025-03-26',
        capabilities:{tools:{listChanged:false}},serverInfo:{name:'test-kling',version:'1.0.0'}};
      else if(request.method==='tools/list')result={tools:[
        {name:'who_am_i',inputSchema:{type:'object',properties:{}}},
        {name:'text_to_video',inputSchema:{type:'object',properties:{prompt:{type:'string'}}}}
      ]};
      else if(request.method==='tools/call'){
        if(request.params.name==='who_am_i')identityCalls++;
        if(request.params.name==='text_to_video')generated++;
        result={content:[{type:'text',text:request.params.name==='who_am_i'?
          '{"account":"test-only","models":["model-from-server"]}':'{"generation_id":"mock-id"}'}]};
      } else throw new Error(`Unexpected MCP method: ${request.method}`);
      return json({jsonrpc:'2.0',id:request.id,result},{headers:{'Content-Type':'application/json'}});
    }
    throw new Error(`Unexpected OAuth discovery request: ${method} ${url.origin}${url.pathname}`);
  };
  return {fetcher,authorizeUrl:url=>{authChallenge=url.searchParams.get('code_challenge');},
    stats:()=>({registration,identityCalls,generated})};
}

test('official MCP transport performs OAuth/PKCE in browser and verifies who_am_i before tools/call',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-mcp-test-'));
  const probe=http.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));
  const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));
  const remote=fakeRemote(),vault=new Vault(path.join(dir,'vault.json'),storage);vault.load();
  const opened=[];
  const kling=new Kling({vault,port,fetcher:remote.fetcher,openExternal:async raw=>{
    const authUrl=new URL(raw);opened.push(authUrl.origin+authUrl.pathname);
    assert.equal(authUrl.origin,auth);
    assert.equal(authUrl.searchParams.get('code_challenge_method'),'S256');
    remote.authorizeUrl(authUrl);
    const callback=new URL(authUrl.searchParams.get('redirect_uri'));
    callback.searchParams.set('state',authUrl.searchParams.get('state'));
    callback.searchParams.set('code','only-a-test-code');
    const response=await fetch(callback);
    assert.equal(response.status,200);
  }});
  try{
    const result=await kling.authorize({timeoutMs:5000});
    assert.equal(result.ok,true);assert.equal(result.endpoint,KLING_ENDPOINT);
    assert.match(JSON.stringify(result.identity),/test-only/);
    assert.deepEqual(opened,[`${auth}/authorize`]);
    assert.equal(remote.stats().identityCalls,1);
    assert.equal(remote.stats().registration,1);
    assert.ok(!fs.readFileSync(vault.file,'utf8').includes('TEST_ACCESS_ONLY'));
    assert.equal(vault.publicSettings().klingAuthorized,true);
    const tools=await kling.listTools();assert.equal(tools.tools[1].name,'text_to_video');
    const mockOutput=await kling.execute({kind:'kling',command:'text_to_video',args:{prompt:'mock only'}});
    assert.equal(mockOutput.ok,true);assert.equal(remote.stats().generated,1);
    await kling.close();
    assert.equal((await kling.execute({kind:'kling',command:'who_am_i',args:{}})).ok,true);
    assert.equal(opened.length,1,'stored OAuth reconnect does not open login again');
    await kling.logout();assert.equal(vault.publicSettings().klingAuthorized,false);
  }finally{await kling.close();fs.rmSync(dir,{recursive:true,force:true});}
});

test('Kling callback remains reachable until login opens, verifies state/issuer and can be cancelled',async()=>{
  const provider={expectedState:'fresh-state',discoveryState:()=>({authorizationServerMetadata:{issuer:auth}})};
  const listener=oauthCallback({provider,port:0,timeoutMs:500,deferTimeout:true});
  try{
    await new Promise((resolve,reject)=>listener.server.once('error',reject).listen(0,'127.0.0.1',resolve));
    const base=`http://127.0.0.1:${listener.server.address().port}`;
    await new Promise(resolve=>setTimeout(resolve,550)); // discovery took longer than timeout
    assert.equal((await fetch(`${base}/kling-callback?state=wrong&code=test`)).status,400);
    assert.equal((await fetch(`${base}/kling-callback?state=fresh-state&iss=https%3A%2F%2Fevil.example&code=test`)).status,400);
    listener.startTimer();
    const result=await fetch(`${base}/kling-callback?state=fresh-state&iss=${encodeURIComponent(auth)}&code=mock-code`);
    assert.equal(result.status,200);
    assert.equal(await listener.code,'mock-code');
    assert.equal((await fetch(`${base}/kling-callback?state=fresh-state&code=mock-code`)).status,410);
  }finally{listener.close();}
  const pending=oauthCallback({provider,port:0,timeoutMs:5000});
  await new Promise((resolve,reject)=>pending.server.once('error',reject).listen(0,'127.0.0.1',resolve));
  const port=pending.server.address().port;
  const closed=new Promise(resolve=>pending.server.once('close',resolve));
  pending.close(new Error('Kling login cancelled'));
  await assert.rejects(pending.code,/cancelled/);
  await closed;
  const probe=http.createServer();
  await new Promise((resolve,reject)=>probe.once('error',reject).listen(port,'127.0.0.1',resolve));
  await new Promise(resolve=>probe.close(resolve));
});

test('closing the studio aborts a pending Kling callback without exposing the code',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-mcp-cancel-'));
  const vault=new Vault(path.join(dir,'vault.json'),storage);vault.load();
  const probe=http.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));
  const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));
  const remote=fakeRemote();
  let opened;
  const browserOpened=new Promise(resolve=>opened=resolve);
  const kling=new Kling({vault,port,fetcher:remote.fetcher,openExternal:async raw=>{
    remote.authorizeUrl(new URL(raw));opened();
  }});
  try{
    const login=kling.authorize({timeoutMs:5000});
    await browserOpened;
    const closed=new Promise(resolve=>kling.activeCallback.server.once('close',resolve));
    await kling.close();
    await assert.rejects(login,/interrupted|cancelled/);
    await closed;
    const next=http.createServer();
    await new Promise((resolve,reject)=>next.once('error',reject).listen(port,'127.0.0.1',resolve));
    await new Promise(resolve=>next.close(resolve));
  }finally{await kling.close();fs.rmSync(dir,{recursive:true,force:true});}
});

'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {createHash}=require('node:crypto');
const {GatewayMCP,ConnectorOAuthProvider}=require('../src/gateway-mcp.cjs');
const {PublicTransport}=require('../src/gateway-network.cjs');
const {Vault}=require('../src/vault.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:s=>Buffer.from(`ENCRYPTED:${Buffer.from(s).toString('base64')}`),
  decryptString:b=>Buffer.from(b.toString('utf8').slice(10),'base64').toString('utf8')};
const endpoint='https://mcp.vendor.com/rpc';
const resource='https://mcp.vendor.com/.well-known/oauth-protected-resource/rpc';
const auth='https://auth.vendor.com';
function mockRemote(){
  let challenge,registration=0,calls=0,tools=0;
  const requests=[];
  const fetcher=async(input,init={})=>{
    const url=new URL(input),method=init.method||'GET';
    requests.push({url:url.href,method,redirect:init.redirect,auth:new Headers(init.headers).get('authorization')});
    assert.equal(init.redirect,'manual','MCP and OAuth endpoints must not follow redirects');
    assert.ok(init.dispatcher,'safe transport must pin DNS on the socket');
    if(url.href===resource)return Response.json({resource:endpoint,authorization_servers:[auth]});
    if(url.href===`${auth}/.well-known/oauth-authorization-server`)
      return Response.json({issuer:auth,authorization_endpoint:`${auth}/authorize`,
        token_endpoint:`${auth}/token`,registration_endpoint:`${auth}/register`,
        response_types_supported:['code'],grant_types_supported:['authorization_code','refresh_token'],
        code_challenge_methods_supported:['S256']});
    if(url.href===`${auth}/register`&&method==='POST'){
      registration++;const body=JSON.parse(init.body);
      assert.match(body.redirect_uris[0],/^http:\/\/127\.0\.0\.1:\d+\/gateway-callback$/);
      return Response.json({...body,client_id:'public-mcp-client',token_endpoint_auth_method:'none'});
    }
    if(url.href===`${auth}/token`&&method==='POST'){
      const params=new URLSearchParams(init.body);
      assert.equal(params.get('client_id'),'public-mcp-client');
      if(params.get('grant_type')==='authorization_code'){
        assert.equal(params.get('code'),'one-time-code-only');
        assert.equal(createHash('sha256').update(params.get('code_verifier')).digest('base64url'),challenge);
      }
      return Response.json({access_token:'SAVED_ACCESS_123456',refresh_token:'SAVED_REFRESH_123456',
        token_type:'Bearer',expires_in:3600});
    }
    if(url.href===endpoint){
      if(new Headers(init.headers).get('authorization')!=='Bearer SAVED_ACCESS_123456')
        return new Response('Unauthorized',{status:401,headers:{
          'WWW-Authenticate':`Bearer resource_metadata="${resource}"`}});
      if(method==='GET')return new Response(null,{status:405});
      const request=JSON.parse(init.body);
      if(!('id'in request))return new Response(null,{status:202});
      let result;
      if(request.method==='initialize')result={protocolVersion:'2025-03-26',
        capabilities:{tools:{listChanged:false},resources:{},prompts:{}},
        serverInfo:{name:'mock-mcp',version:'1.0'}};
      else if(request.method==='tools/list'){
        tools++;result={tools:[
          {name:'lookup',inputSchema:{type:'object',properties:{q:{type:'string'}}}},
          {name:'send_message',inputSchema:{type:'object',properties:{to:{type:'string'}}}}]};
      }else if(request.method==='tools/call'){
        calls++;result={content:[{type:'text',text:`called:${request.params.name}`}]};
      }else if(request.method==='resources/list')result={resources:[{name:'Guide',uri:'guide://latest'}]};
      else if(request.method==='resources/read')result={contents:[{uri:request.params.uri,
        text:'A remote, untrusted resource'}]};
      else if(request.method==='prompts/list')result={prompts:[{name:'brief'}]};
      else if(request.method==='prompts/get')result={description:'Mock brief',messages:[
        {role:'user',content:{type:'text',text:'Brief a campaign'}}]};
      else throw new Error(`Unknown MCP call: ${request.method}`);
      return Response.json({jsonrpc:'2.0',id:request.id,result},
        {headers:{'Content-Type':'application/json'}});
    }
    throw new Error(`Unexpected MCP URL ${method} ${url.origin}${url.pathname}`);
  };
  return {fetcher,authorize:url=>{challenge=url.searchParams.get('code_challenge');},
    stats:()=>({registration,calls,tools,requests})};
}
async function freePort(){
  const probe=http.createServer();await new Promise(resolve=>probe.listen(0,'127.0.0.1',resolve));
  const port=probe.address().port;await new Promise(resolve=>probe.close(resolve));return port;
}

test('remote MCP OAuth/PKCE uses only owner browser + localhost callback, stores encrypted tokens, never opens browser for agent reads',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-gateway-mcp-'));
  const vault=new Vault(path.join(dir,'vault.json'),storage);vault.load();
  vault.saveConnector({id:'mcp-oauth',name:'Remote',kind:'mcp',url:endpoint,auth:'oauth',agentAccess:'read'});
  const remote=mockRemote(),network=new PublicTransport({fetcher:remote.fetcher});
  const port=await freePort(),opened=[];
  const mcp=new GatewayMCP({vault,network,port,openExternal:async raw=>{
    const url=new URL(raw);opened.push(`${url.origin}${url.pathname}`);
    assert.equal(url.origin,auth);
    assert.equal(url.searchParams.get('code_challenge_method'),'S256');
    remote.authorize(url);
    const callback=new URL(url.searchParams.get('redirect_uri'));
    callback.searchParams.set('state','wrong-csrf-state');callback.searchParams.set('code','one-time-code-only');
    assert.equal((await fetch(callback)).status,400);
    callback.searchParams.set('state',url.searchParams.get('state'));
    assert.equal((await fetch(callback)).status,200);
  }});
  try{
    await assert.rejects(()=>mcp.execute({connectorId:'mcp-oauth',action:'tools'}),/Authorize/);
    assert.equal(opened.length,0,'remote requests must not initiate a browser OAuth flow');
    const login=await mcp.authorize('mcp-oauth',{timeoutMs:5000});
    assert.equal(login.ok,true);assert.deepEqual(login.tools,['lookup','send_message']);
    assert.deepEqual(opened,[`${auth}/authorize`]);
    assert.equal(remote.stats().registration,1);
    assert.ok(remote.stats().requests.every(row=>row.redirect==='manual'));
    assert.ok(remote.stats().requests.filter(row=>row.url.startsWith(auth))
      .every(row=>!row.auth),'access token must not be forwarded to OAuth discovery');
    assert.ok(!JSON.stringify(login).includes('SAVED_ACCESS_123456'));
    assert.ok(!fs.readFileSync(vault.file,'utf8').includes('SAVED_ACCESS_123456'));
    assert.equal(vault.listConnectors()[0].authorized,true);
    assert.equal((await mcp.execute({connectorId:'mcp-oauth',action:'resources'})).body.resources[0].uri,'guide://latest');
    assert.match(JSON.stringify((await mcp.execute({connectorId:'mcp-oauth',action:'read-resource',uri:'guide://latest'})).body),/untrusted resource/);
    assert.equal((await mcp.execute({connectorId:'mcp-oauth',action:'prompts'})).body.prompts[0].name,'brief');
    assert.ok((await mcp.execute({connectorId:'mcp-oauth',action:'get-prompt',name:'brief',args:{}})).ok);
    await assert.rejects(()=>mcp.execute({connectorId:'mcp-oauth',action:'call',tool:'unlisted',args:{}}),/not in the live/);
    assert.equal(remote.stats().calls,0);
    assert.equal((await mcp.execute({connectorId:'mcp-oauth',action:'call',tool:'lookup',args:{q:'game'}})).ok,true);
    assert.equal(remote.stats().calls,1);
    await mcp.close();
    assert.equal((await mcp.execute({connectorId:'mcp-oauth',action:'tools'})).ok,true);
    assert.equal(opened.length,1,'stored OAuth token reconnects silently without a browser');
    await mcp.close();
    vault.saveConnectorOAuth('mcp-oauth','tokens',{access_token:'EXPIRED_ONLY',token_type:'Bearer'});
    await assert.rejects(()=>mcp.execute({connectorId:'mcp-oauth',action:'tools'}));
    assert.equal(opened.length,1,'an expired agent-side token may never open the owner’s browser');
  }finally{await mcp.close();await network.close();fs.rmSync(dir,{recursive:true,force:true});}
});

test('OAuth authorization redirect never opens internal URLs or URLs containing an access code',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-gateway-redirect-'));
  const vault=new Vault(path.join(dir,'vault.json'),storage);vault.load();
  vault.saveConnector({id:'mcp-oauth',name:'Remote',kind:'mcp',url:endpoint,auth:'oauth',agentAccess:'none'});
  const opened=[];
  const provider=new ConnectorOAuthProvider({vault,id:'mcp-oauth',redirectUrl:'http://127.0.0.1:10000/gateway-callback',
    openExternal:async url=>opened.push(url)});
  try{
    await assert.rejects(()=>provider.redirectToAuthorization(new URL('http://127.0.0.1:7777/login?state=abc')));
    await assert.rejects(()=>provider.redirectToAuthorization(new URL('https://auth.vendor.com/authorize?code=secret')));
    await assert.rejects(()=>provider.redirectToAuthorization(new URL('https://auth.vendor.com/authorize?access_token=secret')));
    assert.equal(opened.length,0);
    await provider.redirectToAuthorization(new URL('https://auth.vendor.com/authorize?code_challenge=pkce&state=test'));
    assert.equal(opened.length,1);
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('a registered remote MCP bearer token stays on its own origin; no browser is opened',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-gateway-bearer-'));
  const vault=new Vault(path.join(dir,'vault.json'),storage);vault.load();
  vault.saveConnector({id:'mcp-bearer',name:'Remote bearer',kind:'mcp',url:endpoint,
    auth:'bearer',credential:'OWNER_MCP_SECRET_123456',agentAccess:'read'});
  let opened=0,requests=0;
  const network=new PublicTransport({fetcher:async(raw,init={})=>{
    requests++;
    assert.equal(raw,endpoint);
    assert.equal(new Headers(init.headers).get('authorization'),'Bearer OWNER_MCP_SECRET_123456');
    if(init.method==='GET')return new Response(null,{status:405});
    const body=JSON.parse(init.body);
    if(!('id'in body))return new Response(null,{status:202});
    const result=body.method==='initialize'?{protocolVersion:'2025-03-26',
      capabilities:{tools:{}},serverInfo:{name:'mock-bearer',version:'1.0'}}:
      body.method==='tools/list'?{tools:[{name:'lookup',inputSchema:{type:'object'}}]}:null;
    if(!result)throw new Error('Unexpected MCP action');
    return Response.json({jsonrpc:'2.0',id:body.id,result},
      {headers:{'Content-Type':'application/json'}});
  }});
  const mcp=new GatewayMCP({vault,network,openExternal:async()=>opened++});
  try{
    const result=await mcp.execute({connectorId:'mcp-bearer',action:'tools'});
    assert.equal(result.ok,true);
    assert.equal(result.body.tools[0].name,'lookup');
    assert.equal(opened,0);assert.ok(requests>=2);
    assert.ok(!JSON.stringify(result).includes('OWNER_MCP_SECRET_123456'));
    assert.ok(!fs.readFileSync(vault.file,'utf8').includes('OWNER_MCP_SECRET_123456'));
  }finally{await mcp.close();await network.close();fs.rmSync(dir,{recursive:true,force:true});}
});

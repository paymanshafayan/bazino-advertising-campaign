'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {Vault}=require('../src/vault.cjs');
const {Gateway}=require('../src/gateway.cjs');
const {publicUrl,publicAddress,createPublicLookup,PublicTransport}=require('../src/gateway-network.cjs');
const {validateOperation,isMutating,redact}=require('../src/protocol.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:text=>Buffer.from(`ENCRYPTED:${Buffer.from(text).toString('base64')}`),
  decryptString:bytes=>Buffer.from(bytes.toString('utf8').slice(10),'base64').toString('utf8')};
function withVault(fn){
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-gateway-test-'));
  const vault=new Vault(path.join(dir,'private-vault.json'),storage);vault.load();
  return Promise.resolve().then(()=>fn(vault)).finally(()=>fs.rmSync(dir,{recursive:true,force:true}));
}
const api=(overrides={})=>({id:'vendor-api',name:'Vendor',kind:'api',url:'https://api.vendor.com/v1',
  auth:'header',headerName:'X-API-Key',credential:'ONLY_OWNER_KEY_123456',agentAccess:'read',...overrides});
const mcp=(overrides={})=>({id:'remote-mcp',name:'Remote tools',kind:'mcp',url:'https://mcp.vendor.com/rpc',
  auth:'none',agentAccess:'none',...overrides});

test('arbitrary links must be public HTTPS, never internal, loopback, credential-bearing or opaque redirects',async()=>{
  for(const address of ['http://news.vendor.com','https://localhost','https://a.localhost',
    'https://router.local','https://metadata.internal','https://a.test','https://[::1]/',
    'https://127.0.0.1/','https://0x7f000001/','https://[::ffff:7f00:1]/',
    'https://me:password@news.vendor.com','https://news.vendor.com:8443/story',
    'https://news.vendor.com/#fragment','https://news.vendor.com/?apiKey=secret',
    'https://news.vendor.com/?key=secret','https://news.vendor.com/?access_token=secret',
    'https://news.vendor.com/?session=secret','https://news.vendor.com/?code=secret'])
    assert.throws(()=>publicUrl(address),undefined,address);
  assert.equal(publicUrl('https://news.vendor.com/article?topic=gaming').hostname,'news.vendor.com');
  assert.throws(()=>publicUrl('https://api.vendor.com/rpc?path=1',{allowQuery:false}));
  assert.equal(publicUrl('https://auth.vendor.com/authorize?code_challenge=pkce&state=abc',
    {allowSensitiveQuery:true}).protocol,'https:');
  for(const ip of ['127.0.0.1','10.0.0.1','169.254.169.254','100.64.0.1',
    '192.0.0.9','203.0.113.5','::1','::ffff:10.0.0.1',
    '64:ff9b::a00:1','64:ff9b:1::a00:1','::a00:1'])
    assert.equal(publicAddress(ip),false,ip);
  assert.equal(publicAddress('8.8.8.8'),true);
  assert.equal(publicAddress('2606:4700:4700::1111'),true);
  const seen=[];
  const transport=new PublicTransport({fetcher:async(url,options)=>{
    seen.push({url,options});return new Response(null,{status:302,
      headers:{Location:'http://169.254.169.254/latest/meta-data'}});
  }});
  try{
    await assert.rejects(()=>transport.fetch('https://news.vendor.com/redirect',{
      redirect:'follow',headers:{Authorization:'Bearer OWNER'}}),/Redirect refused/);
    assert.equal(seen.length,1);
    assert.equal(seen[0].options.redirect,'manual');
    assert.ok(seen[0].options.dispatcher,'socket-pinned Undici dispatcher must be used');
  }finally{await transport.close();}
});

test('socket-time DNS validation prevents private results, mixed A/AAAA and rebinding',async()=>{
  let n=0;
  const lookup=createPublicLookup((host,opts,cb)=>{
    assert.equal(host,'news.vendor.com');assert.equal(opts.all,true);
    n++;
    cb(null,n===1?[{address:'8.8.8.8',family:4}]:[{address:'10.0.0.1',family:4}]);
  });
  const query=()=>new Promise(resolve=>lookup('news.vendor.com',{},(error,address)=>resolve({error,address})));
  assert.equal((await query()).address,'8.8.8.8');
  assert.match((await query()).error.message,/private, reserved/);
  assert.equal(n,2,'DNS must be checked for every connection, not at registration alone');
  const mixed=createPublicLookup((_host,_opts,cb)=>cb(null,[{address:'8.8.8.8',family:4},
    {address:'169.254.169.254',family:4}]));
  const error=await new Promise(resolve=>mixed('news.vendor.com',{},e=>resolve(e)));
  assert.match(error.message,/private, reserved/);
  const bad=createPublicLookup((_host,_opts,cb)=>cb(null,[{address:'64:ff9b:1::a00:1',family:6}]));
  assert.match((await new Promise(resolve=>bad('news.vendor.com',{},e=>resolve(e)))).message,/private, reserved/);
  let socketLookups=0;
  const transport=new PublicTransport({timeoutMs:1500,resolve:(_host,_opts,cb)=>{
    socketLookups++;cb(null,[{address:'127.0.0.1',family:4}]);
  }});
  try{
    await assert.rejects(()=>transport.fetch('https://news.vendor.com/'),e=>
      e.cause?.message.includes('DNS resolution includes a private'));
    assert.equal(socketLookups,1,'the real Undici socket path must call guarded lookup');
  }finally{await transport.close();}
});

test('encrypted registry exposes metadata but not credentials, and rotates secrets on origin/auth changes',()=>withVault(vault=>{
  const info=vault.saveConnector(api());
  assert.equal(info.hasCredential,true);
  assert.equal(info.credential,undefined);
  assert.equal(vault.listConnectors()[0].configured,true);
  assert.equal(vault.publicSettings().connectors,undefined);
  assert.ok(!fs.readFileSync(vault.file,'utf8').includes('ONLY_OWNER_KEY_123456'));
  assert.equal(vault.allSecrets().includes('ONLY_OWNER_KEY_123456'),true);
  const again=new Vault(vault.file,storage);again.load();
  assert.equal(again.connector('vendor-api').credential,'ONLY_OWNER_KEY_123456');
  // Empty credential keeps the previous value only when the destination and auth are unchanged.
  again.saveConnector(api({credential:''}));
  assert.equal(again.connector('vendor-api').credential,'ONLY_OWNER_KEY_123456');
  again.saveConnector(api({url:'https://api.other-vendor.com/v1',credential:''}));
  assert.equal(again.connector('vendor-api').credential,'');
  assert.equal(again.listConnectors()[0].hasCredential,false);
  again.saveConnector(mcp({auth:'oauth'}));
  again.saveConnectorOAuth('remote-mcp','tokens',{access_token:'PRIVATE_OAUTH_87654321'});
  assert.equal(again.listConnectors()[1].authorized,true);
  assert.ok(!fs.readFileSync(again.file,'utf8').includes('PRIVATE_OAUTH_87654321'));
  assert.equal(redact({text:'PRIVATE_OAUTH_87654321'},0,again.allSecrets()).text,'[redacted]');
  again.saveConnector(mcp({auth:'oauth',url:'https://mcp.other-vendor.com/rpc'}));
  assert.equal(again.getConnectorOAuth('remote-mcp','tokens'),undefined);
  again.removeConnector('remote-mcp');
  assert.equal(again.connector('remote-mcp'),null);
  for(const bad of [api({url:'http://api.vendor.com'}),api({url:'https://127.0.0.1'}),
    api({url:'https://api.vendor.com/v1?key=oops'}),api({url:'https://api.vendor.com/../v1'}),
    mcp({auth:'header'}),api({headerName:'Host'}),api({headerName:'Cookie'}),
    api({url:'https://zernio.com/api'}),api({url:'https://api.cloudflare.com/client/v4'}),
    mcp({url:'https://kling.ai/mcp'})])
    assert.throws(()=>again.saveConnector(bad),undefined,JSON.stringify(bad));
}));

test('relative API request stays under registered prefix and sends key only to registered endpoint',()=>withVault(async vault=>{
  vault.saveConnector(api());
  const calls=[];
  const gateway=new Gateway({vault,fetcher:async(url,options)=>{
    calls.push({url,options});
    if(url.includes('news.vendor.com'))return new Response('<html><head><title>News</title></head><body><nav>Ignore menu</nav><article><h1>Update</h1><p>Documented change &amp; context</p><script>danger()</script></article></body></html>',
      {headers:{'Content-Type':'text/html; charset=utf-8'}});
    return Response.json({name:'Update',token:'ONLY_OWNER_KEY_123456'},
      {headers:{'Content-Type':'application/json'}});
  }});
  try{
    const result=await gateway.request({kind:'gateway-api',connectorId:'vendor-api',method:'GET',path:'/games?limit=5'});
    assert.equal(result.ok,true);
    assert.equal(calls[0].url,'https://api.vendor.com/v1/games?limit=5');
    assert.equal(calls[0].options.headers['X-API-Key'],'ONLY_OWNER_KEY_123456');
    assert.equal(redact(result,0,vault.allSecrets()).body.token,'[redacted]');
    const page=await gateway.readPage({kind:'web-fetch',url:'https://news.vendor.com/updates'});
    assert.equal(page.format,'html');assert.equal(page.title,'News');
    assert.match(page.text,/Documented change & context/);
    assert.doesNotMatch(page.text,/danger|Ignore menu/);
    assert.equal(calls[1].options.headers['X-API-Key'],undefined);
    assert.equal(calls[1].options.headers.Authorization,undefined);
    await assert.rejects(()=>gateway.request({kind:'gateway-api',connectorId:'vendor-api',path:'/../admin'}));
    await assert.rejects(()=>gateway.request({kind:'gateway-api',connectorId:'vendor-api',path:'//other.com/'}));
    await assert.rejects(()=>gateway.request({kind:'gateway-api',connectorId:'vendor-api',path:'/items?apiKey=bad'}));
    assert.equal(calls.length,2,'invalid paths are rejected before remote network access');
  }finally{await gateway.close();}
}));

test('readers reject oversized, redirected, binary and private URLs without following or running scripts',()=>withVault(async vault=>{
  let remote=0;
  const gateway=new Gateway({vault,fetcher:async(url)=>{
    remote++;
    if(url.endsWith('redirect'))return new Response(null,{status:302,
      headers:{Location:'https://127.0.0.1/admin'}});
    if(url.endsWith('pdf'))return new Response(Buffer.from('%PDF test'),
      {headers:{'Content-Type':'application/pdf'}});
    return new Response('x'.repeat(1024*1024+1),{headers:{'Content-Type':'text/plain'}});
  }});
  try{
    await assert.rejects(()=>gateway.readPage({kind:'web-fetch',url:'https://news.vendor.com/redirect'}),/Redirect refused/);
    await assert.rejects(()=>gateway.readPage({kind:'web-fetch',url:'https://news.vendor.com/pdf'}),/Only|does not provide/);
    await assert.rejects(()=>gateway.readPage({kind:'web-fetch',url:'https://news.vendor.com/long'}),/size limit/);
    await assert.rejects(()=>gateway.readPage({kind:'web-fetch',url:'https://169.254.169.254/'}));
    assert.equal(remote,3);
  }finally{await gateway.close();}
}));

test('gateway protocol classifies all MCP calls and non-GET API operations as writes',()=>{
  assert.equal(isMutating(validateOperation({kind:'web-fetch',url:'https://news.vendor.com'})),false);
  assert.equal(isMutating(validateOperation({kind:'gateway-list'})),false);
  assert.equal(isMutating(validateOperation({kind:'gateway-api',connectorId:'vendor-api',method:'GET',path:'/v1/items'})),false);
  assert.equal(isMutating(validateOperation({kind:'gateway-api',connectorId:'vendor-api',method:'DELETE',path:'/v1/items/one'})),true);
  assert.equal(isMutating(validateOperation({kind:'gateway-mcp',connectorId:'remote-mcp',action:'tools'})),false);
  assert.equal(isMutating(validateOperation({kind:'gateway-mcp',connectorId:'remote-mcp',action:'call',
    tool:'get_data',args:{}})),true,'MCP tool names are untrusted, including read-sounding names');
  for(const body of [{accessToken:'DANGER'},{client_secret:'DANGER'}])
    assert.equal(Object.values(redact(body))[0],'[redacted]');
  assert.throws(()=>validateOperation({kind:'gateway-api',connectorId:'vendor-api',path:'https://evil.com'}));
  assert.throws(()=>validateOperation({kind:'gateway-api',connectorId:'vendor-api',path:'/read?key=secrets'}));
  assert.throws(()=>validateOperation({kind:'gateway-mcp',connectorId:'remote-mcp',action:'call',tool:'other',
    args:{a:'x'.repeat(100_000)}}));
});

test('first-time OAuth login is allowed locally without an existing token, but execution waits for it',()=>withVault(async vault=>{
  vault.saveConnector(mcp({auth:'oauth'}));
  let loginCalls=0;
  const gateway=new Gateway({vault,mcpFactory:()=>({
    authorize:async id=>{loginCalls++;return {ok:true,connectorId:id};},
    execute:async()=>({ok:true}),closeConnector:async()=>{},close:async()=>{}})});
  try{
    assert.equal((await gateway.authorizeMcp('remote-mcp')).ok,true);
    assert.equal(loginCalls,1);
    await assert.rejects(()=>gateway.executeMcp({kind:'gateway-mcp',connectorId:'remote-mcp',action:'tools'}),/Authorize/);
  }finally{await gateway.close();}
}));

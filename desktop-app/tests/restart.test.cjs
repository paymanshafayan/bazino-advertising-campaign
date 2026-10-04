'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const http=require('node:http');
const {createService}=require('../src/server.cjs');
const {request,restartExisting,waitReady}=require('../src/restart-existing.cjs');
const {HOST}=require('../src/server-address.cjs');
const token='a'.repeat(43);
const legacyPage=`<!doctype html><title>BAZINO · Marketing Control</title>`+
  `<meta name="bazino-session" content="${token}">`;
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const storage={isEncryptionAvailable:()=>true,
  encryptString:text=>Buffer.from('ENC:'+Buffer.from(text).toString('base64')),
  decryptString:bytes=>Buffer.from(bytes.toString('utf8').slice(4),'base64').toString('utf8')};
async function listen(handler){
  const server=http.createServer(handler);
  await new Promise((resolve,reject)=>server.once('error',reject).listen(0,HOST,resolve));
  return server;
}
async function close(server){
  if(server.listening)await new Promise(resolve=>server.close(resolve));
}

test('preflight stops an actual studio with its session and releases the port',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'bazino-restart-'));
  let stops=0;
  const app=createService({baseDir:dir,port:0,storage,onStop:()=>{stops++;return app.close();},
    klingFactory:()=>({close:async()=>{}}),gatewayFactory:()=>({close:async()=>{}})});
  try{
    await app.listen();
    const port=app.server.address().port;
    const result=await restartExisting({port,waitMs:2500});
    assert.deepEqual(result,{existing:true,restarted:true});
    assert.equal(stops,1);
    assert.equal(app.server.listening,false);
    const next=http.createServer();
    try{
      await new Promise((resolve,reject)=>next.once('error',reject).listen(port,HOST,resolve));
    }finally{await close(next);}
  }finally{await app.close();fs.rmSync(dir,{recursive:true,force:true});}
});

test('legacy packaged studio is recognized without the new header; stop is authenticated',async()=>{
  let posts=0;let payload;
  const server=await listen((req,res)=>{
    if(req.url==='/'&&req.method==='GET'){
      assert.equal(req.headers.host,`${HOST}:${server.address().port}`);
      res.writeHead(200,{'Content-Type':'text/html'});res.end(legacyPage);return;
    }
    posts++;
    payload={url:req.url,origin:req.headers.origin,host:req.headers.host,
      session:req.headers['x-bazino-session']};
    let data='';req.on('data',chunk=>data+=chunk);
    req.on('end',()=>{
      payload.body=JSON.parse(data);
      res.writeHead(200,{'Content-Type':'application/json'});res.end('{"ok":true}');
      setTimeout(()=>close(server),30);
    });
  });
  try{
    const port=server.address().port;
    assert.deepEqual(await restartExisting({port,waitMs:2500}),{existing:true,restarted:true});
    assert.equal(posts,1);
    assert.deepEqual(payload,{url:'/api/dispatch',origin:`http://${HOST}:${port}`,
      host:`${HOST}:${port}`,session:token,body:{action:'app:stop'}});
  }finally{await close(server);}
});

test('a non-Bazino or malformed listener is never sent a stop request',async()=>{
  for(const body of ['Not Bazino',`<title>Unrelated program</title><meta name="bazino-session" content="${token}">`,
    '<title>BAZINO · Marketing Control</title><meta name="bazino-session" content="not-a-token">']){
    let posts=0;
    const server=await listen((req,res)=>{
      if(req.method==='POST')posts++;
      res.writeHead(200,{'Content-Type':'text/html'});res.end(body);
    });
    try{
      await assert.rejects(restartExisting({port:server.address().port}),/unrecognized service/);
      assert.equal(posts,0);assert.equal(server.listening,true);
    }finally{await close(server);}
  }
});

test('an unresponsive port is left untouched rather than killed',async()=>{
  let posts=0;
  const server=await listen((req,_res)=>{if(req.method==='POST')posts++;});
  try{
    await assert.rejects(restartExisting({port:server.address().port}),/not responding as Bazino Studio/);
    assert.equal(posts,0);assert.equal(server.listening,true);
  }finally{await close(server);}
});

test('a trickling listener cannot keep preflight alive past its deadline',async()=>{
  const server=await listen((_req,res)=>{
    res.writeHead(200);res.write('x');
    const interval=setInterval(()=>res.write('x'),50);
    res.on('close',()=>clearInterval(interval));
  });
  try{
    await assert.rejects(request(server.address().port,{timeoutMs:300}),{code:'ETIMEDOUT'});
    assert.equal(server.listening,true);
  }finally{server.closeAllConnections();await close(server);}
});

test('a Bazino-looking listener that rejects the session is not killed',async()=>{
  let posts=0;
  const server=await listen((req,res)=>{
    if(req.method==='POST'){
      posts++;res.writeHead(403);res.end('Unauthorized');return;
    }
    res.writeHead(200);res.end(legacyPage);
  });
  try{
    await assert.rejects(restartExisting({port:server.address().port}),/refused to stop/);
    assert.equal(posts,1);assert.equal(server.listening,true);
  }finally{await close(server);}
});

test('a slow or stuck old studio times out without terminating its process',async()=>{
  let posts=0;
  const server=await listen((req,res)=>{
    if(req.method==='POST'){
      posts++;res.writeHead(200,{'Content-Type':'application/json'});res.end('{"ok":true}');return;
    }
    res.writeHead(200);res.end(legacyPage);
  });
  try{
    await assert.rejects(restartExisting({port:server.address().port,waitMs:350}),/did not release port/);
    assert.equal(posts,1);assert.equal(server.listening,true);
  }finally{await close(server);}
});

test('a port freed mid-probe and an initially free port are safe to start on',async()=>{
  const server=await listen((_req,res)=>{
    res.writeHead(200);res.end(legacyPage);
    void close(server); // stop listening before a subsequent POST can reach it
  });
  const port=server.address().port;
  try{
    const result=await restartExisting({port,waitMs:1000});
    assert.deepEqual(result,{existing:true,restarted:true});
    await close(server);
    assert.deepEqual(await restartExisting({port}),{existing:false});
  }finally{await close(server);}
});

test('startup readiness waits for the new marked studio and rejects other services',async()=>{
  const reserved=await listen((_req,res)=>res.end('not Bazino'));
  const port=reserved.address().port;
  await assert.rejects(waitReady({port,waitMs:500}),/unrecognized service/);
  await close(reserved);
  await assert.rejects(waitReady({port,waitMs:250}),/did not become ready/);
  let server;
  try{
    const opening=waitReady({port,waitMs:1500});
    await delay(100);
    server=http.createServer((_req,res)=>{
      res.writeHead(200,{'X-Bazino-App':'marketing-studio'});res.end(legacyPage);
    });
    await new Promise((resolve,reject)=>server.once('error',reject).listen(port,HOST,resolve));
    assert.equal(await opening,true);
  }finally{if(server)await close(server);}
});

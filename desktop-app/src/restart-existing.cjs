'use strict';

// One-shot preflight invoked by BazinoMarketing.ps1 BEFORE starting a new Node
// server. Only a recognizable local Bazino Studio instance may be stopped.
// No token is logged, sent off-device, or written to disk.
const http=require('node:http');
const net=require('node:net');
const {HOST,PORT}=require('./server-address.cjs');
const MAX_HTML=128*1024;
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));

function request(port,{method='GET',pathname='/',token,body,timeoutMs=1600}={}){
  const headers={Host:`${HOST}:${port}`,Connection:'close'};
  if(token){
    headers.Origin=`http://${HOST}:${port}`;
    headers['X-Bazino-Session']=token;
    headers['Content-Type']='application/json';
  }
  let timer;
  return new Promise((resolve,reject)=>{
    const req=http.request({hostname:HOST,port,path:pathname,method,headers,agent:false},res=>{
      const chunks=[];let bytes=0;
      res.on('data',chunk=>{
        bytes+=chunk.length;
        if(bytes>MAX_HTML)res.destroy(new Error('Local studio response is too large'));
        else chunks.push(chunk);
      });
      res.on('error',reject);
      res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,
        body:Buffer.concat(chunks).toString('utf8')}));
    });
    // An absolute deadline, not an inactivity timer: a trickling listener must
    // not keep the EXE stuck in preflight by sending one byte at a time.
    timer=setTimeout(()=>{
      const error=new Error('Local server did not respond in time');error.code='ETIMEDOUT';
      req.destroy(error);
    },timeoutMs);
    req.on('error',reject);
    req.end(body);
  }).finally(()=>clearTimeout(timer));
}
function identify(response){
  if(response.status!==200)return null;
  // New versions advertise a fixed header. The older packaged studio did not,
  // so also recognize its exact application title + injected session meta tag.
  const marked=response.headers['x-bazino-app']==='marketing-studio';
  const legacy=response.body.includes('<title>BAZINO · Marketing Control</title>');
  if(!marked&&!legacy)return null;
  const match=/<meta name="bazino-session" content="([A-Za-z0-9_-]{43})">/.exec(response.body);
  return match?.[1]||null;
}
function occupied(port){
  return new Promise((resolve,reject)=>{
    const socket=net.connect({host:HOST,port});
    socket.once('connect',()=>{socket.destroy();resolve(true);});
    socket.once('error',e=>{
      socket.destroy();
      if(e.code==='ECONNREFUSED')resolve(false);else reject(e);
    });
    socket.setTimeout(1000,()=>{
      const error=new Error('Local port check timed out');error.code='ETIMEDOUT';
      socket.destroy();reject(error);
    });
  });
}
async function waitUntilFree(port,waitMs){
  const deadline=Date.now()+waitMs;
  while(Date.now()<deadline){
    try{if(!(await occupied(port)))return true;}
    catch{ /* treat a transient network error as busy, not permission to start */ }
    await sleep(180);
  }
  return false;
}
async function restartExisting({port=PORT,waitMs=12000}={}){
  let page;
  try{page=await request(port);}
  catch(e){
    // A previous server might have closed just as we connected. A free port is
    // safe to use; an occupied but unresponsive port is NOT safe to take over.
    if(e.code==='ECONNREFUSED'||e.code==='ECONNRESET'&&!(await occupied(port)))
      return {existing:false};
    throw new Error(`Port ${port} is not responding as Bazino Studio; no process was stopped`);
  }
  const token=identify(page);
  if(!token)throw new Error(`Port ${port} belongs to an unrecognized service; no process was stopped`);
  let stopped;
  try{stopped=await request(port,{method:'POST',pathname:'/api/dispatch',token,
    body:JSON.stringify({action:'app:stop'}),timeoutMs:4000});}
  catch{
    // A successful stop can close the connection before delivering the reply.
    if(await waitUntilFree(port,waitMs))return {existing:true,restarted:true};
    throw new Error(`Could not request a safe stop on port ${port}; no process was killed`);
  }
  let answer;
  try{answer=JSON.parse(stopped.body);}catch{}
  if(stopped.status!==200||answer?.ok!==true)
    throw new Error(`Existing Bazino Studio on port ${port} refused to stop; no process was killed`);
  if(await waitUntilFree(port,waitMs))return {existing:true,restarted:true};
  throw new Error(`Existing Bazino Studio did not release port ${port}; no process was killed`);
}
async function waitReady({port=PORT,waitMs=12000}={}){
  const deadline=Date.now()+waitMs;
  while(Date.now()<deadline){
    let page;
    try{page=await request(port,{timeoutMs:900});}
    catch(e){
      if(!['ECONNREFUSED','ECONNRESET','ETIMEDOUT'].includes(e.code))
        throw new Error(`Cannot verify the new studio on port ${port}`);
      await sleep(180);continue;
    }
    if(page.headers['x-bazino-app']==='marketing-studio'&&identify(page))return true;
    throw new Error(`Port ${port} was taken by an unrecognized service; the new studio did not start`);
  }
  throw new Error(`The new Bazino Studio did not become ready on port ${port}`);
}
if(require.main===module){
  (process.argv[2]==='--wait-ready'?waitReady():restartExisting()).then(result=>{
    process.stdout.write(process.argv[2]==='--wait-ready'?'New studio is ready.\n':
      result.existing?'Existing studio stopped; restarting.\n':'Studio port is free.\n');
  }).catch(e=>{process.stdout.write(`${e.message}\n`);process.exitCode=1;});
}
module.exports={request,identify,occupied,restartExisting,waitReady};

#!/usr/bin/env node
'use strict';
// Independent agent companion: GitHub Contents for ciphertext, signed requests, fixed Arena branch.
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { execFileSync } = require('node:child_process');
const { BRANCH, RELAY_ROOT } = require('./src/constants.cjs');
const { newIdentity, newSigningIdentity, fingerprint, combinedFingerprint,
  signRequest, verifyReply, seal, unseal } = require('./src/crypto.cjs');
const { validateOperation, isMutating } = require('./src/protocol.cjs');
const { remotePath } = require('./src/relay.cjs');

const root=path.resolve(__dirname,'../..'); // studio/desktop-app -> repository root
const keyPath=path.join(__dirname,'.agent-signing-key.json'); // gitignored, never sent to GitHub
const replyPath=path.join(__dirname,'.agent-replies.json'); // ephemeral reply private keys, gitignored
function run(exe,args){return execFileSync(exe,args,{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();}
function ghGet(file){
  const output=run('gh',['api',`${remotePath(file)}?ref=${encodeURIComponent(BRANCH)}`,'--jq','.content']);
  return JSON.parse(Buffer.from(output.replace(/\s/g,''),'base64').toString('utf8'));
}
function optionalGhGet(file){try{return ghGet(file);}catch(e){if(/404|Not Found/.test(String(e.message)))return null;throw e;}}
function signingKey(create=false){
  if(fs.existsSync(keyPath))return JSON.parse(fs.readFileSync(keyPath,'utf8'));
  if(!create)throw new Error('Run agent-client.cjs pair first; the signing key is not present');
  const pair=newSigningIdentity();
  fs.writeFileSync(keyPath,JSON.stringify(pair),{mode:0o600,flag:'wx'});
  return pair;
}
function pendingReplies(){
  try{return JSON.parse(fs.readFileSync(replyPath,'utf8'));}
  catch(e){if(e.code==='ENOENT')return {};throw e;}
}
function saveReplies(values){
  const tmp=`${replyPath}.${process.pid}.tmp`;
  fs.writeFileSync(tmp,JSON.stringify(values),{mode:0o600,flag:'wx'});
  fs.renameSync(tmp,replyPath);
}
function prepareGit(){
  if(run('git',['branch','--show-current'])!==BRANCH)throw new Error('Wrong branch: this session can only push its tracked branch');
  if(run('git',['status','--porcelain']))throw new Error('Worktree must be clean before queuing a request; do not commit unrelated changes');
  run('git',['fetch','-q','origin',BRANCH]);
  const local=run('git',['rev-parse','HEAD']),remote=run('git',['rev-parse','FETCH_HEAD']);
  if(local!==remote)run('git',['merge','--ff-only','FETCH_HEAD']); // never rewrite/force push
}
function publish(relative,object,message){
  const file=path.join(root,relative);
  fs.mkdirSync(path.dirname(file),{recursive:true});
  fs.writeFileSync(file,JSON.stringify(object)+'\n',{flag:'wx'});
  run('git',['add','--',relative]);
  run('git',['-c','user.name=bazino-agent','-c','user.email=agent@bazino.local','commit','-m',message]);
  run('git',['push','origin',BRANCH]); // only Arena session branch, NEVER force push
}
async function wait(id,privateKey,signingPublicKey,timeoutSec){
  const until=Date.now()+timeoutSec*1000;
  while(Date.now()<until){
    const reply=optionalGhGet(`responses/${id}.json`);
    if(reply){
      if(reply.id!==id||!verifyReply(reply,signingPublicKey))throw new Error('Reply signature is invalid; never trust an unverified GitHub response');
      return unseal(reply.payload,privateKey,id);
    }
    await new Promise(resolve=>setTimeout(resolve,5000));
  }
  throw new Error(`No reply yet. Later run: node agent-client.cjs fetch ${id}. Do NOT repeat a write without inspecting its status on the desktop.`);
}
async function main(){
  const [action,...params]=process.argv.slice(2);
  if(!['pair','status','fetch','api','kling','zernio-media-upload','access','engagement-check','research','editorial',
    'web','connectors','service','mcp-server'].includes(action)){
    console.error('Use: node agent-client.cjs web <public-https-url> | connectors | service <registered-id> <METHOD> <relative-path> [json] [--confirm-write] | mcp-server <id> tools|resources|read-resource <uri>|prompts|get-prompt <name> [json]|call <tool> [json] [--confirm-write] | pair | status | fetch <requestId> | access | engagement-check <accountId> | research [feed-ids] | editorial list|save <file-outside-repo> [--confirm-write] | api <zernio|cloudflare> <METHOD> <path> [json] [--idempotency=UUID] [--confirm-write] | kling <tool> [json-arguments] [--confirm-write]');
    process.exitCode=2;return;
  }
  if(action==='pair'){
    prepareGit();
    const keys=signingKey(true),id={v:1,publicKey:keys.publicKey,fingerprint:fingerprint(keys.publicKey),publishedAt:new Date().toISOString()};
    const existing=optionalGhGet('agent-identity.json');
    if(existing&&existing.fingerprint!==id.fingerprint)throw new Error('Agent identity changed on GitHub. Owner must investigate; do not silently overwrite or regenerate.');
    if(!existing)publish(`${RELAY_ROOT}/agent-identity.json`,id,'marketing relay: publish agent verification key');
    console.log(`Agent signing fingerprint: ${id.fingerprint}. Owner: compare this exact value with the desktop pairing dialog, then approve pairing.`);
    return;
  }
  if(action==='fetch'){
    const id=params[0],replies=pendingReplies();
    if(typeof id!=='string'||!/^[a-f0-9-]{36}$/i.test(id)||!replies[id])throw new Error('Unknown local request ID; its reply private key is not present');
    const result=await wait(id,replies[id].privateKey,replies[id].signingPublicKey,180);
    delete replies[id];saveReplies(replies);
    console.log(JSON.stringify(result,null,2));return;
  }
  const publicInfo=optionalGhGet('desktop-identity.json');
  if(!publicInfo?.publicKey||!publicInfo?.signingPublicKey||
    publicInfo.fingerprint!==combinedFingerprint(publicInfo.publicKey,publicInfo.signingPublicKey))
    throw new Error('Desktop has not connected/published valid encryption AND signing keys');
  if(action==='status'){
    console.log(`Desktop key fingerprint: ${publicInfo.fingerprint}. Compare with the Windows app before sending requests.`);
    const agent=optionalGhGet('agent-identity.json');
    if(agent)console.log(`Agent signing fingerprint: ${agent.fingerprint}. Compare with the Windows pairing dialog.`);
    return;
  }
  const expected=process.env.BAZINO_DESKTOP_FINGERPRINT;
  if(!expected||expected!==publicInfo.fingerprint)throw new Error('Set BAZINO_DESKTOP_FINGERPRINT to the fingerprint displayed IN THE DESKTOP APP, after owner verification. Never accept a GitHub-only fingerprint.');
  const keys=signingKey();
  const paired=ghGet('agent-identity.json');
  if(paired.fingerprint!==fingerprint(keys.publicKey))throw new Error('Agent signing identity does not match GitHub; do not send requests');
  const confirmed=params.includes('--confirm-write');
  const idFlag=params.find(x=>x.startsWith('--idempotency='));
  const args=params.filter(x=>x!=='--confirm-write'&&!x.startsWith('--idempotency='));
  if(idFlag&&action!=='api')throw new Error('--idempotency is supported only for Zernio POST /v1/posts');
  let op;
  if(action==='api'){
    const [provider,method,apiPath,rawBody]=args;
    op={kind:'api',provider,method,path:apiPath,...(rawBody?{body:JSON.parse(rawBody)}:{}),
      ...((provider==='zernio'&&String(method).toUpperCase()==='POST'&&apiPath?.split('?')[0]==='/v1/posts')?
        {idempotencyKey:idFlag?idFlag.slice('--idempotency='.length):randomUUID()}:{}),
      ...((idFlag&&!(provider==='zernio'&&String(method).toUpperCase()==='POST'&&apiPath?.split('?')[0]==='/v1/posts'))?
        {idempotencyKey:idFlag.slice('--idempotency='.length)}:{})};
  }else if(action==='access'){
    op={kind:'zernio-access'};
  }else if(action==='engagement-check'){
    op={kind:'zernio-engagement-check',accountId:args[0]};
  }else if(action==='research'){
    op={kind:'research',action:'scan',sourceIds:args.length?args[0].split(','):[]};
  }else if(action==='editorial'){
    if(args[0]==='list')op={kind:'editorial',action:'list'};
    else if(args[0]==='save'&&args[1]){
      const file=fs.realpathSync(path.resolve(args[1]));
      if(!file.endsWith('.json')||!fs.statSync(file).isFile()||fs.statSync(file).size>64000||
        file.startsWith(root+path.sep))
        throw new Error('Use a small private JSON research report file outside the Git checkout');
      op={kind:'editorial',action:'save',report:JSON.parse(fs.readFileSync(file,'utf8'))};
    }else throw new Error('Use editorial list or editorial save <JSON-file-outside-repository>');
  }else if(action==='web'){
    if(args.length!==1)throw new Error('Use web <public-https-url> (no token in URL)');
    op={kind:'web-fetch',url:args[0]};
  }else if(action==='connectors'){
    op={kind:'gateway-list'};
  }else if(action==='service'){
    const [connectorId,method,apiPath,rawBody]=args;
    if(args.length<3||args.length>4)throw new Error('Use service <id> <METHOD> <relative-path> [json]');
    op={kind:'gateway-api',connectorId,method,path:apiPath,
      ...(rawBody?{body:JSON.parse(rawBody)}:{})};
  }else if(action==='mcp-server'){
    const [connectorId,verb,name,rawArgs]=args;
    if(args.length<2||args.length>4||
      ['call','read-resource','get-prompt'].includes(verb)&&!name||
      verb==='read-resource'&&args.length!==3)
      throw new Error('Use mcp-server <id> <action> [name/uri] [json]');
    if(verb==='call')op={kind:'gateway-mcp',connectorId,action:'call',tool:name,
      args:rawArgs?JSON.parse(rawArgs):{}};
    else if(verb==='read-resource')op={kind:'gateway-mcp',connectorId,action:verb,uri:name};
    else if(verb==='get-prompt')op={kind:'gateway-mcp',connectorId,action:verb,name,
      args:rawArgs?JSON.parse(rawArgs):{}};
    else if(['tools','resources','prompts'].includes(verb)&&args.length===2)
      op={kind:'gateway-mcp',connectorId,action:verb};
    else throw new Error('Use a supported MCP action: tools, resources, read-resource, prompts, get-prompt, call');
  }else if(action==='kling'){
    if(args.length>2)throw new Error('Use one JSON object as Kling MCP tool arguments, not CLI flags');
    op={kind:'kling',command:args[0],args:args[1]?JSON.parse(args[1]):{}};
  }else if(action==='zernio-media-upload'){
    op={kind:'zernio-media-upload',assetId:args[0]};
  }
  op=validateOperation(op);
  if(isMutating(op)&&!confirmed)throw new Error('Write/cost action requires owner approval in the conversation AND --confirm-write. The desktop also asks for confirmation by default.');
  prepareGit();
  const id=randomUUID(),replyKeys=newIdentity(),now=Date.now();
  const request={v:1,id,issuedAt:new Date(now).toISOString(),expiresAt:new Date(now+10*60000).toISOString(),
    replyPublicKey:replyKeys.publicKey,payload:seal(op,publicInfo.publicKey,id)};
  request.signature=signRequest(request,keys.privateKey);
  const replies=pendingReplies();replies[id]={privateKey:replyKeys.privateKey,
    signingPublicKey:publicInfo.signingPublicKey,createdAt:new Date().toISOString()};saveReplies(replies);
  publish(`${RELAY_ROOT}/requests/${id}.json`,request,`marketing relay: request ${id}`);
  console.log(`Signed encrypted request ${id} queued on ${BRANCH}; waiting for owner's desktop.`);
  if(op.idempotencyKey)console.log(`Zernio post retry key (24h, reuse ONLY for this unchanged logical post): ${op.idempotencyKey}`);
  const result=await wait(id,replyKeys.privateKey,publicInfo.signingPublicKey,180);
  delete replies[id];saveReplies(replies);
  console.log(JSON.stringify(result,null,2));
}
main().catch(e=>{console.error(String(e.message||e));process.exitCode=1;});

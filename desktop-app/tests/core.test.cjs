'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {newIdentity,newSigningIdentity,seal,unseal,fingerprint,combinedFingerprint,
  signRequest,verifyRequest,signReply,verifyReply}=require('../src/crypto.cjs');
const {validateOperation,isMutating,redact,validateEnvelope}=require('../src/protocol.cjs');
const {Vault}=require('../src/vault.cjs');
const {Assets}=require('../src/assets.cjs');
const {KlingOAuthProvider}=require('../src/kling.cjs');
const {BRANCH}=require('../src/constants.cjs');
const mockStorage={isEncryptionAvailable:()=>true,encryptString:s=>Buffer.from(`ENC:${s}`),decryptString:b=>b.toString().slice(4)};
function temp(){return fs.mkdtempSync(path.join(os.tmpdir(),'bazino-test-'));}

test('fixed branch and authenticated end-to-end envelopes',()=>{
  assert.equal(BRANCH,'arena/01a0d4ee-bazino-gamenet-portal');
  const desktop=newIdentity(),agent=newIdentity(),signing=newSigningIdentity(),id='12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6';
  const request={v:1,id,issuedAt:new Date().toISOString(),expiresAt:new Date(Date.now()+60000).toISOString(),
    replyPublicKey:agent.publicKey,payload:seal({kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts'},desktop.publicKey,id)};
  request.signature=signRequest(request,signing.privateKey);
  assert.equal(verifyRequest(request,signing.publicKey),true);
  assert.equal(fingerprint(signing.publicKey).length,32);
  validateEnvelope(request);
  assert.equal(unseal(request.payload,desktop.privateKey,id).path,'/v1/accounts');
  assert.throws(()=>unseal(request.payload,desktop.privateKey,'tampered'));
  request.expiresAt=new Date(Date.now()+120000).toISOString();
  assert.equal(verifyRequest(request,signing.publicKey),false);
  assert.throws(()=>validateEnvelope({...request,expiresAt:new Date(Date.now()-1000).toISOString()}));
  const desktopSigning=newSigningIdentity();
  const reply={v:1,id,createdAt:new Date().toISOString(),payload:seal({ok:true,result:'test'},agent.publicKey,id)};
  reply.signature=signReply(reply,desktopSigning.privateKey);
  assert.equal(verifyReply(reply,desktopSigning.publicKey),true);
  assert.equal(combinedFingerprint(desktop.publicKey,desktopSigning.publicKey).length,32);
  assert.equal(unseal(reply.payload,agent.privateKey,id).result,'test');
  assert.equal(verifyReply({...reply,id:'00000000-0000-0000-0000-000000000000'},desktopSigning.publicKey),false);
});

test('validates write classification, path isolation, body limits and sensitive fields',()=>{
  assert.equal(isMutating(validateOperation({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',body:{publishNow:true}})),true);
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    body:{platforms:[{platform:'whatsapp',accountId:'connected-account'}]}}),/WhatsApp uses Zernio inbox\/broadcast/);
  assert.equal(isMutating(validateOperation({kind:'api',provider:'zernio',method:'POST',path:'/v1/broadcasts',
    body:{name:'owner-reviewed'}})),true);
  for(const provider of ['youtube','telegram','telegram-gateway','manus'])
    assert.throws(()=>validateOperation({kind:'api',provider,method:'GET',path:'/v1/accounts'}),/Unknown tool/);
  assert.throws(()=>validateOperation({kind:'youtube-upload',assetId:'12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6'}));
  assert.throws(()=>validateOperation({kind:'telegram-media-send',assetId:'12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6'}));
  const fluxPath='/accounts/1234567890abcdef1234567890abcdef/ai/run/@cf/black-forest-labs/flux-1-schnell';
  assert.equal(isMutating(validateOperation({kind:'api',provider:'cloudflare',method:'POST',path:fluxPath,body:{prompt:'a visual'}})),true);
  assert.throws(()=>validateOperation({kind:'api',provider:'cloudflare',method:'GET',path:'/user/tokens'}));
  assert.throws(()=>validateOperation({kind:'api',provider:'cloudflare',method:'POST',path:'/accounts/1234567890abcdef1234567890abcdef/ai/run/@cf/other'}));
  assert.equal(isMutating(validateOperation({kind:'kling',command:'text_to_video',args:{model:'from-discovery',prompt:'a test'}})),true);
  assert.equal(isMutating(validateOperation({kind:'kling',command:'who_am_i',args:{}})),false);
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'GET',path:'//evil.example/take'}));
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'GET',path:'https://evil.example'}));
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'GET',path:'/foo/../bar'}));
  for(const bypass of ['/v1/./posts','/v1//posts','/v1/%70osts','/v1/%2e/posts',
    '/v1/%69nbox/conversations','/v1/comments?access_token=secret'])
    assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'POST',path:bypass,body:{}}),
      /canonical|Credentials/);
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'GET',path:'/v1/posts',body:{x:1}}));
  assert.equal(validateOperation({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    idempotencyKey:'12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6'}).idempotencyKey,'12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6');
  assert.throws(()=>validateOperation({kind:'api',provider:'cloudflare',method:'POST',path:fluxPath,
    idempotencyKey:'12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6'}));
  assert.throws(()=>validateOperation({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',body:{x:'a'.repeat(70000)}}));
  const cleaned=redact({access_token:'xxx',message:'Bearer realtoken abcSECRET12345678'},0,['abcSECRET12345678']);
  assert.equal(cleaned.access_token,'[redacted]');
  assert.ok(!JSON.stringify(cleaned).includes('realtoken'));
  assert.ok(!JSON.stringify(cleaned).includes('abcSECRET'));
});

test('encrypted vault persists identity/agent pairing, never exposes secrets through settings',()=>{
  const dir=temp(),file=path.join(dir,'settings.json');
  try{
    const vault=new Vault(file,mockStorage);vault.load();
    vault.update({secrets:{zernioKey:'SECRET_PRIVATE_KEY',githubToken:'github-secret'}});
    assert.ok(!fs.readFileSync(file,'utf8').includes('SECRET_PRIVATE_KEY'));
    assert.equal(vault.get('zernioKey'),'SECRET_PRIVATE_KEY');
    assert.equal(vault.publicSettings().configured.githubToken,true);
    assert.ok(!JSON.stringify(vault.publicSettings()).includes('github-secret'));
    const first=vault.identity();assert.equal(vault.identity().fingerprint,first.fingerprint);
    assert.equal(vault.publicSettings().identityFingerprint,
      combinedFingerprint(first.publicKey,first.signingPublicKey));
    // A prior Windows release could have stored direct-network credentials.
    // Upgrading removes them from ciphertext, without losing device pairing.
    vault.data.secrets.youtubeRefreshToken='OLD_YOUTUBE_TOKEN';
    vault.data.secrets.telegramBotToken='OLD_TELEGRAM_TOKEN';
    vault.data.secrets.manusApiKey='OLD_MANUS_TOKEN';
    vault.save();
    const reopened=new Vault(file,mockStorage);reopened.load();
    assert.equal(reopened.identity().fingerprint,first.fingerprint);
    assert.equal(reopened.get('youtubeRefreshToken'),'');
    const ciphertext=JSON.parse(fs.readFileSync(file,'utf8')).ciphertext;
    const decoded=mockStorage.decryptString(Buffer.from(ciphertext,'base64'));
    assert.ok(!decoded.includes('OLD_YOUTUBE_TOKEN')&&!decoded.includes('OLD_TELEGRAM_TOKEN')&&!decoded.includes('OLD_MANUS_TOKEN'));
    assert.throws(()=>reopened.update({secrets:{manusApiKey:'not-allowed'}}));
    const agent=newSigningIdentity();
    assert.equal(reopened.pairAgent(agent.publicKey).agentFingerprint,fingerprint(agent.publicKey));
    assert.throws(()=>reopened.update({agentPublicKey:agent.publicKey}));
    reopened.saveKlingOAuth('tokens',{access_token:'LOCAL_OAUTH_SECRET'});
    assert.equal(reopened.publicSettings().klingAuthorized,true);
    assert.ok(!JSON.stringify(reopened.publicSettings()).includes('LOCAL_OAUTH_SECRET'));
    assert.ok(!fs.readFileSync(file,'utf8').includes('LOCAL_OAUTH_SECRET'));
    reopened.saveKlingOAuth('tokens',undefined);
    assert.equal(reopened.publicSettings().klingAuthorized,false);
    reopened.update({clearSecrets:['githubToken']});assert.equal(reopened.get('githubToken'),'');
    assert.throws(()=>new Vault(path.join(dir,'other'),{isEncryptionAvailable:()=>false}).load());
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('local asset references cannot traverse a path; OAuth provider only persists inside encrypted vault',async()=>{
  const dir=temp();try{
    const src=path.join(dir,'photo.jpg');fs.writeFileSync(src,'JPEG TEST');
    const assets=new Assets(path.join(dir,'assets'));
    const asset=assets.import(src);assert.equal(assets.list()[0].id,asset.id);
    assert.equal(assets.find(asset.id).mime,'image/jpeg');assert.throws(()=>assets.find('../../settings.json'));
    assert.throws(()=>assets.import(src,'../escape.jpg'));
    const vault=new Vault(path.join(dir,'settings.json'),mockStorage);vault.load();
    const redirects=[];
    const provider=new KlingOAuthProvider({vault,redirectUrl:'http://127.0.0.1:59677/kling-callback',
      openExternal:async(url)=>redirects.push(url)});
    provider.saveClientInformation({client_id:'test-client'});
    provider.saveTokens({access_token:'SECRET_OAUTH_ACCESS'});
    provider.saveCodeVerifier('PKCE_VERIFIER');
    assert.equal(provider.codeVerifier(),'PKCE_VERIFIER');
    assert.equal(provider.clientInformation().client_id,'test-client');
    assert.equal(provider.tokens().access_token,'SECRET_OAUTH_ACCESS');
    assert.ok(!fs.readFileSync(vault.file,'utf8').includes('SECRET_OAUTH_ACCESS'));
    await assert.rejects(provider.redirectToAuthorization(new URL('http://evil.example/login')),/insecure/);
    await provider.redirectToAuthorization(new URL('https://kling.ai/login'));
    assert.deepEqual(redirects,['https://kling.ai/login']);
    provider.invalidateCredentials('tokens');assert.equal(provider.tokens(),undefined);
    assert.throws(()=>validateOperation({kind:'kling',command:'text_to_video',args:['--model','v2']}));
    assert.throws(()=>validateOperation({kind:'kling',command:'account',args:{}}));
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

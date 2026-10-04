'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {Providers,serviceUrl,readLimited}=require('../src/providers.cjs');
const {Assets}=require('../src/assets.cjs');
const json=(value,init={})=>Response.json(value,init);
const vault=(secret={},publics={})=>({get:k=>secret[k]||'',public:k=>publics[k]||''});
const account='1234567890abcdef1234567890abcdef';
const fluxPath=`/accounts/${account}/ai/run/@cf/black-forest-labs/flux-1-schnell`;

test('Zernio requests use the fixed host, never call Google, Telegram Bot or Manus',async()=>{
  const events=[];
  const api=new Providers({vault:vault({zernioKey:'z-key'}),assets:{},fetcher:async(url,options)=>{
    events.push([String(url),options]);
    return json({data:{status:'ok',authorization:'should-be-redacted'}});
  }});
  const result=await api.api({kind:'api',provider:'zernio',method:'GET',path:'/v1/accounts'});
  assert.equal(events[0][0],'https://zernio.com/api/v1/accounts');
  assert.equal(events[0][1].headers.Authorization,'Bearer z-key');
  assert.equal(result.body.data.authorization,'[redacted]');
  for(const provider of ['youtube','telegram','manus','telegram-gateway'])
    await assert.rejects(api.api({kind:'api',provider,method:'GET',path:'/v1/accounts'}),/Unknown tool/);
  assert.equal(events.length,1);
  assert.throws(()=>serviceUrl('youtube','/channels',vault()),/Unknown API host/);
  assert.throws(()=>serviceUrl('cloudflare',fluxPath,vault()),/Set your own Cloudflare account ID/);
});

test('Zernio discovers live accounts/health and uses 24h Idempotency-Key for targeted posts',async()=>{
  const id='12fbb621-e0c7-43a5-8a39-d01d4ca1b4e6';
  const platforms=[{platform:'youtube',accountId:'youtube-account',platformSpecificData:{title:'Studio video',visibility:'private'}},
    {platform:'telegram',accountId:'telegram-account',platformSpecificData:{parseMode:'HTML'}},
    {platform:'instagram',accountId:'instagram-account'}];
  const seen=[];
  const api=new Providers({vault:vault({zernioKey:'key'}),assets:{},fetcher:async(url,options)=>{
    seen.push([url,options]);
    assert.equal(options.headers.Authorization,'Bearer key');
    if(url.endsWith('/v1/accounts?status=connected'))
      return json({hasAnalyticsAccess:true,accounts:platforms.map(p=>({_id:p.accountId,platform:p.platform,
        profileId:{_id:'brand-profile'},isActive:true}))});
    if(url.endsWith('/v1/accounts/health'))return json({accounts:platforms.map(p=>({accountId:p.accountId,
      platform:p.platform,status:'healthy',canPost:true,canFetchAnalytics:true,tokenValid:true,needsReconnect:false}))});
    assert.equal(url,'https://zernio.com/api/v1/posts');
    assert.equal(options.headers['Idempotency-Key'],id);
    assert.equal(options.headers['x-request-id'],undefined);
    assert.deepEqual(JSON.parse(options.body).platforms,platforms);
    return json({post:{_id:'post-1',status:'scheduled'}},{status:201});
  }});
  const access=await api.access();
  assert.equal(access.accounts.length,3);
  assert.equal(access.accounts[0].canPost,true,'tokenValid status must survive redaction');
  assert.equal(access.accounts[0].canFetchAnalytics,true);
  const scheduledFor=new Date(Date.now()+3600_000).toISOString();
  const result=await api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    body:{content:'Approved Turkish content',platforms,scheduledFor},idempotencyKey:id});
  assert.equal(result.status,201);assert.equal(result.idempotencyKey,id);
  assert.equal(seen.filter(([url])=>url.endsWith('/v1/posts')).length,1);
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    body:{content:'test',platforms:[{platform:'telegram',accountId:'not-connected'}],publishNow:true}}),
    /not confirmed a connected telegram account/);
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/posts',
    body:{content:'test',platforms,scheduledFor:'2020-01-01T00:00:00Z'}}),/publishes past scheduledFor immediately/);
  assert.equal(seen.filter(([url])=>url.endsWith('/v1/posts')).length,1,'bad targets and past schedules never reach POST');
});

test('automatic comment delivery requires the right account, post, keyword, and inbox read scopes',async()=>{
  let writes=0,allowMessages=true;
  const fetcher=async(url)=>{
    if(url.endsWith('/v1/accounts?status=connected'))return json({accounts:[{_id:'ig-one',platform:'instagram',
      profileId:{_id:'brand-one'},isActive:true}]});
    if(url.endsWith('/v1/accounts/health'))return json({accounts:[{accountId:'ig-one',platform:'instagram',
      status:'healthy',canPost:true,tokenValid:true,needsReconnect:false}]});
    if(url.includes('/v1/inbox/comments'))return json({data:[],meta:{accountsQueried:1,accountsFailed:0,
      failedAccounts:[],accountsSkipped:[]}});
    if(url.includes('/v1/inbox/conversations'))return json({data:[],meta:{accountsQueried:1,
      accountsFailed:allowMessages?0:1,failedAccounts:allowMessages?[]:[{accountId:'ig-one'}],accountsSkipped:[]}});
    if(url.endsWith('/v1/comment-automations')){writes++;return json({success:true,automation:{id:'auto1',isActive:true}},{status:201});}
    throw new Error('Unexpected request '+url);
  };
  const api=new Providers({vault:vault({zernioKey:'key'}),assets:{},fetcher});
  const body={profileId:'brand-one',accountId:'ig-one',name:'Guide',postId:'a'.repeat(24),
    keywords:['AYAR'],matchMode:'exact',dmMessage:'Gerçek ayar listesi'};
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',
    body:{...body,postId:undefined}}),/account-wide automatic DM is not allowed/);
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',
    body:{...body,keywords:[]}}),/nonempty, topic-specific keyword/);
  allowMessages=false;
  const checked=await api.engagementAccess('ig-one');assert.equal(checked.messagesReadable,false);
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',body}),
    /not confirmed comment and message read access/);
  assert.equal(writes,0);
  allowMessages=true;
  const missingScopeProof=new Providers({vault:vault({zernioKey:'key'}),assets:{},fetcher:async(url,opts)=>{
    if(url.includes('/v1/inbox/'))return json({data:[]});
    return fetcher(url,opts);
  }});
  assert.equal((await missingScopeProof.engagementAccess('ig-one')).messagesReadable,false,
    'HTTP 200 with no queried account is not scope evidence');
  assert.equal((await api.api({kind:'api',provider:'zernio',method:'POST',path:'/v1/comment-automations',body})).status,201);
  assert.equal(writes,1);
});

test('redirects and oversized provider responses are refused',async()=>{
  const api=new Providers({vault:vault({zernioKey:'test'}),assets:{},fetcher:async()=>new Response(null,{status:302,headers:{Location:'https://evil.example'}})});
  await assert.rejects(api.api({kind:'api',provider:'zernio',method:'GET',path:'/v1/posts'}),/Redirect refused/);
  await assert.rejects(readLimited(new Response('x'.repeat(100)),50),/size limit/);
});

test('FLUX is optional local access limited to the configured account and model',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'providers-'));
  try{
    const assets=new Assets(path.join(dir,'assets'));
    const jpg=Buffer.from([0xff,0xd8,0xff,0x00,0x11]);
    const fetcher=async(url,options)=>{
      assert.equal(String(url),`https://api.cloudflare.com/client/v4${fluxPath}`);
      assert.equal(options.method,'POST');assert.equal(options.headers.Authorization,'Bearer optional-local-token');
      return json({result:{image:jpg.toString('base64')}});
    };
    const api=new Providers({vault:vault({cloudflareToken:'optional-local-token'},{cloudflareAccountId:account}),assets,fetcher});
    const result=await api.api({kind:'api',provider:'cloudflare',method:'POST',path:fluxPath,body:{prompt:'game art'}});
    assert.equal(result.body.result.image.imported.mime,'image/jpeg');
    assert.equal(assets.find(result.body.result.image.imported.id).bytes,jpg.length);
    assert.ok(!JSON.stringify(result).includes(jpg.toString('base64')));
    const missing=new Providers({vault:vault({},{cloudflareAccountId:account}),assets,fetcher});
    await assert.rejects(missing.api({kind:'api',provider:'cloudflare',method:'POST',path:fluxPath}),/Use portal FLUX by default/);
    assert.throws(()=>serviceUrl('cloudflare',`/accounts/${'a'.repeat(32)}/ai/run/@cf/black-forest-labs/flux-1-schnell`,
      vault({},{cloudflareAccountId:account})),/Set your own Cloudflare account ID/);
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('Zernio presigned media upload sends no Bearer token to storage; rejects unexpected hosts',async()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'providers-'));
  try{
    const file=path.join(dir,'photo.png');fs.writeFileSync(file,Buffer.from([0x89,0x50,0x4e,0x47]));
    const assets=new Assets(path.join(dir,'assets')),asset=assets.import(file);
    const seen=[];
    const fetcher=async(url,options)=>{
      seen.push([String(url),options]);
      if(seen.length===1)return json({uploadUrl:'https://bucket.r2.cloudflarestorage.com/temp/file?X-Amz-Signature=one',publicUrl:'https://media.zernio.com/temp/file'});
      for await(const chunk of options.body) assert.ok(chunk.length>0);
      return new Response(null,{status:200});
    };
    const api=new Providers({vault:vault({zernioKey:'PRIVATE'}),assets,fetcher});
    const result=await api.uploadZernioMedia({kind:'zernio-media-upload',assetId:asset.id});
    assert.equal(result.publicUrl,'https://media.zernio.com/temp/file');
    assert.equal(seen[0][1].headers.Authorization,'Bearer PRIVATE');
    assert.ok(!JSON.stringify(seen[1][1].headers).includes('PRIVATE'));
    assert.ok(!JSON.stringify(result).includes('X-Amz-Signature'));
    const bad=new Providers({vault:vault({zernioKey:'PRIVATE'}),assets,
      fetcher:async()=>json({uploadUrl:'https://evil.example/temp/file',publicUrl:'https://media.zernio.com/temp/file'})});
    await assert.rejects(bad.uploadZernioMedia({kind:'zernio-media-upload',assetId:asset.id}),/unexpected media host/);
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('promoting a Zernio draft via PUT inherits only verified live accounts and rejects past schedules',async()=>{
  const postId='c'.repeat(24);const events=[];
  let canPost=false;
  const api=new Providers({vault:vault({zernioKey:'key'}),assets:{},fetcher:async(url,opts)=>{
    events.push([url,opts]);
    if(url.endsWith(`/v1/posts/${postId}`)&&opts.method==='GET')return json({post:{_id:postId,status:'draft',
      platforms:[{platform:'instagram',accountId:{_id:'ig-account'},status:'draft'}]}});
    if(url.endsWith('/v1/accounts?status=connected'))return json({accounts:[{_id:'ig-account',platform:'instagram',isActive:true}]});
    if(url.endsWith('/v1/accounts/health'))return json({accounts:[{accountId:'ig-account',status:'healthy',
      canPost,tokenValid:true,needsReconnect:false}]});
    if(url.endsWith(`/v1/posts/${postId}`)&&opts.method==='PUT')return json({post:{_id:postId,status:'scheduled'}});
    throw Error('Unexpected request');
  }});
  const body={isDraft:false,scheduledFor:new Date(Date.now()+3_600_000).toISOString()};
  const op={kind:'api',provider:'zernio',method:'PUT',path:`/v1/posts/${postId}`,body};
  await assert.rejects(api.api(op),/health does not confirm posting access/);
  assert.equal(events.filter(([,opts])=>opts.method==='PUT').length,0);
  canPost=true;
  assert.equal((await api.api(op)).status,200);
  assert.equal(events.filter(([,opts])=>opts.method==='PUT').length,1);
  await assert.rejects(api.api({...op,body:{...body,scheduledFor:'2020-01-01T00:00:00Z'}}),
    /publishes past scheduledFor immediately/);
  assert.equal(events.filter(([,opts])=>opts.method==='PUT').length,1);
});

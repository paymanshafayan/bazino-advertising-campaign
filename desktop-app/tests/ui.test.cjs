'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {JSDOM}=require('jsdom');
const root=path.resolve(__dirname,'../src');
const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const script=fs.readFileSync(path.join(root,'ui.js'),'utf8');
const browserClient=fs.readFileSync(path.join(root,'client.js'),'utf8');
const flush=()=>new Promise(resolve=>setTimeout(resolve,15));

test('browser tab reports authenticated departure without leaking its session to the URL',async()=>{
  const dom=new JSDOM('<meta name="bazino-session" content="local-test-session">',{
    url:'http://127.0.0.1:59670/',runScripts:'outside-only'});
  const requests=[];
  dom.window.fetch=async(url,options)=>{
    requests.push({url,options});return {ok:true,json:async()=>({ok:true})};
  };
  try{
    dom.window.eval(browserClient);
    await dom.window.marketing.startup();
    const started=JSON.parse(requests[0].options.body);
    assert.equal(started.action,'startup');assert.ok(started.arg.tabId);
    dom.window.dispatchEvent(new dom.window.Event('pagehide'));
    await flush();
    const left=requests.at(-1);
    assert.equal(left.url,'/api/dispatch');
    assert.equal(left.options.keepalive,true);
    assert.equal(left.options.headers['X-Bazino-Session'],'local-test-session');
    assert.deepEqual(JSON.parse(left.options.body),{action:'ui:leave',arg:{tabId:started.arg.tabId}});
  }finally{dom.window.close();}
});

function fixture({engagementError=false,reports=[]}={}){
  const dom=new JSDOM(html,{url:'http://127.0.0.1:59670/',runScripts:'outside-only'});
  const {window}=dom;
  window.scrollTo=()=>{};window.confirm=()=>true;
  const calls=[],saved=[],opened=[],registered=[],logins=[];
  const settings={configured:{},klingAuthorized:false,cloudflareAccountId:'1234567890abcdef1234567890abcdef',
    autoApproveReads:true,autoApproveWrites:false,agentFingerprint:'',identityFingerprint:''};
  const accounts=['instagram','youtube','telegram','facebook'].map(platform=>({
    accountId:`${platform}-account`,platform,profileId:'brand-profile',displayName:`Brand ${platform}`,
    connected:true,canPost:true,tokenValid:true}));
  window.marketing={
    startup:async()=>({settings,history:[],assets:[],relay:{connected:false}}),
    saveSettings:async patch=>{saved.push(patch);return {...settings,...patch,configured:{zernioKey:!!patch.secrets?.zernioKey}};},
    listConnectors:async()=>({ok:true,connectors:registered.map(x=>x.public)}),
    saveConnector:async row=>{
      const item={...row};registered.push({input:item,public:{id:row.id,name:row.name,
        kind:row.kind,url:row.url,auth:row.auth,headerName:row.headerName,
        agentAccess:row.agentAccess,hasCredential:!!row.credential,authorized:false}});
      return {ok:true,connector:registered.at(-1).public,connectors:registered.map(x=>x.public)};
    },
    removeConnector:async id=>{const index=registered.findIndex(x=>x.public.id===id);
      if(index>=0)registered.splice(index,1);
      return {ok:true,connectors:registered.map(x=>x.public)};},
    authorizeConnector:async id=>{logins.push(id);return {ok:true,message:'mock-login'};},
    run:async op=>{
      calls.push(op);
      if(op.kind==='zernio-access')return {ok:true,accounts,hasAnalyticsAccess:false};
      if(op.kind==='zernio-engagement-check'){
        if(engagementError)throw Error('Inbox read failed');
        return {ok:true,accountId:op.accountId,platform:'instagram',commentsReadable:true,messagesReadable:true};
      }
      if(op.kind==='editorial'&&op.action==='list')return {ok:true,reports};
      if(op.kind==='editorial'&&op.action==='save')return {ok:true,report:{id:'saved-report'}};
      if(op.kind==='research')return {ok:true,observedAt:new Date().toISOString(),sources:[{source:'VGC',items:[
        {title:'New gameplay update',url:'https://example.com/article',sourceName:'VGC',publishedAt:new Date().toISOString()}]}]};
      if(op.kind==='api'&&op.path==='/v1/posts')return {ok:true,status:202,body:{post:{status:'processing'}}};
      return {ok:true,status:200,body:{message:'success'}};
    },
    connectRelay:async()=>({connected:false,error:'token needed'}),disconnectRelay:async()=>({connected:false}),
    pairAgent:async()=>({paired:false}),importAsset:async()=>null,openAsset:async()=>true,
    authorizeKling:async()=>({ok:true}),klingIdentity:async()=>({ok:true}),
    klingTools:async()=>({ok:true}),logoutKling:async()=>({ok:true}),stopApp:async()=>({ok:true}),
    openWebsite:async url=>{opened.push(url);},onActivity:()=>{},onRelay:()=>{}
  };
  window.eval(script);
  return {dom,window,calls,saved,opened,registered,logins,
    q:selector=>window.document.querySelector(selector)};
}

test('desktop UI routes social only through discovered Zernio accounts, schedule/keys and portal FLUX',async()=>{
  const {dom,calls,saved,opened,q}=fixture();
  await flush();
  assert.equal(q('#view-overview').classList.contains('active'),true);
  q('[data-url="https://bazino.pro/admin/content"]').click();await flush();
  assert.deepEqual(opened,['https://bazino.pro/admin/content']);
  q('[data-preset="zernio-accounts"]').click();
  assert.equal(q('#api-path').value,'/v1/accounts');
  q('#run-api').click();await flush();
  assert.equal(calls.find(op=>op.kind==='api').method,'GET');
  assert.match(q('#api-result').textContent,/"status": 200/);
  q('[data-preset="flux-image"]').click();
  assert.match(q('#api-path').value,/accounts\/1234567890abcdef1234567890abcdef\/ai\/run/);
  assert.ok(JSON.parse(q('#api-body').value).prompt);
  q('[data-go="settings"]').click();
  q('[data-secret="zernioKey"]').value='ZERNIO_SECRET';
  q('#settings-form').dispatchEvent(new dom.window.Event('submit',{bubbles:true,cancelable:true}));await flush();
  assert.equal(saved[0].secrets.zernioKey,'ZERNIO_SECRET');
  assert.equal(q('[data-secret="zernioKey"]').value,'');
  assert.ok(!dom.window.document.body.textContent.includes('ZERNIO_SECRET'));
  for(const field of ['youtubeRefreshToken','telegramBotToken','manusApiKey'])
    assert.equal(q(`[data-secret="${field}"]`),null);

  q('[data-go="zernio"]').click();q('#refresh-access').click();await flush();
  assert.equal(q('#post-platform option[value="whatsapp"]'),null);
  assert.match(q('#view-zernio').textContent,/WhatsApp.*API پیام\/برادکست/s);
  assert.ok([...q('#post-account').options].some(o=>o.value==='instagram-account'));
  assert.equal(q('#post-account').value,'','the owner must choose an account');
  q('#post-account').value='instagram-account';
  q('#post-content').value='Draft for review';
  q('#post-draft').click();await flush();
  const posts=()=>calls.filter(op=>op.kind==='api'&&op.path==='/v1/posts');
  assert.equal(posts()[0].body.isDraft,true);
  assert.equal(posts()[0].body.platforms[0].accountId,'instagram-account');
  assert.match(posts()[0].idempotencyKey,/^[a-f0-9-]{36}$/);
  q('#post-draft').click();await flush();
  assert.equal(posts()[1].idempotencyKey,posts()[0].idempotencyKey,'retry same body reuses key');

  q('[data-platform="youtube"]').click();
  assert.ok([...q('#post-account').options].some(o=>o.value==='youtube-account'));
  q('#post-account').value='youtube-account';
  q('#post-content').value='Owner-approved video';
  q('#post-media-url').value='https://media.zernio.com/temp/video.mp4';
  q('#post-media-type').value='video';
  q('#post-youtube-title').value='Studio video';
  q('#post-publish').click();await flush();
  assert.equal(posts()[2].body.publishNow,true);
  assert.equal(posts()[2].body.platforms[0].platformSpecificData.title,'Studio video');
  assert.notEqual(posts()[2].idempotencyKey,posts()[0].idempotencyKey);

  q('[data-platform="telegram"]').click();
  assert.ok([...q('#post-account').options].some(o=>o.value==='telegram-account'));
  q('#post-account').value='telegram-account';
  q('#post-content').value='Approved announcement';
  q('#post-media-url').value='';
  q('#post-platform-options').value='{"parseMode":"HTML"}';
  q('#post-scheduled-for').value=new Date(Date.now()+86_400_000).toISOString().slice(0,16);
  q('#post-schedule').click();await flush();
  assert.ok(posts()[3].body.scheduledFor.endsWith('Z'));
  assert.equal(posts()[3].body.platforms[0].platformSpecificData.parseMode,'HTML');

  q('[data-platform="instagram"]').click();q('#post-account').value='instagram-account';
  q('#post-content').value='Approved image post';
  q('#post-media-type').value='image';q('#post-media-url').value='https://media.zernio.com/temp/image.png';
  q('#post-platform-options').value='';q('#post-publish').click();await flush();
  assert.equal(posts()[4].body.platforms[0].platform,'instagram');
  assert.equal(posts()[4].body.mediaItems[0].type,'image');
  assert.ok(posts().every(op=>op.provider==='zernio'));
  dom.window.close();
});

test('research is a lead, editorial report is unverified, and inbox failures block automation',async()=>{
  const {dom,calls,q}=fixture({engagementError:true});await flush();
  q('[data-go="intelligence"]').click();
  q('#research-scan').click();await flush();
  assert.match(q('#research-results').textContent,/New gameplay update/);
  q('#research-results .research-row').querySelectorAll('button')[1].click();
  assert.equal(q('#report-title').value,'New gameplay update');
  assert.equal(q('#report-primary-url').value,'','a lead is not an independently reviewed primary source');
  assert.equal(q('#report-support-url').value,'https://example.com/article');
  q('#report-facts').value='Original editorial summary, independently reviewed.';
  q('#report-primary-url').value='https://game.example.com/patch-notes';
  q('#report-save').click();await flush();
  const report=calls.find(op=>op.kind==='editorial'&&op.action==='save');
  assert.equal(report.report.verifiedBySoftware,undefined);
  assert.equal(report.report.sources[0].type,'original');
  q('[data-go="zernio"]').click();q('#refresh-access').click();await flush();
  q('#automation-account').value='instagram-account';
  q('#engagement-check').click();await flush();
  assert.match(q('#automation-result').textContent,/Inbox read failed/);
  q('#automation-name').value='Real reward';
  q('#automation-keyword').value='REHBER';
  q('#automation-target-id').value='a'.repeat(24);
  q('#automation-dm').value='Full guide link: https://bazino.pro/guides/example';
  q('#automation-create').click();await flush();
  assert.ok(!calls.some(op=>op.kind==='api'&&op.path==='/v1/comment-automations'));
  dom.window.close();
});

test('Explorer reuses a per-post retry key and sends it in the operation envelope',async()=>{
  const {dom,calls,q}=fixture();await flush();
  q('[data-preset="zernio-posts"]').click();q('#api-method').value='POST';
  q('#api-body').value=JSON.stringify({content:'test',platforms:[{platform:'instagram',accountId:'instagram-account'}],isDraft:true});
  q('#run-api').click();await flush();
  const post=()=>calls.filter(op=>op.kind==='api'&&op.path==='/v1/posts');
  assert.match(q('#api-idempotency').value,/^[a-f0-9-]{36}$/);
  assert.equal(post()[0].idempotencyKey,q('#api-idempotency').value);
  q('#run-api').click();await flush();assert.equal(post()[1].idempotencyKey,post()[0].idempotencyKey);
  q('#api-body').value=JSON.stringify({content:'changed',platforms:[{platform:'instagram',accountId:'instagram-account'}],isDraft:true});
  q('#run-api').click();await flush();assert.equal(post().length,2,'changed body with old key is blocked');
  dom.window.close();
});

test('keyword CTA guided flow permits a draft but not publication before scoped delivery is ready',async()=>{
  const report={id:'f4ce714b-f09f-4c91-9d19-c91b078fc20d',game:'Oyun',title:'Settings guide',rank:60,
    category:'guide',keyword:'AYAR',promised:'Prepared settings file',
    evidenceState:'notes_from_multiple_domains_need_review',posts:[],
    variants:{instagram:'Rehber yayında! AYAR',telegram:'Kaynaklı Türkçe bilgi'}};
  const {dom,calls,q}=fixture({reports:[report]});await flush();
  q('[data-go="zernio"]').click();q('#refresh-access').click();await flush();
  q('#post-account').value='instagram-account';q('#post-topic').value=report.id;
  q('#post-content').value='Original Turkish CTA copy';
  q('#post-media-url').value='https://media.zernio.com/temp/photo.png';
  q('#post-publish').click();await flush();
  assert.equal(calls.filter(op=>op.kind==='api'&&op.path==='/v1/posts').length,0);
  q('#post-draft').click();await flush();
  const draft=calls.find(op=>op.kind==='api'&&op.path==='/v1/posts');
  assert.equal(draft.body.isDraft,true);
  assert.equal(draft.body.metadata.bazinoTopicId,report.id);
  q('#automation-account').value='instagram-account';
  q('#engagement-check').click();await flush();
  q('#automation-name').value='AYAR guide';
  q('#automation-keyword').value='AYAR';
  q('#automation-target-id').value='a'.repeat(24);
  q('#automation-dm').value='Gerçek ayar listesi: https://bazino.pro/rehber';
  q('#automation-create').click();await flush();
  const automation=calls.find(op=>op.kind==='api'&&op.path==='/v1/comment-automations');
  assert.equal(automation.body.postId,'a'.repeat(24));
  assert.equal(automation.body.profileId,'brand-profile');
  assert.deepEqual(JSON.parse(JSON.stringify(automation.body.keywords)),['AYAR']);
  assert.equal(automation.body.alsoMatchInDms,false);
  dom.window.close();
});

test('owner UI registers API and MCP locally, clears token field, extracts a URL and builds scoped agent operations',async()=>{
  const {dom,calls,registered,logins,q}=fixture();await flush();
  q('[data-nav="gateway"]').click();
  assert.equal(q('#view-gateway').classList.contains('active'),true);
  q('#gateway-id').value='vendor-api';q('#gateway-name').value='Vendor API';
  q('#gateway-url').value='https://api.vendor.com/v1';
  q('#gateway-auth').value='bearer';q('#gateway-credential').value='OWNER_SECRET_123456';
  q('#gateway-agent-access').value='approved-writes';
  q('#gateway-register').dispatchEvent(new dom.window.Event('submit',{bubbles:true,cancelable:true}));
  await flush();
  assert.equal(registered[0].input.credential,'OWNER_SECRET_123456');
  assert.equal(q('#gateway-credential').value,'');
  assert.ok(!q('#gateway-list').textContent.includes('OWNER_SECRET_123456'));
  assert.equal(q('#gateway-api-select').options[1].value,'vendor-api');
  q('#web-url').value='https://news.vendor.com/article';q('#web-read').click();await flush();
  assert.equal(calls.find(op=>op.kind==='web-fetch').url,'https://news.vendor.com/article');
  q('#gateway-api-select').value='vendor-api';q('#gateway-api-method').value='POST';
  q('#gateway-api-path').value='/items';q('#gateway-api-body').value='{"topic":"games"}';
  q('#gateway-api-run').click();await flush();
  assert.equal(calls.find(op=>op.kind==='gateway-api').body.topic,'games');
  assert.equal(calls.find(op=>op.kind==='gateway-api').connectorId,'vendor-api');
  q('#gateway-new').click();
  q('#gateway-id').value='vendor-mcp';q('#gateway-name').value='Vendor MCP';
  q('#gateway-kind').value='mcp';q('#gateway-url').value='https://mcp.vendor.com/rpc';
  q('#gateway-auth').value='oauth';q('#gateway-agent-access').value='read';
  q('#gateway-register').dispatchEvent(new dom.window.Event('submit',{bubbles:true,cancelable:true}));await flush();
  assert.equal(q('#gateway-oauth').disabled,false);
  q('#gateway-oauth').click();await flush();assert.deepEqual(logins,['vendor-mcp']);
  q('#gateway-mcp-select').value='vendor-mcp';q('#gateway-mcp-action').value='call';
  q('#gateway-mcp-name').value='lookup';q('#gateway-mcp-args').value='{"q":"gaming"}';
  q('#gateway-mcp-run').click();await flush();
  const mcp=calls.find(op=>op.kind==='gateway-mcp');
  assert.equal(mcp.connectorId,'vendor-mcp');assert.equal(mcp.action,'call');
  assert.equal(mcp.args.q,'gaming');
  q('#gateway-remove').click();await flush();assert.equal(registered.length,1);
  dom.window.close();
});

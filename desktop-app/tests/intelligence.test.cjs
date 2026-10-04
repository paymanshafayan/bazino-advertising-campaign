'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const os=require('node:os');
const path=require('node:path');
const {Research,FEEDS,parseFeed}=require('../src/research.cjs');
const {EditorialStore,validateReport}=require('../src/editorial.cjs');
const {validateOperation,isMutating}=require('../src/protocol.cjs');
const storage={isEncryptionAvailable:()=>true,
  encryptString:text=>Buffer.from('ENCRYPTED:'+Buffer.from(text).toString('base64')),
  decryptString:b=>Buffer.from(b.toString().slice(10),'base64').toString()};

function sampleReport(overrides={}){
  return {title:'Oyun 4.1 güncellemesi',game:'Oyun',category:'update',rank:72,
    sources:[{type:'original',url:'https://game.example.com/notes',note:'Version 4.1 patch notes'},
      {type:'supporting',url:'https://merlininkazani.com/story',note:'Independent comparison'}],
    facts:'Original patch notes and version confirmed by an editor.',uncertainty:'Server rollout may vary.',
    variants:{instagram:'Türkçe özgün gönderi',telegram:'Türkçe özgün bilgi',blog:'Türkçe blog taslağı'},
    keyword:'AYAR',promised:'Prepared settings guide linked on the owner’s site',...overrides};
}

test('curated RSS/Atom feeds return only bounded, same-site headline leads, never fetch a supplied URL',async()=>{
  const feed=FEEDS.find(f=>f.id==='vgc');
  const rss=`<rss><channel><item><title><![CDATA[ New &amp; upcoming <b>games</b> ]]></title>
    <link>https://www.videogameschronicle.com/news/a</link><pubDate>Fri, 25 Sep 2026 12:00:00 GMT</pubDate></item>
    <item><title>Duplicate</title><link>https://www.videogameschronicle.com/news/a</link></item>
    <item><title>Wrong origin</title><link>https://evil.example.com/path</link></item>
    <item><title>Insecure</title><link>http://www.videogameschronicle.com/path</link></item>
    <item><title>Secrets in URL</title><link>https://www.videogameschronicle.com/news/b?token=hidden</link></item></channel></rss>`;
  const items=parseFeed(rss,feed);
  assert.equal(items.length,1);assert.equal(items[0].title,'New & upcoming games');
  assert.equal(items[0].status,'headline_only_not_verified');
  assert.equal(items[0].url,'https://www.videogameschronicle.com/news/a');
  assert.throws(()=>parseFeed('<!DOCTYPE rss [<!ENTITY x SYSTEM "file:///etc/passwd">]><rss></rss>',feed),/unsafe/);
  const atom=parseFeed('<feed><entry><title>Turkish guide</title><link href="https://www.videogameschronicle.com/guide"/></entry></feed>',feed);
  assert.equal(atom.length,1);
  const seen=[];
  const research=new Research({fetcher:async(url,options)=>{
    seen.push([url,options]);
    assert.equal(options.redirect,'manual');assert.ok(!options.headers.Authorization);
    return new Response(rss,{status:200});
  }});
  assert.equal((await research.scan(['vgc'])).sources[0].items.length,1);
  assert.equal(seen[0][0],feed.url);
  await assert.rejects(research.scan(['https://evil.example.com']),/Unknown or duplicate/);
  await assert.rejects(research.scan(['vgc','vgc']),/Unknown or duplicate/);
  assert.equal(seen.length,1);
});

test('feed errors are per-source and never become verified publication claims',async()=>{
  const research=new Research({fetcher:async(url)=>{
    if(url.includes('eurogamer'))return new Response(null,{status:302,headers:{Location:'https://elsewhere.example/'}});
    return new Response('<rss><channel><item><title>A lead</title><link>https://www.pcgamer.com/news/a</link></item></channel></rss>');
  }});
  const result=await research.scan(['eurogamer','pcgamer']);
  assert.equal(result.ok,true);
  assert.equal(result.sources[0].items.length,0);assert.match(result.sources[0].error,/HTTP 302/);
  assert.equal(result.sources[1].items.length,1);
  assert.match(result.note,/Headlines only/);
});

test('report metadata never verifies claims automatically, is encrypted at rest and tracks duplicate post IDs',()=>{
  const input=sampleReport();
  assert.equal(validateReport(input).evidenceState,'notes_from_multiple_domains_need_review');
  assert.equal(validateReport(input).verifiedBySoftware,false);
  assert.equal(validateReport(sampleReport({sources:input.sources.map(s=>({...s,note:''}))})).evidenceState,
    'needs_independent_sources');
  assert.throws(()=>validateReport(sampleReport({promised:''})),/promised value/);
  assert.throws(()=>validateReport(sampleReport({sources:[{type:'original',url:'http://127.0.0.1',note:'secret'}]})),
    /public HTTPS/);
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'editorial-test-'));
  try{
    const file=path.join(dir,'reports.json');
    const store=new EditorialStore(file,storage);const row=store.upsert(input);
    assert.equal(store.list()[0].title,input.title);
    assert.ok(!fs.readFileSync(file,'utf8').includes(input.title));
    assert.ok(!fs.readFileSync(file,'utf8').includes(input.promised));
    assert.throws(()=>store.upsert(input),/already exists/);
    const post={_id:'a'.repeat(24),status:'draft'};
    store.recordPost(row.id,post,[{platform:'instagram',accountId:'account1'}]);
    store.recordPost(row.id,post,[{platform:'instagram',accountId:'account1'}]);
    assert.equal(store.list()[0].posts.length,1);
    assert.equal(store.list()[0].posts[0].platforms[0].platform,'instagram');
    assert.equal(new EditorialStore(file,storage).list()[0].posts.length,1);
    assert.equal(store.upsert({...input,id:row.id,rank:85}).rank,85);
    assert.equal(store.list()[0].posts.length,1);
  }finally{fs.rmSync(dir,{recursive:true,force:true});}
});

test('new read operations remain read-only, editor save and post promotion are classified as writes',()=>{
  const research=validateOperation({kind:'research',action:'scan',sourceIds:['vgc']});
  const reports=validateOperation({kind:'editorial',action:'list'});
  const access=validateOperation({kind:'zernio-access'});
  assert.equal(isMutating(research),false);assert.equal(isMutating(reports),false);
  assert.equal(isMutating(access),false);
  assert.equal(isMutating(validateOperation({kind:'editorial',action:'save',report:sampleReport()})),true);
  const post='a'.repeat(24);
  assert.equal(isMutating(validateOperation({kind:'api',provider:'zernio',method:'PUT',path:`/v1/posts/${post}`,
    body:{isDraft:false,scheduledFor:'2099-01-01T12:00:00Z'}})),true);
});

'use strict';

async function readBounded(response,max) {
  if(Number(response.headers?.get?.('content-length')||0)>max)throw new Error('Feed exceeds size limit');
  const chunks=[];let bytes=0;
  if(!response.body?.getReader) {
    const buf=Buffer.from(await response.arrayBuffer());
    if(buf.length>max)throw new Error('Feed exceeds size limit');
    return buf;
  }
  const reader=response.body.getReader();
  while(true) {
    const {value,done}=await reader.read();if(done)break;
    bytes+=value.length;
    if(bytes>max){await reader.cancel();throw new Error('Feed exceeds size limit');}
    chunks.push(value);
  }
  return Buffer.concat(chunks,bytes);
}

// Deliberately fixed, public RSS endpoints; no arbitrary URL fetching, API keys or cookies.
// Headlines are research leads, NOT a claim that the underlying articles were read or verified.
const FEEDS=Object.freeze([
  {id:'eurogamer',name:'Eurogamer',region:'global',url:'https://www.eurogamer.net/feed',host:'eurogamer.net'},
  {id:'pcgamer',name:'PC Gamer',region:'global',url:'https://www.pcgamer.com/rss/?feed=rss',host:'pcgamer.com'},
  {id:'vgc',name:'VGC',region:'global',url:'https://www.videogameschronicle.com/feed/',host:'videogameschronicle.com'},
  {id:'merlin',name:"Merlin’in Kazanı",region:'turkish',url:'https://www.merlininkazani.com/feed/',host:'merlininkazani.com'},
  {id:'playstation',name:'PlayStation Blog (official)',region:'primary',url:'https://blog.playstation.com/feed/',host:'blog.playstation.com'}
]);
function decode(value) {
  return String(value||'').replace(/<!\[CDATA\[([\s\S]*?)\]\]>/gi,'$1')
    .replace(/<[^>]*>/g,' ').replace(/&(#x[0-9a-f]+|#[0-9]+|amp|lt|gt|quot|apos|nbsp);/gi,(_all,entity)=>{
      if(entity[0]==='#') {
        const code=entity[1].toLowerCase()==='x'?parseInt(entity.slice(2),16):parseInt(entity.slice(1),10);
        return code>0 && code<=0x10ffff ? String.fromCodePoint(code) : '';
      }
      return {amp:'&',lt:'<',gt:'>',quot:'"',apos:"'",nbsp:' '}[entity.toLowerCase()];
    }).replace(/\s+/g,' ').trim();
}
function tag(xml,name) {
  const match=xml.match(new RegExp(`<${name}(?:\\s[^>]*)?>([\\s\\S]*?)<\\/${name}\\s*>`,'i'));
  return match?decode(match[1]):'';
}
function entryUrl(xml,atom) {
  if(!atom)return tag(xml,'link');
  const link=xml.match(/<link\b(?=[^>]*\bhref\s*=)[^>]*\bhref\s*=\s*(["'])(.*?)\1[^>]*\/?\s*>/i);
  return link?decode(link[2]):tag(xml,'link');
}
function safeArticle(url,feed) {
  try {
    const parsed=new URL(url);
    const host=parsed.hostname.toLowerCase().replace(/^www\./,'');
    if(parsed.protocol!=='https:' || parsed.username || parsed.password || parsed.port ||
      host!==feed.host.replace(/^www\./,'') || /(?:code|token|secret|key|auth)/i.test(parsed.search))return null;
    parsed.hash='';return parsed.toString();
  } catch {return null;}
}
function parseFeed(xml,feed,limit=12) {
  if(typeof xml!=='string'||xml.length>600_000||/<!DOCTYPE\b|<!ENTITY\b/i.test(xml) ||
    !/<(?:rss|feed)\b/i.test(xml))throw new Error('Invalid or unsafe RSS/Atom feed');
  const atom=/<feed\b/i.test(xml) && !/<rss\b/i.test(xml);
  const blocks=xml.match(atom?/<entry\b[^>]*>[\s\S]*?<\/entry\s*>/gi:/<item\b[^>]*>[\s\S]*?<\/item\s*>/gi)||[];
  const results=[],seen=new Set();
  for(const block of blocks.slice(0,60)) {
    const title=tag(block,'title').slice(0,240);
    const url=safeArticle(entryUrl(block,atom),feed);
    if(!url || !title || seen.has(url))continue;
    seen.add(url);
    const date=tag(block,atom?'updated':'pubDate') || tag(block,atom?'published':'dc:date');
    const stamp=Date.parse(date);
    results.push({source:feed.id,sourceName:feed.name,region:feed.region,title,url,
      publishedAt:Number.isFinite(stamp)?new Date(stamp).toISOString():null,
      status:'headline_only_not_verified'});
    if(results.length>=limit)break;
  }
  return results;
}
class Research {
  constructor({fetcher=globalThis.fetch,timeoutMs=12000}={}) {this.fetcher=fetcher;this.timeoutMs=timeoutMs;}
  async scan(ids) {
    const requested=ids?.length?ids:FEEDS.map(s=>s.id);
    if(!Array.isArray(requested)||requested.length>FEEDS.length||new Set(requested).size!==requested.length||
      requested.some(id=>!FEEDS.some(s=>s.id===id)))throw new Error('Unknown or duplicate public feed');
    const results=await Promise.all(requested.map(async id=>{
      const feed=FEEDS.find(s=>s.id===id);
      try {
        const response=await this.fetcher(feed.url,{method:'GET',redirect:'manual',
          headers:{Accept:'application/rss+xml, application/atom+xml, application/xml, text/xml'},
          signal:AbortSignal.timeout(this.timeoutMs)});
        if(!response.ok||response.status>=300 && response.status<400)
          throw new Error(`Feed returned HTTP ${response.status}`);
        const xml=(await readBounded(response,512*1024)).toString('utf8');
        return {source:id,items:parseFeed(xml,feed),error:null};
      } catch(e) {return {source:id,items:[],error:String(e.message||'Feed unavailable').slice(0,120)};}
    }));
    return {ok:results.some(r=>!r.error),observedAt:new Date().toISOString(),
      note:'Headlines only. Read the original article and an independent source before asserting facts.',
      sources:results};
  }
}
module.exports={FEEDS,Research,parseFeed,safeArticle};

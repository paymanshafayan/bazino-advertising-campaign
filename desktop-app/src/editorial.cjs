'use strict';

const fs=require('node:fs');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {isIP}=require('node:net');

const TYPES=new Set(['news','update','guide','community','event','other']);
const UUID=/^[a-f0-9-]{36}$/i;
function bounded(value,max=1000) {
  if(typeof value!=='string'||value.length>max)throw new Error('Invalid or oversized editorial text');
  return value.trim();
}
function source(input) {
  if (!input || typeof input!=='object' || Array.isArray(input))throw new Error('Source must include a public URL and research note');
  const raw=bounded(input.url,700);
  if(!raw)return null;
  const url=new URL(raw);
  if(url.protocol!=='https:'||!url.hostname.includes('.')||isIP(url.hostname)||url.username||url.password||
    url.port||/(?:^|\.)(?:local|internal|localhost)$/.test(url.hostname) ||
    /(?:code|token|secret|key|auth)/i.test(url.search))throw new Error('Only public HTTPS source links without credential parameters are allowed');
  url.hash='';
  return {url:url.toString(),note:bounded(input.note||'',900),
    type:input.type==='original'?'original':'supporting'};
}
function validateReport(input) {
  if(!input||typeof input!=='object'||Array.isArray(input))throw new Error('Expected an editorial report');
  const title=bounded(input.title,180),game=bounded(input.game||'',100);
  if(!title)throw new Error('A research topic needs a title');
  if(!Array.isArray(input.sources)||input.sources.length>8)throw new Error('Use at most eight concise source records');
  const sources=input.sources.map(source).filter(Boolean);
  const hosts=new Set(sources.filter(s=>s.note).map(s=>new URL(s.url).hostname.replace(/^www\./,'')));
  const rank=Number(input.rank||0);
  if(!Number.isInteger(rank)||rank<0||rank>100)throw new Error('Topic priority must be 0-100');
  const category=TYPES.has(input.category)?input.category:'other';
  const variants=input.variants||{};
  if(!variants||typeof variants!=='object'||Array.isArray(variants))throw new Error('Language variants must be an object');
  const copy=Object.fromEntries(['instagram','telegram','blog','facebook','tiktok','youtube']
    .map(key=>[key,bounded(variants[key]||'',4000)]));
  const keyword=bounded(input.keyword||'',40);
  const promised=bounded(input.promised||'',1000);
  if(keyword && !promised)throw new Error('Comment-to-unlock needs the promised value prepared before using a keyword');
  return {title,game,category,rank,sources,
    facts:bounded(input.facts||'',4000),uncertainty:bounded(input.uncertainty||'',1500),
    variants:copy,keyword,promised,
    evidenceState:hosts.size>=2 && sources.some(s=>s.type==='original'&&s.note) &&
      bounded(input.facts||'',4000) ? 'notes_from_multiple_domains_need_review' : 'needs_independent_sources',
    // Source URLs/notes are author-submitted; software has NOT read the articles or verified claims.
    verifiedBySoftware:false};
}
class EditorialStore {
  constructor(file,safeStorage) {this.file=file;this.safeStorage=safeStorage;this.rows=[];this.load();}
  load() {
    if(!this.safeStorage?.isEncryptionAvailable())throw new Error('Local report encryption is unavailable');
    if(!fs.existsSync(this.file))return;
    const payload=JSON.parse(fs.readFileSync(this.file,'utf8'));
    if(payload.v!==1||typeof payload.ciphertext!=='string')throw new Error('Unknown research report format');
    const value=JSON.parse(this.safeStorage.decryptString(Buffer.from(payload.ciphertext,'base64')));
    if(!Array.isArray(value)||value.length>120)throw new Error('Invalid encrypted research reports');
    this.rows=value;
  }
  persist() {
    fs.mkdirSync(path.dirname(this.file),{recursive:true,mode:0o700});
    const json=JSON.stringify(this.rows);
    if(Buffer.byteLength(json)>1024*1024)throw new Error('Research report limit reached; archive on your own computer first');
    const tmp=`${this.file}.${process.pid}.${randomUUID()}.tmp`;
    fs.writeFileSync(tmp,JSON.stringify({v:1,ciphertext:this.safeStorage.encryptString(json).toString('base64')}),
      {mode:0o600,flag:'wx'});
    fs.renameSync(tmp,this.file);
  }
  list() {return this.rows.map(row=>({...row}));}
  upsert(input) {
    const report=validateReport(input);
    const old=input.id && UUID.test(input.id) ? this.rows.find(row=>row.id===input.id) : null;
    const titleKey=`${report.game}:${report.title}`.toLocaleLowerCase('tr');
    if(!old && this.rows.some(row=>`${row.game}:${row.title}`.toLocaleLowerCase('tr')===titleKey))
      throw new Error('This topic already exists; update it rather than publishing duplicate news');
    const row={...report,id:old?.id||randomUUID(),createdAt:old?.createdAt||new Date().toISOString(),
      updatedAt:new Date().toISOString(),posts:old?.posts||[]};
    this.rows=old?this.rows.map(item=>item.id===old.id?row:item):[row,...this.rows].slice(0,100);
    this.persist();return row;
  }
  recordPost(topicId,post,requestedPlatforms=[]) {
    if(!UUID.test(topicId)||!post||typeof post._id!=='string')return;
    const row=this.rows.find(item=>item.id===topicId);
    if(!row)return;
    if(row.posts.some(item=>item.id===post._id))return;
    const destinations=Array.isArray(post.platforms)&&post.platforms.length?post.platforms:requestedPlatforms;
    row.posts.push({id:post._id,status:String(post.status||'unknown').slice(0,40),
      platforms:Array.isArray(destinations)?destinations.slice(0,16).map(p=>({
        platform:String(p.platform||'').slice(0,40),status:String(p.status||post.status||'unknown').slice(0,40)})):[],
      observedAt:new Date().toISOString()});
    row.updatedAt=new Date().toISOString();this.persist();
  }
}
module.exports={EditorialStore,validateReport};

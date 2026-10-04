'use strict';

// Deliberately text-only. No JavaScript, external resources, cookies, forms,
// browser automation, or HTML rendering from untrusted websites.
const {parseDocument}=require('htmlparser2');
const BLOCKS=new Set(['p','div','section','article','main','br','li','ul','ol','h1','h2','h3','h4','h5','h6','blockquote','tr']);
const SKIP=new Set(['script','style','noscript','template','svg','canvas','iframe','nav','footer','header','aside','form']);
function find(node,predicate,found=[]){
  if(predicate(node))found.push(node);
  for(const child of node.children||[])find(child,predicate,found);
  return found;
}
function plain(node){
  if(SKIP.has(node.name))return '';
  if(node.type==='text')return node.data||'';
  if(node.type==='comment')return '';
  const content=(node.children||[]).map(plain).join('');
  return BLOCKS.has(node.name)?`\n${content}\n`:content;
}
function tidy(text){
  return String(text).replace(/\u00a0/g,' ').replace(/[\t \f\v]+/g,' ')
    .replace(/\s*\n\s*/g,'\n').replace(/\n{3,}/g,'\n\n').trim();
}
function decode(bytes,contentType){
  const label=/charset\s*=\s*["']?([a-z0-9._-]+)/i.exec(contentType)?.[1]||'utf-8';
  try{return new TextDecoder(label).decode(bytes);}catch{return new TextDecoder('utf-8').decode(bytes);}
}
function extractText(bytes,contentType){
  const type=contentType.toLowerCase().split(';')[0].trim();
  const source=decode(bytes,contentType);
  if(source.includes('\0'))throw new Error('Binary content cannot be read as a webpage');
  if(type==='text/html'||type==='application/xhtml+xml'||(!type&&/<(?:html|article|body)\b/i.test(source))){
    const tree=parseDocument(source,{decodeEntities:true});
    const title=tidy(find(tree,node=>node.name==='title')[0]?.children?.map(plain).join('')||'').slice(0,240);
    const metas=find(tree,node=>node.name==='meta');
    const description=tidy(metas.find(node=>['description','og:description'].includes(
      (node.attribs?.name||node.attribs?.property||'').toLowerCase()))?.attribs?.content||'').slice(0,500);
    const candidates=find(tree,node=>['article','main'].includes(node.name));
    const body=find(tree,node=>node.name==='body')[0]||tree;
    const ranked=candidates.map(node=>tidy(plain(node))).sort((a,b)=>b.length-a.length);
    const text=ranked[0]?.length>=120?ranked[0]:tidy(plain(body));
    return {title,description,text:text.slice(0,32000),format:'html'};
  }
  if(type==='application/json'||type.endsWith('+json')){
    let value;
    try{value=JSON.parse(source);}catch{throw new Error('Remote response is not valid JSON');}
    return {title:'',description:'',text:JSON.stringify(value,null,2).slice(0,32000),format:'json'};
  }
  if(type.startsWith('text/')||type==='application/xml'||type.endsWith('+xml')){
    return {title:'',description:'',text:tidy(source).slice(0,32000),format:'text'};
  }
  throw new Error('Only HTML, JSON, XML and plain-text pages can be extracted; binary/PDF needs a separate reviewed reader');
}
module.exports={extractText};

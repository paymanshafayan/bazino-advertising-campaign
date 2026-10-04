'use strict';

// A gateway must never become a proxy into localhost, the owner's LAN, cloud
// metadata, or a different host after DNS rebinding/redirects. Apply the lookup
// policy at socket-creation time (not as a one-time preflight DNS query).
const dns=require('node:dns');
const {isIP}=require('node:net');
const {Agent,fetch:undiciFetch}=require('undici');
const ipaddr=require('ipaddr.js');

const UNSAFE_LABEL=/(?:^|\.)(?:localhost|local|internal|intranet|lan|home|onion|corp)(?:\.|$)|\.(?:test|invalid|example)$/i;
const SENSITIVE_QUERY=/(?:^|[_-])(?:key|token|secret|password|pass|auth|code|credential|signature|sig|session)(?:$|[_-])|(?:apiKey|accessToken|refreshToken|clientSecret|secretKey|authorization)/i;

function publicUrl(raw,{allowQuery=true,allowSensitiveQuery=false}={}){
  if(typeof raw!=='string'||raw.length>2048||raw.length<10)throw new Error('A short public HTTPS URL is required');
  let url;
  try{url=new URL(raw);}catch{throw new Error('Invalid public URL');}
  const host=url.hostname.replace(/^\[|\]$/g,'');
  if(url.protocol!=='https:'||url.username||url.password||url.port||url.hash||
    isIP(host)||host.endsWith('.')||!host.includes('.')||UNSAFE_LABEL.test(host)||
    !/^[a-z0-9.-]+$/i.test(host)||url.href.includes('\\'))
    throw new Error('Only public HTTPS hostnames on port 443, without credentials or fragments, are allowed');
  if(!allowQuery&&url.search)throw new Error('Connector endpoints must not contain query parameters');
  if(!allowSensitiveQuery&&[...url.searchParams.keys()].some(k=>SENSITIVE_QUERY.test(k)))
    throw new Error('Never place a token, key or authorization code in a URL');
  return url;
}
function publicAddress(address){
  try {
    let ip=ipaddr.parse(address);
    if(ip.kind()==='ipv6'){
      if(ip.isIPv4MappedAddress())ip=ip.toIPv4Address();
      else if(ip.match(ipaddr.parse('64:ff9b:1::'),48)||ip.match(ipaddr.parse('::'),96))
        return false; // RFC 8215 NAT64 and IPv4-compatible forms can tunnel private IPv4.
    }
    return ip.range()==='unicast';
  }catch{return false;}
}
function createPublicLookup(resolve=dns.lookup){
  return function lookup(host,options,callback){
    // net.connect/Undici call this on EVERY new socket, closing the TOCTOU gap.
    const opts=typeof options==='object'&&options?options:{};
    resolve(host,{all:true,family:opts.family||0,verbatim:true},(error,records)=>{
      if(error){callback(error);return;}
      if(!Array.isArray(records)||!records.length||records.some(r=>!publicAddress(r.address))){
        callback(new Error('DNS resolution includes a private, reserved or invalid address'));return;
      }
      if(opts.all)callback(null,records);
      else callback(null,records[0].address,records[0].family);
    });
  };
}
class PublicTransport {
  constructor({fetcher=undiciFetch,resolve=dns.lookup,timeoutMs=25000,maxBytes=2*1024*1024}={}){
    this.fetcher=fetcher;
    this.timeoutMs=timeoutMs;this.maxBytes=maxBytes;
    this.dispatcher=new Agent({connect:{lookup:createPublicLookup(resolve),timeout:10000},
      connections:4,headersTimeout:timeoutMs,bodyTimeout:timeoutMs});
    this.fetch=this.fetch.bind(this);
  }
  async fetch(input,init={}){
    const url=publicUrl(String(input));
    // Fetch implementations may follow redirects by default. Never forward auth or
    // read an unvalidated second URL, including 30x to an internal IP.
    const signal=init.signal?
      AbortSignal.any([init.signal,AbortSignal.timeout(this.timeoutMs)]):
      AbortSignal.timeout(this.timeoutMs);
    const response=await this.fetcher(url.toString(),{...init,dispatcher:this.dispatcher,
      redirect:'manual',signal});
    if(response.status>=300&&response.status<400){
      await response.body?.cancel?.().catch(()=>{});
      throw new Error(`Redirect refused (HTTP ${response.status}); no other host was contacted`);
    }
    if(Number(response.headers?.get?.('content-length')||0)>this.maxBytes){
      await response.body?.cancel?.().catch(()=>{});
      throw new Error('Public HTTP response exceeds the gateway size limit');
    }
    // Limit streaming bodies too (MCP clients may consume their own responses).
    if(!response.body?.getReader||[204,205,304].includes(response.status))return response;
    const reader=response.body.getReader(),max=this.maxBytes;
    let total=0;
    const stream=new ReadableStream({
      async pull(controller){
        try{
          const {done,value}=await reader.read();
          if(done){controller.close();return;}
          total+=value.byteLength;
          if(total>max){await reader.cancel();throw new Error('Public HTTP response exceeds the gateway size limit');}
          controller.enqueue(value);
        }catch(e){controller.error(e);}
      },
      cancel(reason){return reader.cancel(reason);}
    });
    return new Response(stream,{status:response.status,statusText:response.statusText,headers:response.headers});
  }
  async close(){await this.dispatcher.close();}
}
module.exports={publicUrl,publicAddress,createPublicLookup,PublicTransport};

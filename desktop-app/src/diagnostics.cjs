'use strict';

// Copyable diagnostics deliberately contain ONLY fixed codes, bounded numbers and
// timestamps. Never put exception messages, URLs, request/response bodies, account
// names, OAuth data, session headers or Vault values into this log.
const BUILD_ID='studio-logs-2026-09-26.1';
const COMPONENTS=new Set(['server','operation','relay','asset']);
const CODES=new Set(['SERVER_READY','REQUEST_DONE','REQUEST_FAILED','OP_STARTED','OP_DONE',
  'OP_PARTIAL','OP_FAILED','OP_DECLINED','RELAY_CONNECTED','RELAY_DISCONNECTED',
  'RELAY_FAILED','ASSET_IMPORT_DONE','ASSET_IMPORT_FAILED']);
const KINDS=new Set(['api','zernio-access','zernio-engagement-check','zernio-media-upload',
  'research','editorial','web-fetch','gateway-list','gateway-api','gateway-mcp','kling']);
const ACTIONS=new Set(['startup','settings:save','gateway:list','gateway:save','gateway:remove',
  'gateway:authorize','operation:run','relay:connect','relay:disconnect','relay:pair',
  'assets:open','kling:authorize','kling:identity','kling:tools','kling:logout',
  'external:open','app:stop','events','diagnostics']);
const NETWORK_CODES=new Set(['ECONNRESET','ECONNREFUSED','ENOTFOUND','EAI_AGAIN','ETIMEDOUT',
  'ENETUNREACH','EHOSTUNREACH','UND_ERR_CONNECT_TIMEOUT','UND_ERR_SOCKET',
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE','SELF_SIGNED_CERT_IN_CHAIN','CERT_HAS_EXPIRED',
  'ERR_TLS_CERT_ALTNAME_INVALID']);
const HTTP_CODES=new Set([400,401,403,404,408,409,429,500,502,503,504]);
const REASONS=new Set(['NONE','OTHER','NETWORK','AUTH','DECLINED',
  ...[...NETWORK_CODES].map(code=>`NET_${code}`),
  ...[...HTTP_CODES].map(status=>`HTTP_${status}`)]);

function failureReason(error){
  let current=error;
  for(let i=0;i<4&&current&&typeof current==='object';i++,current=current.cause){
    if(NETWORK_CODES.has(current.code))return `NET_${current.code}`;
  }
  const message=String(error?.message||'').slice(0,1000);
  const status=/(?:GitHub|HTTP|status)\s*[:( ]*([1-5][0-9]{2})/i.exec(message);
  if(status&&HTTP_CODES.has(Number(status[1])))return `HTTP_${status[1]}`;
  if(/fetch failed|network|unreachable|timed out|timeout/i.test(message))return 'NETWORK';
  if(/OAuth|not authorized|unauthorized|session is not active/i.test(message))return 'AUTH';
  if(/DECLINED_BY_OWNER|declined/i.test(message))return 'DECLINED';
  return 'OTHER';
}
function createDiagnostics({now=()=>new Date(),limit=120}={}){
  const rows=[];let seq=0;
  function add(component,code,{kind,action,status,reason}={}){
    const row={seq:++seq,at:now().toISOString(),
      component:COMPONENTS.has(component)?component:'server',
      code:CODES.has(code)?code:'REQUEST_FAILED'};
    if(KINDS.has(kind))row.kind=kind;
    if(ACTIONS.has(action))row.action=action;
    if(Number.isInteger(status)&&status>=100&&status<=599)row.status=status;
    if(reason)row.reason=REASONS.has(reason)?reason:'OTHER';
    rows.push(row);
    if(rows.length>limit)rows.splice(0,rows.length-limit);
    return row;
  }
  return {add,list:()=>rows.map(row=>({...row}))};
}
module.exports={BUILD_ID,createDiagnostics,failureReason};

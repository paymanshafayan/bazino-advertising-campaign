'use strict';

const { generateKeyPairSync, createPublicKey, createPrivateKey, diffieHellman,
  hkdfSync, randomBytes, createCipheriv, createDecipheriv, createHash, sign, verify } = require('node:crypto');
const { MAX_REQUEST_BYTES, MAX_RESPONSE_BYTES } = require('./constants.cjs');

function newIdentity() {
  const { publicKey, privateKey } = generateKeyPairSync('x25519');
  return {
    publicKey: publicKey.export({ type: 'spki', format: 'pem' }),
    privateKey: privateKey.export({ type: 'pkcs8', format: 'pem' })
  };
}
function fingerprint(publicKeyPem) {
  const der = createPublicKey(publicKeyPem).export({ type: 'spki', format: 'der' });
  return createHash('sha256').update(der).digest('hex').slice(0, 32);
}
function derivation(secret, salt) {
  return Buffer.from(hkdfSync('sha256', secret, salt, Buffer.from('bazino-marketing-relay-v1'), 32));
}
function seal(value, recipientPublicKeyPem, context = '', maxBytes = MAX_REQUEST_BYTES) {
  const raw = Buffer.from(JSON.stringify(value), 'utf8');
  if (!raw.length || raw.length > maxBytes) throw new Error('Encrypted payload exceeds configured size limit');
  const ephemeral = newIdentity();
  const shared = diffieHellman({ privateKey: createPrivateKey(ephemeral.privateKey), publicKey: createPublicKey(recipientPublicKeyPem) });
  const salt = randomBytes(32), nonce = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', derivation(shared, salt), nonce);
  cipher.setAAD(Buffer.from(String(context), 'utf8'));
  const encrypted = Buffer.concat([cipher.update(raw), cipher.final()]);
  return {
    algorithm: 'x25519-hkdf-sha256-aes256gcm',
    publicKey: ephemeral.publicKey,
    salt: salt.toString('base64'), nonce: nonce.toString('base64'),
    ciphertext: encrypted.toString('base64'), tag: cipher.getAuthTag().toString('base64')
  };
}
function unseal(envelope, recipientPrivateKeyPem, context = '') {
  if (!envelope || envelope.algorithm !== 'x25519-hkdf-sha256-aes256gcm') throw new Error('Unknown encrypted envelope');
  if (typeof envelope.ciphertext !== 'string' || envelope.ciphertext.length > MAX_RESPONSE_BYTES * 2) throw new Error('Envelope too large');
  const salt = Buffer.from(envelope.salt, 'base64');
  const nonce = Buffer.from(envelope.nonce, 'base64');
  const tag = Buffer.from(envelope.tag, 'base64');
  if (salt.length !== 32 || nonce.length !== 12 || tag.length !== 16) throw new Error('Invalid envelope parameters');
  const shared = diffieHellman({ privateKey: createPrivateKey(recipientPrivateKeyPem), publicKey: createPublicKey(envelope.publicKey) });
  const decipher = createDecipheriv('aes-256-gcm', derivation(shared, salt), nonce);
  decipher.setAAD(Buffer.from(String(context), 'utf8'));
  decipher.setAuthTag(tag);
  const bytes = Buffer.concat([decipher.update(Buffer.from(envelope.ciphertext, 'base64')), decipher.final()]);
  if (bytes.length > MAX_RESPONSE_BYTES) throw new Error('Response too large');
  return JSON.parse(bytes.toString('utf8'));
}
function combinedFingerprint(encryptionPublicKey,signingPublicKey){
  return createHash('sha256').update(fingerprint(encryptionPublicKey)+fingerprint(signingPublicKey)).digest('hex').slice(0,32);
}
function newSigningIdentity() {
  const {publicKey,privateKey}=generateKeyPairSync('ed25519');
  return {publicKey:publicKey.export({type:'spki',format:'pem'}),
    privateKey:privateKey.export({type:'pkcs8',format:'pem'})};
}
function signedBytes(request) {
  // Fixed property order, independent of caller object insertion order.
  return Buffer.from(JSON.stringify([request.v,request.id,request.issuedAt,request.expiresAt,
    request.replyPublicKey,request.payload]),'utf8');
}
function signRequest(request,privateKey) {
  return sign(null,signedBytes(request),createPrivateKey(privateKey)).toString('base64');
}
function verifyRequest(request,publicKey) {
  if (typeof request.signature!=='string' || request.signature.length>200) return false;
  try {return verify(null,signedBytes(request),createPublicKey(publicKey),Buffer.from(request.signature,'base64'));}
  catch{return false;}
}
function replyBytes(reply){return Buffer.from(JSON.stringify([reply.v,reply.id,reply.createdAt,reply.payload]),'utf8');}
function signReply(reply,privateKey){return sign(null,replyBytes(reply),createPrivateKey(privateKey)).toString('base64');}
function verifyReply(reply,publicKey){
  if(!reply||typeof reply.signature!=='string'||reply.signature.length>200)return false;
  try{return verify(null,replyBytes(reply),createPublicKey(publicKey),Buffer.from(reply.signature,'base64'));}
  catch{return false;}
}
module.exports = { newIdentity, fingerprint, combinedFingerprint, seal, unseal,
  newSigningIdentity, signRequest, verifyRequest, signReply, verifyReply };

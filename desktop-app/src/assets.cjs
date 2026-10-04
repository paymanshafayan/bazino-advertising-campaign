'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const EXTENSIONS = new Set(['.png','.jpg','.jpeg','.webp','.mp4','.mov','.m4v','.pdf']);
const MIME = { '.png':'image/png','.jpg':'image/jpeg','.jpeg':'image/jpeg','.webp':'image/webp',
  '.mp4':'video/mp4','.mov':'video/quicktime','.m4v':'video/x-m4v','.pdf':'application/pdf' };
const MAX_BYTES = 512 * 1024 * 1024;

class Assets {
  constructor(root) { this.root = root; }
  list() {
    try { return JSON.parse(fs.readFileSync(path.join(this.root,'index.json'),'utf8')).filter(item => {
      try { this.find(item.id); return true; } catch { return false; }
    }); } catch { return []; }
  }
  remember(item) {
    fs.mkdirSync(this.root,{recursive:true,mode:0o700});
    const items=this.list(); items.unshift(item);
    const tmp=path.join(this.root,`index.${process.pid}.tmp`);
    fs.writeFileSync(tmp,JSON.stringify(items.slice(0,500)),{mode:0o600});
    fs.renameSync(tmp,path.join(this.root,'index.json'));
    return item;
  }
  import(localFile, displayName = path.basename(localFile)) {
    const ext = path.extname(localFile).toLowerCase();
    if(typeof displayName!=='string' || displayName.length>200 ||
      path.basename(displayName)!==displayName || path.extname(displayName).toLowerCase()!==ext)
      throw new Error('Invalid asset display name');
    if (!EXTENSIONS.has(ext)) throw new Error('Unsupported media type');
    const stat = fs.statSync(localFile);
    if (!stat.isFile() || stat.size < 1 || stat.size > MAX_BYTES) throw new Error('Asset must be a nonempty file no larger than 512 MiB');
    fs.mkdirSync(this.root, { recursive: true, mode: 0o700 });
    const id = randomUUID();
    fs.copyFileSync(localFile, path.join(this.root, `${id}${ext}`), fs.constants.COPYFILE_EXCL);
    return this.remember({ id, name: displayName, bytes: stat.size, mime: MIME[ext] });
  }
  find(id) {
    if (typeof id !== 'string' || !/^[a-f0-9-]{36}$/i.test(id)) throw new Error('Invalid asset ID');
    for (const ext of EXTENSIONS) {
      const p = path.join(this.root, `${id}${ext}`);
      if (fs.existsSync(p) && fs.statSync(p).isFile()) return { path: p, mime: MIME[ext], bytes: fs.statSync(p).size };
    }
    throw new Error('Imported asset not found on this computer');
  }
  saveImage(buffer, mime) {
    const ext = String(mime).includes('jpeg') ? '.jpg' : '.png';
    if (!Buffer.isBuffer(buffer) || buffer.length > MAX_BYTES || !buffer.length) throw new Error('Invalid generated image');
    fs.mkdirSync(this.root, { recursive: true, mode: 0o700 });
    const id = randomUUID();
    fs.writeFileSync(path.join(this.root, `${id}${ext}`), buffer, { mode: 0o600, flag: 'wx' });
    return this.remember({ id, name:`Generated image ${id.slice(0,8)}`, mime:MIME[ext], bytes:buffer.length });
  }
}
module.exports = { Assets };

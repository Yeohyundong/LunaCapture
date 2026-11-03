import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { handleFeedback, validate } from './core.mjs';
function database() {
  const db = new DatabaseSync(':memory:');
  db.exec(readFileSync(new URL('../../../drizzle/0000_wandering_moonstone.sql', import.meta.url), 'utf8').replaceAll('--> statement-breakpoint', ''));
  return { prepare(sql) { return { bind(...args) { return { async first() { return db.prepare(sql).get(...args) ?? null; }, async run() { return db.prepare(sql).run(...args); } }; } }; } };
}
const payload = (n = 1) => ({content:'복잡한 배경에서 선이 잘 안 보여요',replyTo:null,version:'1.2.2',requestId:n.toString(16).padStart(32,'0')});
const request = (body, headers={}) => new Request('https://example.com/api/feedback',{method:'POST',headers:{'content-type':'application/json','CF-Connecting-IP':'127.0.0.1',...headers},body:JSON.stringify(body)});
test('validation: optional email, length, and invalid fields',()=>{
  assert.equal(validate(payload()),true);
  assert.equal(validate({...payload(),replyTo:'hello@example.com'}),true);
  for(const change of [{content:''},{content:'x'.repeat(4001)},{replyTo:'bad'},{replyTo:'a@b.com\nother'},{requestId:'x'},{version:null}]) assert.equal(validate({...payload(),...change}),false);
});
test('durable save, optional reply email, idempotent retry and conflict',async()=>{
  const env={DB:database()};
  assert.equal((await handleFeedback(request(payload()),env)).status,200);
  assert.equal((await handleFeedback(request(payload()),env)).status,200);
  assert.equal((await handleFeedback(request({...payload(),content:'changed'}),env)).status,409);
  assert.equal((await handleFeedback(request({...payload(2),replyTo:'hello@example.com'}),env)).status,200);
  assert.equal((await env.DB.prepare('SELECT COUNT(*) AS n FROM feedback').bind().first()).n,2);
});
test('per-IP limit and retry after lost acknowledgement',async()=>{
  const env={DB:database()};
  for(let n=1;n<=5;n++) assert.equal((await handleFeedback(request(payload(n)),env)).status,200);
  assert.equal((await handleFeedback(request(payload(6)),env)).status,429);
  assert.equal((await handleFeedback(request(payload(1)),env)).status,200);
});
test('failed persistence never acknowledges success; invalid requests rejected',async()=>{
  assert.equal((await handleFeedback(request(payload()),{})).status,503);
  assert.equal((await handleFeedback(request(payload()),{DB:{prepare(){throw Error('offline');}}})).status,503);
  assert.equal((await handleFeedback(request(payload(),{origin:'https://untrusted.example'}),{DB:database()})).status,403);
  assert.equal((await handleFeedback(request({content:'x'.repeat(40000)}),{DB:database()})).status,400);
});

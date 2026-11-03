import { test } from 'node:test';
import assert from 'node:assert/strict';
import { adminAccess, loadInbox, pageNumber } from './policy.mjs';
const owner = {userId:'trusted-sites-identity',email:'hyundongy@gmail.com'};
test('administrator requires a signed-in identity and exact owner email',()=>{
  assert.equal(adminAccess(null),'anonymous');
  assert.equal(adminAccess({email:owner.email}),'anonymous');
  assert.equal(adminAccess({userId:'someone',email:'someone@example.com'}),'denied');
  assert.equal(adminAccess({...owner,email:'hyundongy@gmail.com.attacker.example'}),'denied');
  assert.equal(adminAccess(owner),'allowed');
});
test('unauthorized users never reach database queries',async()=>{
  const db = {prepare(){throw Error('must not query');}};
  assert.equal((await loadInbox(null,db)).access,'anonymous');
  assert.equal((await loadInbox({userId:'someone',email:'other@example.com'},db)).access,'denied');
});
test('pagination rejects injection and keeps queries bounded',async()=>{
  for(const value of ['0','-1','1;DROP TABLE feedback','99999',['2'],undefined]) assert.equal(pageNumber(value),1);
  assert.equal(pageNumber('999'),400);
  assert.equal(pageNumber('2'),2);
  const queries = [];
  const db = {prepare(sql) {queries.push(sql);return {async first(){return {total:30};},bind(offset){assert.equal(offset,25);return {async all(){return {results:[{id:'test',content:'<script>alert(1)</script>'}]};}};}};}};
  const inbox = await loadInbox(owner,db,2);
  assert.equal(inbox.total,30); assert.equal(inbox.rows.length,1);
  assert.ok(queries[1].includes('ORDER BY created DESC, id DESC LIMIT 25 OFFSET ?'));
});

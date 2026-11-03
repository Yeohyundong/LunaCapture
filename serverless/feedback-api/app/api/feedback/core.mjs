const json = (status, body) => Response.json(body, { status, headers: { 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' } });
export function validate(body) {
  if (!body || typeof body.content !== 'string' || !body.content.trim() || body.content.length > 4000) return false;
  if (body.replyTo != null && body.replyTo !== '' && (typeof body.replyTo !== 'string' || body.replyTo.length > 254 || !/^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/.test(body.replyTo))) return false;
  return typeof body.version === 'string' && /^\d{1,4}\.\d{1,4}\.\d{1,4}$/.test(body.version) && typeof body.requestId === 'string' && /^[a-f0-9]{32}$/.test(body.requestId);
}
async function readBody(request) {
  const reader = request.body?.getReader();
  if (!reader) throw Error('body');
  const parts = []; let size = 0;
  while (true) {
    const { value, done } = await reader.read(); if (done) break;
    size += value.byteLength;
    if (size > 32768) { await reader.cancel(); throw Error('large'); }
    parts.push(value);
  }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const part of parts) { bytes.set(part, offset); offset += part.length; }
  return JSON.parse(new TextDecoder().decode(bytes));
}
export async function handleFeedback(request, env) {
  if (request.method !== 'POST') return json(405, { ok: false });
  if (request.headers.get('origin') && request.headers.get('origin') !== new URL(request.url).origin) return json(403, { ok: false });
  if (!request.headers.get('content-type')?.startsWith('application/json')) return json(415, { ok: false });
  let body;
  try { body = await readBody(request); } catch { return json(400, { ok: false }); }
  if (!validate(body)) return json(400, { ok: false });
  if (!env.DB) return json(503, { ok: false });
  const content = body.content.trim(), reply = body.replyTo || null;
  try {
    const previous = await env.DB.prepare('SELECT content, reply_to, version FROM feedback WHERE id = ?').bind(body.requestId).first();
    if (previous) {
      const same = previous.content === content && previous.reply_to === reply && previous.version === body.version;
      return json(same ? 200 : 409, { ok: same });
    }
    const now = Math.floor(Date.now() / 1000);
    const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${Math.floor(now / 86400)}|${ip}`));
    const key = Array.from(new Uint8Array(digest), x => x.toString(16).padStart(2, '0')).join('');
    for (const [bucket, limit, expires] of [[`${key}:${Math.floor(now / 3600)}`, 5, now + 3600], [`all:${Math.floor(now / 86400)}`, 200, now + 86400]]) {
      const rate = await env.DB.prepare('INSERT INTO feedback_limits (key, count, expires) VALUES (?, 1, ?) ON CONFLICT(key) DO UPDATE SET count = count + 1 WHERE count < ? RETURNING count').bind(bucket, expires, limit).first();
      if (!rate) return json(429, { ok: false });
    }
    await env.DB.prepare('INSERT OR IGNORE INTO feedback (id, content, reply_to, version, created) VALUES (?, ?, ?, ?, ?)').bind(body.requestId, content, reply, body.version, now).run();
    const saved = await env.DB.prepare('SELECT content, reply_to, version FROM feedback WHERE id = ?').bind(body.requestId).first();
    if (!saved || saved.content !== content || saved.reply_to !== reply || saved.version !== body.version) return json(409, { ok: false });
    try { await env.DB.prepare('DELETE FROM feedback_limits WHERE expires < ?').bind(now - 86400).run(); } catch {}
    return json(200, { ok: true });
  } catch { return json(503, { ok: false }); }
}

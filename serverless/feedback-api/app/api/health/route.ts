import { env } from 'cloudflare:workers';
export const dynamic = 'force-dynamic';
export async function GET() {
  try {
    if (!env.DB) throw Error('unavailable');
    await env.DB.prepare('SELECT id FROM feedback LIMIT 1').first();
    return Response.json({ ready: true }, { headers: { 'Cache-Control': 'no-store' } });
  } catch { return Response.json({ ready: false }, { status: 503 }); }
}

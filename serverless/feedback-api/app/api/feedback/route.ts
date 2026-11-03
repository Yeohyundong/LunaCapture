import { env } from 'cloudflare:workers';
import { handleFeedback } from './core.mjs';
export const dynamic = 'force-dynamic';
export async function POST(request: Request) { return handleFeedback(request, env); }

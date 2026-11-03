// Identity comes exclusively from Sites' trusted sign-in headers, never query/body/cookies.
const OWNER_EMAIL = 'hyundongy@gmail.com';
export function adminAccess(user) {
  if (!user?.userId || !user?.email) return 'anonymous';
  return user.email.toLowerCase() === OWNER_EMAIL ? 'allowed' : 'denied';
}
export function pageNumber(value) {
  return typeof value === 'string' && /^[1-9]\d{0,2}$/.test(value) ? Math.min(Number(value), 400) : 1;
}
export async function loadInbox(user, db, page = 1) {
  const access = adminAccess(user);
  if (access !== 'allowed') return { access, rows: [], total: 0 };
  if (!db) throw Error('database unavailable');
  const count = await db.prepare('SELECT COUNT(*) AS total FROM feedback').first();
  const result = await db.prepare('SELECT id, content, reply_to, version, created FROM feedback ORDER BY created DESC, id DESC LIMIT 25 OFFSET ?').bind((page - 1) * 25).all();
  return { access, rows: result.results, total: Number(count?.total ?? 0) };
}

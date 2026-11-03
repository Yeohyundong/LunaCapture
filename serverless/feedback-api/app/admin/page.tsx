import { env } from 'cloudflare:workers';
import { getChatGPTUser, chatGPTSignInPath, chatGPTSignOutPath } from '../chatgpt-auth';
import { adminAccess, loadInbox, pageNumber } from './policy.mjs';
import './style.css';

export const dynamic = 'force-dynamic';
export const revalidate = 0;
export const metadata = { title: '피드백함 · LunaCapture', robots: { index: false, follow: false } };
type Feedback = { id: string; content: string; reply_to: string | null; version: string; created: number };

export default async function Admin({ searchParams }: { searchParams: Promise<{ page?: string }> }) {
  const user = await getChatGPTUser();
  const access = adminAccess(user);
  const page = pageNumber((await searchParams).page);
  const returnTo = page > 1 ? `/admin?page=${page}` : '/admin';
  if (access !== 'allowed') return <main className="inbox-gate">
    <a className="inbox-brand" href="/">◔ LunaCapture</a>
    <div className="inbox-lock" aria-hidden="true">↗</div>
    <p className="inbox-eyebrow">관리자 전용</p>
    <h1>피드백함에 오신 걸 환영해요.</h1>
    <p>{access === 'anonymous' ? '관리자 ChatGPT 계정으로 로그인하면 접수된 의견을 확인할 수 있어요.' : '이 계정에는 관리자 권한이 없어요. 사이트 소유자의 계정으로 다시 로그인해주세요.'}</p>
    {access === 'anonymous' ? <a className="inbox-button" href={chatGPTSignInPath(returnTo)} target="_top">ChatGPT로 로그인 →</a> : <>
      <p className="inbox-account">현재 계정: {user?.email}</p>
      <a className="inbox-button" href={chatGPTSignOutPath('/admin')} target="_top">로그아웃하고 계정 바꾸기</a>
    </>}
    <p className="inbox-note">피드백은 공개되지 않으며, 다른 로그인 계정은 열람할 수 없습니다.</p>
  </main>;

  let inbox;
  try { inbox = await loadInbox(user, env.DB, page); } catch { return <main className="inbox-gate">
    <a className="inbox-brand" href="/">◔ LunaCapture</a><h1>피드백함을 불러오지 못했어요.</h1>
    <p>잠시 뒤 다시 시도해주세요. 접수된 내용은 그대로 보관됩니다.</p><a className="inbox-button" href="/admin">다시 불러오기</a>
  </main>; }
  const rows = inbox.rows as Feedback[];
  const date = (value: number) => new Intl.DateTimeFormat('ko-KR', { timeZone: 'Asia/Seoul', dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value * 1000));
  return <main className="inbox-shell">
    <header className="inbox-header"><a className="inbox-brand" href="/">◔ LunaCapture</a><div><span className="inbox-account">{user?.email}</span><a href={chatGPTSignOutPath('/admin')} target="_top">로그아웃</a></div></header>
    <section className="inbox-heading"><div><p className="inbox-eyebrow">작은 의견, 더 나은 캡처</p><h1>피드백함 <span>{inbox.total}</span></h1><p>사용자가 보내준 불편한 점과 아이디어를 모았어요.</p></div><a className="inbox-refresh" href={returnTo}>↻ 새로고침</a></section>
    <div className="inbox-list-heading"><span>최근 접수순</span><span>시간 기준 · 한국</span></div>
    <section className="inbox-list" aria-label="접수된 피드백">
      {rows.length === 0 ? <div className="inbox-empty"><h2>{inbox.total ? '이 페이지에는 의견이 없어요.' : '아직 접수된 의견이 없어요.'}</h2><p>앱에서 보내는 첫 피드백을 기다리고 있어요.</p></div> : rows.map(row => <article key={row.id} className="inbox-card">
        <div className="inbox-meta"><time dateTime={new Date(row.created * 1000).toISOString()}>{date(row.created)}</time><span>v{row.version}</span></div>
        <p className="inbox-content">{row.content}</p>
        <footer>{row.reply_to ? <><span>답장 이메일</span><a href={`mailto:${encodeURIComponent(row.reply_to)}`}>{row.reply_to} ↗</a></> : <span>답장 이메일 없음</span>}</footer>
      </article>)}
    </section>
    <nav className="inbox-pagination" aria-label="페이지 이동">{page > 1 ? <a href={`/admin?page=${page - 1}`}>← 이전</a> : <span/>}<span>{page} 페이지</span>{page * 25 < inbox.total && page < 400 ? <a href={`/admin?page=${page + 1}`}>다음 →</a> : <span/>}</nav>
    <p className="inbox-note">관리자만 볼 수 있는 페이지입니다. 이메일은 답장 목적으로만 사용해주세요.</p>
  </main>;
}

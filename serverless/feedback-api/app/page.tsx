export default function Page() {
 return <main style={{maxWidth:640,margin:'12vh auto',padding:32,fontFamily:'system-ui',lineHeight:1.8}}>
 <p style={{color:'#7160c9'}}>LunaCapture</p><h1>더 편한 캡처를 함께 만들어요.</h1>
 <p>앱 편집 창의 설정 → <strong>이거 고쳐주세요!</strong>에서 불편했던 점이나 아이디어를 보내주세요.</p>
 <hr/><h2>어떤 정보가 전달되나요?</h2>
 <p>작성한 내용, 선택으로 입력한 답장 이메일, 앱 버전이 비공개 피드백함에 저장됩니다. 캡처 사진과 로그는 보내지 않습니다. 이메일은 답장 목적으로만 사용합니다.</p>
 <p>비밀번호나 민감한 개인정보는 적지 말아주세요. 접수된 내용은 개발자만 확인하며 일반 방문자는 열람할 수 없습니다.</p>
 <p style={{marginTop:40}}><a href="/admin" style={{color:'#7160c9',textDecoration:'underline'}}>관리자 로그인 →</a></p>
 </main>;
}

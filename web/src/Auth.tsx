import { createContext, useContext, useEffect, useState, type ReactNode, type FormEvent } from 'react';
import { Navigate, useLocation, Link } from 'react-router-dom';
import { api, post, resetCsrf, type User } from './api';
import { ErrorNotice, Loading } from './hooks';
import { ArrowRight, BookOpen, CheckCircle2 } from 'lucide-react';
type AuthState = { user?: User; loading: boolean; refresh: () => Promise<void>; logout: () => Promise<void> };
const Auth = createContext<AuthState | null>(null);
export const useAuth = () => useContext(Auth)!;
/** VI: Chia sẻ một trạng thái đăng nhập toàn app. EN: Share one identity request across the application. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User>(); const [loading, setLoading] = useState(true);
  const refresh = async () => { resetCsrf(); setUser(await api<User>('/auth/me')); };
  useEffect(() => { api<User>('/auth/me').then(setUser).catch(() => setUser(undefined)).finally(() => setLoading(false)); }, []);
  const logout = async () => { await post('/auth/logout'); resetCsrf(); setUser(undefined); };
  return <Auth.Provider value={{ user, loading, refresh, logout }}>{children}</Auth.Provider>;
}
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth(); const location = useLocation();
  if (loading) return <Loading/>;
  return user ? children : <Navigate to="/login" state={{ from: location.pathname }} replace/>;
}
export function AuthPage({ mode = 'login' }: { mode?: 'login' | 'register' | 'forgot-password' | 'reset-password' | 'verify-email' }) {
  const { user, refresh } = useAuth(); const location = useLocation();
  const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [name, setName] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState<Error>(); const [message, setMessage] = useState('');
  if (user && mode === 'login') return <Navigate to={location.state?.from ?? '/dashboard'} replace/>;
  const submit = async (event?: FormEvent, demo = false) => {
    event?.preventDefault(); setBusy(true); setError(undefined);
    try {
      if (mode === 'login') { await post('/auth/login', { email: demo ? 'learner@englishhub.local' : email, password: demo ? 'EnglishHub@2026' : password }); await refresh(); }
      else if (mode === 'register') { await post('/auth/register', { email, password, displayName: name }); setMessage('Đã tạo tài khoản. Hãy mở email để xác minh trước khi đăng nhập.'); }
      else if (mode === 'forgot-password') { await post('/auth/forgot-password', { email }); setMessage('Nếu tài khoản hợp lệ, liên kết đặt lại mật khẩu đã được gửi.'); }
      else { const params = new URLSearchParams(location.search); await post(`/auth/${mode}`, { userId: params.get('userId'), token: params.get('token'), newPassword: password }); setMessage(mode === 'verify-email' ? 'Email đã xác minh.' : 'Đã thay đổi mật khẩu.'); }
    } catch (e) { setError(e as Error); } finally { setBusy(false); }
  };
  const titles = { login: 'Chào mừng bạn trở lại.', register: 'Bắt đầu chương mới.', 'forgot-password': 'Một khởi đầu mới.', 'reset-password': 'Đặt mật khẩu mới.', 'verify-email': 'Xác minh email.' };
  return <div className="auth-page"><aside className="auth-story"><Link to="/" className="brand"><BookOpen/> englishhub.</Link><div><span className="eyebrow">YOUR WORLD, A LITTLE BIGGER.</span><h1>Mỗi ngày một chút.<br/>Tiến xa <em>hơn nữa.</em></h1><p>Biến “một ngày nào đó” thành hôm nay.<br/>Chúng tôi cùng bạn trên hành trình này.</p></div><span className="muted">Học theo nhịp của bạn. Tiến bộ theo cách của bạn.</span></aside><main className="auth-form"><span className="eyebrow">LET'S MAKE PROGRESS</span><h2>{titles[mode]}</h2><p className="muted">Một bước nhỏ hôm nay mở ra nhiều cơ hội ngày mai.</p><ErrorNotice error={error}/>{message ? <div className="notice success"><CheckCircle2 size={18}/>{message}<Link to="/login">Đăng nhập →</Link></div> : <form onSubmit={submit}>
    {mode === 'register' ? <label>Tên của bạn<input value={name} onChange={e => setName(e.target.value)} required maxLength={120} autoComplete="name"/></label> : null}
    {['login','register','forgot-password'].includes(mode) ? <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email"/></label> : null}
    {['login','register','reset-password'].includes(mode) ? <label>Mật khẩu<input type="password" value={password} onChange={e => setPassword(e.target.value)} required minLength={mode === 'login' ? 1 : 10} autoComplete={mode === 'login' ? 'current-password' : 'new-password'}/></label> : null}
    {mode === 'login' ? <Link className="forgot" to="/forgot-password">Quên mật khẩu?</Link> : null}<button className="primary full" disabled={busy}>{busy ? 'Đang xử lý…' : mode === 'login' ? 'Đăng nhập' : 'Tiếp tục'}<ArrowRight size={17}/></button></form>}{mode === 'login' ? <><div className="divider">hoặc trải nghiệm trước</div><button className="secondary full" disabled={busy} onClick={() => submit(undefined, true)}>Khám phá bằng tài khoản demo</button><p className="auth-switch">Chưa có tài khoản? <Link to="/register">Tham gia EnglishHub</Link></p></> : <Link className="auth-switch" to="/login">← Trở lại đăng nhập</Link>}</main></div>;
}

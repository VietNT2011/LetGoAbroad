import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { BookOpen, ArrowUpRight } from 'lucide-react';
import './styles.css';

// VI: Shell khởi đầu, các màn hình gọi API được bổ sung theo milestone. EN: The initial shell grows with verified API milestones.
function App() {
  return <main className="welcome"><span className="brand"><BookOpen size={25}/> englishhub<span className="dot">.</span></span><span className="eyebrow">A LITTLE EVERY DAY. A WORLD OF DIFFERENCE.</span><h1>Your next chapter<br/>starts <em>today.</em></h1><p>Một lộ trình rõ ràng. Một chút tiến bộ mỗi ngày.<br/>Cùng mở ra thế giới của bạn với tiếng Anh.</p><a className="primary" href="/dashboard">Bắt đầu học <ArrowUpRight size={18}/></a><p className="muted">Không cần AI · Học thật, tiến bộ thật</p></main>;
}
ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><BrowserRouter><App/></BrowserRouter></React.StrictMode>);

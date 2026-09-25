import React, { lazy, Suspense } from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, AuthPage, RequireAuth } from './Auth';
import { Layout } from './Layout';
import { Dashboard, Catalog } from './pages/Catalog';
import { Loading } from './hooks';
import { PathsPage, SettingsPage } from './pages/Learning';
import './styles.css';

const Lesson = lazy(() => import('./pages/Lesson'));
const TestIntro = lazy(() => import('./pages/Assessment').then(m => ({ default: m.TestIntro })));
const AttemptPage = lazy(() => import('./pages/Assessment').then(m => ({ default: m.AttemptPage })));
const ResultPage = lazy(() => import('./pages/Assessment').then(m => ({ default: m.ResultPage })));
// VI: Không giả chức năng chưa triển khai. EN: Pending screens explicitly describe their implementation status.
function Pending() { return <div className="empty"><h2>Từng bước, mình tiến lên.</h2><p>Tính năng này đang được triển khai theo đặc tả.</p></div>; }
function App() {
  return <AuthProvider><Suspense fallback={<Loading/>}><Routes>
    <Route path="/" element={<Navigate to="/dashboard" replace/>}/>
    {(['login','register','forgot-password','reset-password','verify-email'] as const).map(mode => <Route key={mode} path={`/${mode}`} element={<AuthPage mode={mode}/>}/>)}
    <Route element={<RequireAuth><Layout/></RequireAuth>}>
      <Route path="/dashboard" element={<Dashboard/>}/><Route path="/catalog" element={<Catalog/>}/><Route path="/tests" element={<Catalog tests/>}/>
      <Route path="/learn/:id" element={<Lesson/>}/><Route path="/tests/:id" element={<TestIntro/>}/>
      <Route path="/attempts/:id" element={<AttemptPage/>}/><Route path="/attempts/:id/result" element={<ResultPage/>}/>
      <Route path="/paths" element={<PathsPage/>}/><Route path="/settings" element={<SettingsPage/>}/>
      {['vocabulary','groups','admin'].map(path => <Route key={path} path={path} element={<Pending/>}/>)}
    </Route>
    <Route path="*" element={<div className="empty"><h1>Không tìm thấy trang.</h1><a href="/dashboard">Trở về không gian học tập →</a></div>}/>
  </Routes></Suspense></AuthProvider>;
}
ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><BrowserRouter><App/></BrowserRouter></React.StrictMode>);

import { useEffect, useRef, useState } from 'react';
import { Upload, RefreshCw, Music2, CheckCircle2 } from 'lucide-react';
import { post } from '../api';
import { ErrorNotice, useResource } from '../hooks';
import './media.css';
type Asset = { id: string; fileName: string; status: string; failureCode: string | null; durationMs: number | null; attribution: string };
type Job = { id: string; resourceId: string; kind: string; state: string; attempts: number; lastErrorCode: string | null };
/** VI: Upload SAS không được ghi vào localStorage/log. EN: Keep scoped upload URLs in memory and never log or persist access grants. */
export function MediaAdmin({ admin }: { admin: boolean }) {
  const assets=useResource<Asset[]>('/admin/media'); const jobs=useResource<Job[]>(admin ? '/admin/jobs' : null);
  const [file,setFile]=useState<File>(); const [attribution,setAttribution]=useState(''); const [busy,setBusy]=useState(false);
  const [progress,setProgress]=useState(''); const [error,setError]=useState<Error>(); const command=useRef(crypto.randomUUID());
  const upload=async () => {
    if (!file) return; setBusy(true); setError(undefined);
    try {
      setProgress('Đang tạo phiên upload…');
      const grant=await post<{ assetId: string; uploadUrl: string }>('/media/uploads',{ clientRequestId:command.current,purpose:'lesson_audio',fileName:file.name,contentType:file.type || 'audio/wav',bytes:file.size,attribution });
      setProgress('Đang gửi file đến kho lưu trữ…');
      const response=await fetch(grant.uploadUrl,{ method:'PUT',headers:{ 'x-ms-blob-type':'BlockBlob','Content-Type':file.type || 'audio/wav' },body:file });
      if (!response.ok) throw new Error('Upload chưa thành công. Hãy thử lại trước khi phiên hết hạn.');
      await post(`/media/${grant.assetId}/complete`); command.current=crypto.randomUUID(); setProgress(`Đã nhận file. Worker đang xác minh và xử lý. Mã media: ${grant.assetId}`); assets.reload(); if(admin) jobs.reload();
    } catch (e) { setError(e as Error); } finally { setBusy(false); }
  };
  // VI: REST polling là đường phục hồi, không báo Ready từ tiến độ upload. EN: Poll durable server state; uploaded bytes alone never imply a ready asset.
  useEffect(() => { if (!assets.data?.some(x => ['pending_upload','uploaded','processing'].includes(x.status))) return; const timer=setTimeout(() => { assets.reload(); if(admin) jobs.reload(); },2500); return () => clearTimeout(timer); },[assets.data,assets.reload,admin,jobs.reload]);
  return <section className="cms-import media-admin"><div className="section-heading"><div><span className="eyebrow">MEDIA, WITH A CLEAR SOURCE</span><h2>Audio & hàng đợi xử lý</h2></div><button className="secondary" onClick={() => { assets.reload(); if(admin) jobs.reload(); }}><RefreshCw size={13}/> Cập nhật</button></div><ErrorNotice error={error ?? assets.error}/><div className="cms-new-fields"><label>File audio · tối đa 100 MiB<input type="file" accept="audio/mpeg,audio/wav,audio/ogg,audio/mp4,audio/webm" disabled={busy} onChange={e => { setFile(e.target.files?.[0]); command.current=crypto.randomUUID(); }}/></label><label>Nguồn & quyền sử dụng<input value={attribution} onChange={e => setAttribution(e.target.value)} placeholder="Tác giả, giấy phép hoặc căn cứ sử dụng" required/></label></div><button className="primary" disabled={busy || !file || !attribution} onClick={upload}><Upload size={14}/>{busy ? 'Đang upload…' : 'Upload & xử lý'}</button>{progress ? <p className="media-status">{progress}</p> : null}<div className="asset-list">{assets.data?.map(asset => <article key={asset.id}><Music2 size={20}/><div><b>{asset.fileName}</b><code>{asset.id}</code><span>{asset.attribution}</span></div><span className={`asset-status ${asset.status}`}>{asset.status==='ready' ? <CheckCircle2 size={14}/> : null}{asset.status}{asset.failureCode ? ` · ${asset.failureCode}` : ''}</span>{asset.status==='ready' ? <audio controls preload="none" src={`/api/v1/media/${asset.id}/content`}/> : null}</article>)}</div>{admin ? <details><summary>Trạng thái job và lịch sử retry</summary><ErrorNotice error={jobs.error}/><div className="job-list">{jobs.data?.map(job => <div key={job.id}><code>{job.id}</code><span>{job.kind} · {job.state} · {job.attempts}/4 attempts</span>{job.lastErrorCode ? <span>{job.lastErrorCode}</span> : null}</div>)}</div></details> : null}</section>;
}
export function AudioPlayer({ assetId }: { assetId: string }) {
  const audio=useRef<HTMLAudioElement>(null); const position=useRef(0); const [revision,setRevision]=useState(0); const [failed,setFailed]=useState(false);
  return <div className="lesson-audio"><span className="eyebrow">TAKE A MOMENT TO LISTEN</span><audio ref={audio} controls preload="metadata" src={`/api/v1/media/${assetId}/content?r=${revision}`} onError={() => setFailed(true)} onTimeUpdate={() => { position.current=audio.current?.currentTime ?? 0; }} onLoadedMetadata={() => { if(audio.current) audio.current.currentTime=Math.min(position.current,audio.current.duration || position.current); setFailed(false); }}/>{failed ? <button className="secondary" onClick={() => setRevision(v => v+1)}><RefreshCw size={14}/> Thử phát lại</button> : null}<label>Tốc độ<select aria-label="Tốc độ phát" defaultValue="1" onChange={e => { if(audio.current) audio.current.playbackRate=Number(e.target.value); }}>{[0.75,1,1.25,1.5].map(rate => <option key={rate}>{rate}</option>)}</select></label></div>;
}

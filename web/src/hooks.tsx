import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
/** VI: Hủy request cũ để tránh ghi đè dữ liệu mới. EN: Abort obsolete requests so stale responses cannot overwrite the new view. */
export function useResource<T>(path: string | null) {
  const [data, setData] = useState<T>(); const [error, setError] = useState<Error>(); const [loading, setLoading] = useState(true); const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!path) { setLoading(false); return; }
    const abort = new AbortController(); setLoading(true); setError(undefined); setData(undefined);
    api<T>(path, { signal: abort.signal }).then(result => { if (!abort.signal.aborted) setData(result); }).catch(e => { if (!abort.signal.aborted) setError(e); }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [path, revision]);
  return { data, error, loading, reload: useCallback(() => setRevision(v => v + 1), []) };
}
export function ErrorNotice({ error, retry }: { error?: Error; retry?: () => void }) { return error ? <div className="notice error" role="alert">{error.message}{retry ? <button className="link-button" onClick={retry}>Thử lại</button> : null}</div> : null; }
export function Loading() { return <div className="loading" role="status"><span className="spinner"/> Đang tải hành trình của bạn…</div>; }

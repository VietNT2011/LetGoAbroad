import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
/** VI: Hủy request cũ để tránh ghi đè dữ liệu mới. EN: Abort obsolete requests so stale responses cannot overwrite the new view. */
export function useResource<T>(path: string | null) {
  const [snapshot, setSnapshot] = useState<{ path: string; value: T }>();
  const [error, setError] = useState<Error>();
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!path) {
      setSnapshot(undefined);
      setLoading(false);
      return;
    }
    const abort = new AbortController();
    setLoading(true);
    setError(undefined);
    setSnapshot(undefined);
    api<T>(path, { signal: abort.signal })
      .then((result) => {
        if (!abort.signal.aborted) setSnapshot({ path, value: result });
      })
      .catch((e) => {
        if (!abort.signal.aborted) setError(e);
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [path, revision]);
  return {
    // VI: Đổi/đóng nguồn phải ẩn snapshot cũ ngay trong render, trước khi effect dọn request.
    // EN: Hide old-path snapshots during render, before effects clean up the previous request.
    data: snapshot?.path === path ? snapshot.value : undefined,
    error,
    loading,
    reload: useCallback(() => setRevision((v) => v + 1), []),
  };
}
export function ErrorNotice({
  error,
  retry,
}: {
  error?: Error;
  retry?: () => void;
}) {
  return error ? (
    <div className="notice error" role="alert">
      {error.message}
      {retry ? (
        <button className="link-button" onClick={retry}>
          Thử lại
        </button>
      ) : null}
    </div>
  ) : null;
}
export function Loading() {
  return (
    <div className="loading" role="status">
      <span className="spinner" /> Đang tải hành trình của bạn…
    </div>
  );
}

import { useEffect, useRef, useState } from "react";

export type RecordingClip = {
  id: string;
  order: number;
  startMs: number;
  endMs: number;
};

// VI: DTO dùng mili giây; chỉ dung sai 1ms khi đối chiếu thời lượng decoder.
// EN: The authored DTO uses milliseconds; allow only 1ms of decoder-duration rounding.
function clipEnd(player: HTMLAudioElement, end: number): number | null {
  if (!Number.isFinite(player.duration)) return end;
  return player.duration + 0.001 < end ? null : Math.min(end, player.duration);
}

/** VI: Chỉ nghe đoạn do server xác nhận, không mở transcript/chấm bài/tự phát. EN: Play server-resolved timing without transcript disclosure, learning writes or autoplay. */
export function RecordingClipPlayer({
  assetId,
  clip,
}: {
  assetId: string;
  clip: RecordingClip;
}) {
  const audio = useRef<HTMLAudioElement>(null);
  const position = useRef(clip.startMs / 1000);
  const [playing, setPlaying] = useState(false);
  const [revision, setRevision] = useState(0);
  const [failed, setFailed] = useState(false);
  const [badDuration, setBadDuration] = useState(false);
  const start = clip.startMs / 1000;
  const end = clip.endMs / 1000;
  const valid =
    Number.isFinite(start) && Number.isFinite(end) && start >= 0 && end > start;

  // VI: Dừng player cũ ngay khi đổi đoạn/rời trang. EN: Stop the captured element when its source is removed.
  useEffect(() => {
    const player = audio.current;
    return () => player?.pause();
  }, [assetId, valid]);

  // VI: Kiểm tra cả theo frame và media event; không dùng timer để tự bắt đầu audio.
  // EN: Enforce the boundary on frames and media events; no timer starts playback.
  useEffect(() => {
    if (!playing || !valid) return;
    let frame = 0;
    const tick = () => {
      const player = audio.current;
      if (!player || player.paused) return;
      const limit = clipEnd(player, end);
      if (limit == null || limit <= start) {
        player.pause();
        setPlaying(false);
        setBadDuration(true);
        return;
      }
      if (player.currentTime >= limit) {
        player.pause();
        if (player.currentTime !== limit) player.currentTime = limit;
        position.current = limit;
        setPlaying(false);
        return;
      }
      if (player.currentTime < start) player.currentTime = start;
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [playing, valid, start, end]);

  if (!valid) return <p role="alert">Thời gian đoạn mẫu chưa hợp lệ.</p>;
  const enforce = (player: HTMLAudioElement) => {
    const stop = clipEnd(player, end);
    if (stop == null || stop <= start) {
      player.pause();
      setPlaying(false);
      setBadDuration(true);
      return;
    }
    if (player.currentTime < start) player.currentTime = start;
    if (player.currentTime >= stop) {
      player.pause();
      setPlaying(false);
      if (player.currentTime !== stop) player.currentTime = stop;
    }
    position.current = player.currentTime;
  };
  return (
    <div className="lesson-audio">
      <p>
        <b>Mẫu theo đoạn {clip.order + 1}</b> · {start}–{end} giây
      </p>
      <audio
        ref={audio}
        controls
        preload="metadata"
        src={`/api/v1/media/${assetId}/content?r=${revision}`}
        onLoadedMetadata={(event) => {
          const player = event.currentTarget;
          const stop = clipEnd(player, end);
          const fits = stop != null && stop > start;
          setBadDuration(!fits);
          setFailed(false);
          if (fits)
            player.currentTime = Math.max(
              start,
              Math.min(position.current, stop),
            );
          else {
            player.pause();
            setPlaying(false);
          }
        }}
        onPlay={(event) => {
          const player = event.currentTarget;
          const stop = clipEnd(player, end);
          if (stop == null || stop <= start) {
            player.pause();
            setBadDuration(true);
            setPlaying(false);
            return;
          }
          if (player.currentTime < start || player.currentTime >= stop)
            player.currentTime = start;
          setBadDuration(false);
          setPlaying(true);
        }}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
        onSeeking={(event) => enforce(event.currentTarget)}
        onTimeUpdate={(event) => enforce(event.currentTarget)}
        onError={() => {
          setFailed(true);
          setPlaying(false);
        }}
      />
      {badDuration ? (
        <p role="alert">
          Đoạn mẫu không khớp thời lượng audio; chưa thể phát đúng đoạn.
        </p>
      ) : null}
      {failed ? (
        <button type="button" onClick={() => setRevision((value) => value + 1)}>
          Thử tải lại đoạn mẫu
        </button>
      ) : null}
      <label>
        Tốc độ đoạn mẫu
        <select
          aria-label="Tốc độ đoạn mẫu"
          defaultValue="1"
          onChange={(event) => {
            if (audio.current)
              audio.current.playbackRate = Number(event.target.value);
          }}
        >
          {[0.75, 1, 1.25, 1.5].map((rate) => (
            <option key={rate}>{rate}</option>
          ))}
        </select>
      </label>
    </div>
  );
}

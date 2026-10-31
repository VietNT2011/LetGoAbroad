import { Link, useSearchParams } from "react-router-dom";
import { ErrorNotice, Loading, useResource } from "../hooks";
import { AudioPlayer } from "./Media";
import { recordingSourceSuggestion } from "../recordingSource";

type Reference = {
  id: string;
  title: string;
  version: number;
  archived: boolean;
  attribution: string;
  mediaAssetId: string | null;
};

/** VI: Chỉ chọn version bài học; asset mẫu luôn lấy từ API có kiểm quyền. EN: Resolve the sample through an authorized lesson version, never a caller-provided media URL. */
export function RecordingReference() {
  const [search] = useSearchParams();
  const version = search.get("lesson");
  if (!version) return null;
  const suggestion = recordingSourceSuggestion(version, search.get("segment"));
  if (!suggestion)
    return (
      <p role="status">
        Liên kết bài mẫu chưa hợp lệ. Mở lại từ bài nghe bạn muốn luyện.
      </p>
    );
  // VI: Đổi nguồn hủy cây cũ để không phát nhầm audio trong lúc tải version mới.
  // EN: Remount when the version changes so old sample data/audio cannot remain visible during the next fetch.
  return (
    <ReferenceLesson
      key={suggestion.versionId}
      version={suggestion.versionId}
    />
  );
}

function ReferenceLesson({ version }: { version: string }) {
  const reference = useResource<Reference>(`/lessons/${version}`);
  return (
    <section className="card-create" aria-label="Bài mẫu để so giọng">
      <span className="eyebrow">LISTEN, RECORD, COMPARE</span>
      <h2>Nghe mẫu rồi thử giọng của bạn</h2>
      <p>
        Nghe một lượt, dừng mẫu rồi ghi âm. Khi có bản nháp, nghe luân phiên để
        tự so nhịp và cách đọc; bấm Lưu riêng khi muốn giữ bản thu.
      </p>
      <ErrorNotice error={reference.error} retry={reference.reload} />
      {reference.loading ? (
        <Loading />
      ) : reference.data ? (
        <>
          <h3>
            {reference.data.title} · v{reference.data.version}
          </h3>
          {reference.data.mediaAssetId ? (
            <AudioPlayer
              key={reference.data.mediaAssetId}
              assetId={reference.data.mediaAssetId}
            />
          ) : (
            <p>Bài này chưa có audio mẫu.</p>
          )}
          {reference.data.archived ? (
            <p>Đây là bản mẫu đã lưu trữ mà tài khoản của bạn còn quyền xem.</p>
          ) : null}
          <p className="source-note">Nguồn mẫu: {reference.data.attribution}</p>
          <Link to={`/learn/${reference.data.id}`}>
            Trở lại nội dung bài mẫu →
          </Link>
        </>
      ) : null}
    </section>
  );
}

/** VI: Chỉ dừng các player trong trang hiện tại, không đổi vị trí nghe hoặc tự phát. EN: Pause other players in this page without rewinding or starting any audio. */
export function pauseOtherRecordingAudio(
  scope: ParentNode,
  active: EventTarget | null,
) {
  if (!(active instanceof HTMLAudioElement)) return;
  for (const audio of scope.querySelectorAll("audio")) {
    if (audio !== active && !audio.paused) audio.pause();
  }
}

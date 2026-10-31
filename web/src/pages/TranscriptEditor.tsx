import { useMemo, useState } from "react";
import "./transcript-editor.css";

type Segment = {
  startMs: number;
  endMs: number;
  textEn: string;
  textVi: string;
  acceptedAnswers?: string[] | null;
  normalizationPolicyVersion?: number;
  ignoreTerminalPunctuation?: boolean;
  [key: string]: unknown;
};
type Props = {
  json: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

/** VI: Form chỉ cập nhật JSON nháp; không gọi API hoặc tự publish. EN: This controlled editor changes the local JSON draft only, preserving the CMS save/version workflow. */
export function TranscriptEditor(props: Props) {
  const [open, setOpen] = useState(false);
  return (
    <details
      className="transcript-editor"
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary>Biên soạn transcript Anh–Việt</summary>
      {open ? <TranscriptFields {...props} /> : null}
    </details>
  );
}

export function TranscriptFields({ json, onChange, disabled = false }: Props) {
  const parsed = useMemo(() => {
    try {
      const value: unknown = JSON.parse(json);
      if (!value || typeof value !== "object" || Array.isArray(value))
        return null;
      const document = value as Record<string, unknown>;
      const list = document.segments ?? [];
      if (!Array.isArray(list) || list.length > 100) return null;
      for (const item of list) {
        if (
          !item ||
          typeof item !== "object" ||
          Array.isArray(item) ||
          !Number.isSafeInteger(item.startMs) ||
          !Number.isSafeInteger(item.endMs) ||
          typeof item.textEn !== "string" ||
          typeof item.textVi !== "string" ||
          (item.acceptedAnswers != null &&
            (!Array.isArray(item.acceptedAnswers) ||
              !item.acceptedAnswers.every(
                (x: unknown) => typeof x === "string",
              ))) ||
          (item.normalizationPolicyVersion !== undefined &&
            item.normalizationPolicyVersion !== 1) ||
          (item.ignoreTerminalPunctuation !== undefined &&
            typeof item.ignoreTerminalPunctuation !== "boolean")
        )
          return null;
      }
      return { document, segments: list as Segment[] };
    } catch {
      return null;
    }
  }, [json]);
  if (!parsed)
    return (
      <p role="status">
        JSON hoặc cấu trúc segments chưa được hỗ trợ. Sửa ở ô JSON bên dưới;
        form giữ nguyên dữ liệu của bạn.
      </p>
    );
  const { document, segments } = parsed;
  const replace = (next: Segment[]) => {
    if (disabled) return;
    // VI: Giữ nguyên groups, media, field mở rộng và metadata đoạn; không tái tạo cả payload từ form.
    // EN: Preserve groups, media, extension fields and segment metadata instead of rebuilding the payload from known controls.
    onChange(JSON.stringify({ ...document, segments: next }, null, 2));
  };
  const update = (index: number, patch: Partial<Segment>) =>
    replace(segments.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  const nextStart = segments.at(-1)?.endMs ?? 0;
  const canAdd = segments.length < 100 && nextStart >= 0 && nextStart < 3600000;
  const problems = segments.flatMap((s, i) => {
    const prefix = `Câu ${i + 1}: `;
    const messages: string[] = [];
    if (s.startMs < 0 || s.endMs <= s.startMs || s.endMs > 3600000)
      messages.push(
        prefix +
          "mốc kết thúc phải lớn hơn bắt đầu và nằm trong một giờ audio.",
      );
    if (i && s.startMs < segments[i - 1].endMs)
      messages.push(prefix + "thời gian chồng lên câu trước.");
    if (!s.textEn.trim() || !s.textVi.trim())
      messages.push(prefix + "cần cả nội dung tiếng Anh và tiếng Việt.");
    if (s.textEn.length > 2000 || s.textVi.length > 2000)
      messages.push(prefix + "mỗi bản Anh/Việt tối đa 2000 ký tự.");
    if (
      s.acceptedAnswers != null &&
      (s.acceptedAnswers.length < 1 ||
        s.acceptedAnswers.length > 20 ||
        s.acceptedAnswers.some((a) => !a.trim() || a.length > 2000))
    )
      messages.push(
        prefix +
          "cần từ 1 đến 20 biến thể không rỗng, mỗi biến thể tối đa 2000 ký tự.",
      );
    return messages;
  });
  return (
    <div className="transcript-fields">
      <p>
        Mốc tính bằng mili giây: 1000 ms = 1 giây. Gắn ID audio bài học ở trường
        mediaAssetId trong JSON. Server kiểm tra lại thời lượng audio, đáp án và
        thứ tự khi xuất bản.
      </p>
      <p>
        Form chỉnh bản nháp tại máy. Bấm Lưu nháp hoặc Tạo version mới để gửi;
        bản đã xuất bản giữ nguyên.
      </p>
      {!document.mediaAssetId && segments.length ? (
        <p role="status">
          Chưa gắn audio bài học. Transcript cần mediaAssetId hợp lệ trước khi
          xuất bản.
        </p>
      ) : null}
      {problems.length ? (
        <ul className="transcript-warnings" aria-label="Điểm cần kiểm tra">
          {problems.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      ) : null}
      <fieldset disabled={disabled}>
        <legend>{segments.length}/100 câu biên soạn</legend>
        {segments.length === 0 ? (
          <p>Chưa có transcript. Thêm câu đầu và nhập mốc theo file audio.</p>
        ) : null}
        {segments.map((segment, index) => (
          <section
            className="transcript-row"
            key={index}
            aria-label={`Transcript câu ${index + 1}`}
          >
            <h4>Câu {index + 1}</h4>
            <div className="transcript-time">
              {(["startMs", "endMs"] as const).map((key) => (
                <label key={key}>
                  {key === "startMs" ? "Bắt đầu" : "Kết thúc"} câu {index + 1}{" "}
                  (ms)
                  <input
                    type="number"
                    min={0}
                    max={3600000}
                    step={1}
                    value={segment[key]}
                    onChange={(e) => {
                      const value = e.currentTarget.valueAsNumber;
                      if (
                        Number.isSafeInteger(value) &&
                        value >= 0 &&
                        value <= 3600000
                      )
                        update(index, { [key]: value });
                    }}
                  />
                </label>
              ))}
            </div>
            <label>
              Tiếng Anh câu {index + 1}
              <textarea
                value={segment.textEn}
                maxLength={2000}
                rows={2}
                spellCheck={false}
                onChange={(e) => update(index, { textEn: e.target.value })}
              />
            </label>
            <label>
              Tiếng Việt câu {index + 1}
              <textarea
                value={segment.textVi}
                maxLength={2000}
                rows={2}
                onChange={(e) => update(index, { textVi: e.target.value })}
              />
            </label>
            <label>
              Biến thể được chấp nhận câu {index + 1} (mỗi dòng một đáp án)
              <textarea
                rows={2}
                maxLength={40019}
                spellCheck={false}
                placeholder="Để mặc định dùng câu tiếng Anh"
                value={segment.acceptedAnswers?.join("\n") ?? ""}
                onChange={(e) =>
                  update(index, { acceptedAnswers: e.target.value.split("\n") })
                }
              />
            </label>
            <div className="button-row">
              <button
                type="button"
                className="secondary"
                onClick={() => update(index, { acceptedAnswers: undefined })}
              >
                Dùng câu tiếng Anh làm đáp án câu {index + 1}
              </button>
              <label className="transcript-check">
                <input
                  type="checkbox"
                  checked={segment.ignoreTerminalPunctuation ?? true}
                  onChange={(e) =>
                    update(index, {
                      ignoreTerminalPunctuation: e.target.checked,
                    })
                  }
                />
                Bỏ qua dấu kết câu {index + 1}
              </label>
              <button
                type="button"
                className="link-button"
                onClick={() => replace(segments.filter((_, i) => i !== index))}
              >
                Bỏ câu {index + 1} khỏi bản nháp
              </button>
            </div>
          </section>
        ))}
        <button
          type="button"
          className="secondary"
          disabled={!canAdd}
          onClick={() => {
            if (canAdd)
              replace([
                ...segments,
                {
                  startMs: nextStart,
                  endMs: Math.min(nextStart + 3000, 3600000),
                  textEn: "",
                  textVi: "",
                  normalizationPolicyVersion: 1,
                  ignoreTerminalPunctuation: true,
                },
              ]);
          }}
        >
          Thêm câu transcript
        </button>
        {!canAdd ? (
          <p role="status">
            Đã đủ 100 câu hoặc mốc cuối đã hết giới hạn một giờ. Điều chỉnh JSON
            nếu cần.
          </p>
        ) : null}
      </fieldset>
    </div>
  );
}

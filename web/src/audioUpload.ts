import { api, ApiError } from "./api";
import { sessionBinding } from "./authSession";

type Grant = { assetId: string; uploadUrl: string; expiresAt: string };
export const audioBlockBytes = 4 * 1024 * 1024;

/** VI: File và grant chỉ ở RAM; tiếp tục đúng block chưa được ACK, không gửi lại complete đã nhận. EN: Keep immutable files/grants in memory and resume at the first unacknowledged block. */
export function audioUpload(
  file: File,
  purpose: "lesson_audio" | "recording",
  attribution: string,
  progress: (bytes: number, total: number) => void = () => {},
) {
  const actor = sessionBinding();
  const command = crypto.randomUUID();
  const mime =
    file.type.split(";")[0].toLowerCase() ||
    (
      {
        mp3: "audio/mpeg",
        wav: "audio/wav",
        ogg: "audio/ogg",
        webm: "audio/webm",
        m4a: "audio/mp4",
      } as Record<string, string>
    )[file.name.split(".").at(-1)?.toLowerCase() ?? ""];
  let grant: Grant | undefined;
  let next = 0;
  let committed = false;
  let completed = false;
  let running = false;
  const check = (signal: AbortSignal) => {
    signal.throwIfAborted();
    const current = sessionBinding();
    if (
      !actor.actor ||
      actor.checking ||
      current.checking ||
      current.actor !== actor.actor ||
      current.epoch !== actor.epoch
    )
      throw new ApiError(
        409,
        "session_changed",
        "Tài khoản đang được xác minh hoặc đã đổi. Dừng phiên gửi cũ.",
      );
  };
  const send = async (signal: AbortSignal) => {
    if (running) throw new Error("Phiên gửi đang chạy.");
    running = true;
    try {
      check(signal);
      if (
        !file.size ||
        file.size > (purpose === "recording" ? 20 : 100) * 1024 * 1024 ||
        ![
          "audio/mpeg",
          "audio/wav",
          "audio/x-wav",
          "audio/ogg",
          "audio/webm",
          "audio/mp4",
        ].includes(mime) ||
        !attribution.trim()
      )
        throw new Error(
          "Cần file audio hợp lệ, nguồn rõ ràng; tối đa20 MiB cho ghi âm hoặc100 MiB cho bài học.",
        );
      if (!grant) {
        const response = await api<Grant>("/media/uploads", {
          method: "POST",
          body: JSON.stringify({
            clientRequestId: command,
            purpose,
            fileName: file.name,
            contentType: mime,
            bytes: file.size,
            attribution,
          }),
          signal,
        });
        check(signal);
        grant = response;
      }
      if (completed) return grant.assetId;
      if (Date.parse(grant.expiresAt) <= Date.now())
        throw new Error(
          "Phiên gửi đã hết hạn. Tạo phiên mới; bản đã gửi vẫn có thể đang xử lý nếu xác nhận trước đó bị mất.",
        );
      const count = Math.ceil(file.size / audioBlockBytes);
      const ids = Array.from({ length: count }, (_, index) =>
        btoa(
          `${command.replaceAll("-", "")}:${String(index).padStart(6, "0")}`,
        ),
      );
      while (!committed && next < count) {
        check(signal);
        const url = new URL(grant.uploadUrl);
        url.searchParams.set("comp", "block");
        url.searchParams.set("blockid", ids[next]);
        const result = await fetch(url, {
          method: "PUT",
          credentials: "omit",
          referrerPolicy: "no-referrer",
          headers: {
            "x-ms-version": "2023-11-03",
            "Content-Type": "application/octet-stream",
          },
          body: file.slice(
            next * audioBlockBytes,
            Math.min(file.size, (next + 1) * audioBlockBytes),
          ),
          signal,
        });
        check(signal);
        if (!result.ok)
          throw new Error(
            `Chưa gửi được một phần audio (HTTP ${result.status}). Bấm gửi lại để tiếp tục.`,
          );
        next++;
        progress(Math.min(file.size, next * audioBlockBytes), file.size);
      }
      if (!committed) {
        check(signal);
        const url = new URL(grant.uploadUrl);
        url.searchParams.set("comp", "blocklist");
        const result = await fetch(url, {
          method: "PUT",
          credentials: "omit",
          referrerPolicy: "no-referrer",
          headers: {
            "x-ms-version": "2023-11-03",
            "x-ms-blob-content-type": mime,
            "Content-Type": "application/xml",
          },
          body: `<?xml version="1.0" encoding="utf-8"?><BlockList>${ids.map((id) => `<Latest>${id}</Latest>`).join("")}</BlockList>`,
          signal,
        });
        check(signal);
        if (!result.ok)
          throw new Error(
            `Chưa xác nhận đủ các phần audio (HTTP ${result.status}). Có thể gửi lại.`,
          );
        committed = true;
      }
      check(signal);
      await api<void>(`/media/${grant.assetId}/complete`, {
        method: "POST",
        body: "{}",
        signal,
      });
      check(signal);
      completed = true;
      return grant.assetId;
    } finally {
      running = false;
    }
  };
  return { send };
}

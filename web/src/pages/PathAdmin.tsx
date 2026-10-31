import { useState } from "react";
import { api, post, type Content } from "../api";
import { ErrorNotice, useResource } from "../hooks";
import { useAuth } from "../Auth";

type PathContent = {
  title: string;
  summary: string;
  attribution: string;
  stages: unknown[];
};
type PathDraft = PathContent & {
  id: string;
  templateId: string;
  status: string;
  version: number;
  etag: string;
};
type PathRow = {
  id: string;
  templateId: string;
  title: string;
  slug: string;
  level: string;
  version: number;
  status: string;
};

/** VI: CMS graph giữ ETag; chỉ Admin publish sau kiểm tra DAG. EN: The graph editor uses ETags, server DAG validation and administrator-only publication. */
export function PathAdmin() {
  const list = useResource<PathRow[]>("/admin/paths");
  const content = useResource<Content[]>("/catalog");
  const { user } = useAuth();
  const [selected, setSelected] = useState<PathDraft>();
  const [slug, setSlug] = useState("");
  const [level, setLevel] = useState("foundation");
  const [json, setJson] = useState("{}");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<Error>();
  const [message, setMessage] = useState("");
  const load = async (id: string) => {
    const draft = await api<PathDraft>(`/admin/path-versions/${id}`);
    setSelected(draft);
    setJson(
      JSON.stringify(
        {
          title: draft.title,
          summary: draft.summary,
          attribution: draft.attribution,
          stages: draft.stages,
        },
        null,
        2,
      ),
    );
  };
  const perform = async (
    action: "create" | "save" | "new" | "validate" | "publish",
  ) => {
    setBusy(true);
    setError(undefined);
    setMessage("");
    try {
      if (action === "validate") {
        const result = await post<{ topologicalOrder: string[] }>(
          `/admin/path-versions/${selected!.id}/validate`,
          {},
        );
        setMessage(
          `Graph hợp lệ: ${result.topologicalOrder.length} node; không có chu trình và có nguồn completion.`,
        );
      } else {
        const result =
          action === "create"
            ? await post<{ id: string }>("/admin/paths", {
                slug,
                level,
                content: JSON.parse(json),
              })
            : action === "new"
              ? await post<{ id: string }>(
                  `/admin/paths/${selected!.templateId}/versions`,
                  JSON.parse(json),
                )
              : await api<{ id: string }>(
                  `/admin/path-versions/${selected!.id}${action === "publish" ? "/publish" : ""}`,
                  {
                    method: action === "publish" ? "POST" : "PUT",
                    headers: { "If-Match": selected!.etag },
                    body: action === "save" ? json : undefined,
                  },
                );
        await load(result.id);
        list.reload();
        setMessage(
          action === "publish"
            ? "Đã publish. Enrollment đang có vẫn giữ version đã ghim."
            : "Đã lưu bản nháp lộ trình.",
        );
      }
    } catch (e) {
      setError(e as Error);
    } finally {
      setBusy(false);
    }
  };
  const seedEditor = () => {
    const first = content.data?.find((item) => item.kind === "lesson");
    if (!first) return;
    setSelected(undefined);
    setSlug("");
    setMessage("");
    setError(undefined);
    setJson(
      JSON.stringify(
        {
          title: "Mẫu lộ trình mới",
          summary: "Lịch học tự biên soạn.",
          attribution: "Original project author; describe usage rights.",
          stages: [
            {
              key: "first-steps",
              title: "Bước đầu",
              nodes: [
                {
                  key: "lesson-01",
                  contentVersionId: first.id,
                  studyDay: 1,
                  completionRule: "lesson_quiz",
                  minimumCorrectPercent: 100,
                  prerequisites: [],
                },
              ],
            },
          ],
        },
        null,
        2,
      ),
    );
  };
  return (
    <section className="cms-import">
      <h2>Mẫu lộ trình và prerequisite</h2>
      <p>
        Stage → node → ContentVersion. Key prerequisite thuộc cùng version.
        Publish giữ nguyên nội dung, ngày học và quy tắc completion.
      </p>
      <ErrorNotice
        error={error ?? list.error ?? content.error}
        retry={list.reload}
      />
      {message ? (
        <p className="notice" role="status">
          {message}
        </p>
      ) : null}
      <div className="button-row">
        <button
          className="secondary"
          disabled={busy || !content.data}
          onClick={seedEditor}
        >
          Tạo mẫu từ một bài đã publish
        </button>
      </div>
      <div className="cms-path-versions">
        {list.data?.map((item) => (
          <button
            key={item.id}
            className="secondary"
            disabled={busy}
            onClick={() => {
              setError(undefined);
              load(item.id).catch(setError);
            }}
          >
            {item.title} · v{item.version} · {item.status}
          </button>
        ))}
      </div>
      {!selected ? (
        <div className="cms-new-fields">
          <label>
            Slug mẫu
            <input value={slug} onChange={(e) => setSlug(e.target.value)} />
          </label>
          <label>
            Nhóm trình độ
            <select value={level} onChange={(e) => setLevel(e.target.value)}>
              <option value="foundation">Foundation</option>
              <option value="core">Core</option>
              <option value="exam_practice">Exam Practice</option>
            </select>
          </label>
        </div>
      ) : (
        <p>
          Version {selected.version} · {selected.status}
        </p>
      )}
      <label className="editor-label">
        Graph lộ trình (JSON)
        <textarea
          rows={18}
          spellCheck={false}
          value={json}
          disabled={busy}
          onChange={(e) => setJson(e.target.value)}
        />
      </label>
      <div className="button-row">
        {selected ? (
          <>
            <button
              className="secondary"
              disabled={busy || selected.status !== "draft"}
              onClick={() => perform("save")}
            >
              Lưu graph nháp
            </button>
            <button
              className="secondary"
              disabled={busy}
              onClick={() => perform("new")}
            >
              Tạo version lộ trình mới
            </button>
            <button
              className="secondary"
              disabled={busy}
              onClick={() => perform("validate")}
            >
              Kiểm tra DAG và nguồn
            </button>
            {user?.roles.includes("Admin") ? (
              <button
                className="primary"
                disabled={busy || selected.status !== "draft"}
                onClick={() => perform("publish")}
              >
                Publish lộ trình
              </button>
            ) : null}
          </>
        ) : (
          <button
            className="primary"
            disabled={busy || !slug}
            onClick={() => perform("create")}
          >
            Lưu template nháp
          </button>
        )}
      </div>
    </section>
  );
}

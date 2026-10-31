import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api, currentUser, post, resetCsrf } from "../src/api";
import {
  confirmSession,
  onSessionInvalidated,
  suspendSession,
} from "../src/authSession";

const actorA = "11111111-1111-1111-1111-111111111111";
const actorB = "22222222-2222-2222-2222-222222222222";
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status });
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

describe("actor-bound transport", () => {
  beforeEach(() => {
    suspendSession(true);
    confirmSession(actorA);
    resetCsrf();
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("binds reads and writes to the confirmed actor", async () => {
    const fetcher = vi.fn(async (url: string) =>
      url.endsWith("/csrf")
        ? json({ requestToken: "token-A" })
        : json({ saved: true }),
    );
    vi.stubGlobal("fetch", fetcher);
    await api("/cards/");
    await post("/cards/", { front: "Private A" });
    for (const call of fetcher.mock.calls.filter(
      ([url]) => !url.endsWith("/csrf"),
    )) {
      const init = (call as unknown as [string, RequestInit])[1];
      expect(new Headers(init.headers).get("X-EnglishHub-Actor")).toBe(actorA);
      expect(init.cache).toBe("no-store");
    }
  });

  it("does not send an old payload after identity changes while awaiting CSRF", async () => {
    const token = deferred<Response>();
    const fetcher = vi.fn(() => token.promise);
    vi.stubGlobal("fetch", fetcher);
    const result = post("/cards/", { front: "Private A draft" });
    const assertion = expect(result).rejects.toMatchObject({
      status: 0,
      code: "session_changed",
    });
    suspendSession(true);
    confirmSession(actorB);
    token.resolve(json({ requestToken: "token-B" }));
    await assertion;
    expect(fetcher).toHaveBeenCalledTimes(1);
  });

  it("retains the original actor through a legitimate same-account CSRF retry", async () => {
    let tokens = 0;
    let writes = 0;
    const fetcher = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith("/csrf"))
        return json({ requestToken: `token-${++tokens}` });
      expect(new Headers(init?.headers).get("X-EnglishHub-Actor")).toBe(actorA);
      return ++writes === 1
        ? json({ code: "invalid_csrf" }, 400)
        : json({ saved: true });
    });
    vi.stubGlobal("fetch", fetcher);
    expect(await post("/cards/", { front: "A" })).toEqual({ saved: true });
    expect(tokens).toBe(2);
    expect(writes).toBe(2);
  });

  it("cannot retry under B after the first request's invalid-CSRF response", async () => {
    const oldResponse = deferred<Response>();
    const fetcher = vi.fn((url: string) =>
      url.endsWith("/csrf")
        ? Promise.resolve(json({ requestToken: "token-A" }))
        : oldResponse.promise,
    );
    vi.stubGlobal("fetch", fetcher);
    const result = post("/cards/", { front: "A" });
    const assertion = expect(result).rejects.toMatchObject({
      status: 0,
      code: "session_changed",
    });
    await vi.waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2));
    suspendSession(true);
    confirmSession(actorB);
    oldResponse.resolve(json({ code: "invalid_csrf" }, 400));
    await assertion;
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("blocks new business requests during revalidation but permits the identity probe", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => json({ id: actorA })),
    );
    suspendSession();
    await expect(api("/cards/")).rejects.toMatchObject({
      status: 0,
      code: "session_revalidating",
    });
    expect((await currentUser(new AbortController().signal)).id).toBe(actorA);
  });

  it("does not apply an old successful response after switching actors", async () => {
    const old = deferred<Response>();
    vi.stubGlobal(
      "fetch",
      vi.fn(() => old.promise),
    );
    const result = api("/cards/");
    const assertion = expect(result).rejects.toMatchObject({
      status: 0,
      code: "session_changed",
    });
    suspendSession(true);
    confirmSession(actorB);
    old.resolve(json({ secret: "A" }));
    await assertion;
  });

  it.each([401, 409])(
    "revalidates on server identity loss (%s) without a business-conflict status",
    async (status) => {
      vi.stubGlobal(
        "fetch",
        vi.fn(async () =>
          json(
            { code: status === 409 ? "session_changed" : "unauthenticated" },
            status,
          ),
        ),
      );
      const changed = vi.fn();
      const unsubscribe = onSessionInvalidated(changed);
      try {
        await expect(api("/cards/")).rejects.toMatchObject({
          status: 0,
          code: "session_changed",
        });
        expect(changed).toHaveBeenCalledTimes(1);
      } finally {
        unsubscribe();
      }
    },
  );

  it("guards logout too and leaves unrelated validation errors intact", async () => {
    const fetcher = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith("/csrf")) return json({ requestToken: "A" });
      expect(new Headers(init?.headers).get("X-EnglishHub-Actor")).toBe(actorA);
      return json({ code: "validation", detail: "Keep draft" }, 422);
    });
    vi.stubGlobal("fetch", fetcher);
    await expect(post("/auth/logout")).rejects.toMatchObject({
      status: 422,
      code: "validation",
      message: "Keep draft",
    });
  });
});

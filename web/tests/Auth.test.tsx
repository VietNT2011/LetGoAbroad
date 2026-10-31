// @vitest-environment jsdom
import { act, StrictMode, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider, RequireAuth, useAuth } from "../src/Auth";
import { authSignalKey, authSignals, sessionBinding } from "../src/authSession";
import { type User } from "../src/api";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
const actorA: User = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "a@example.com",
  displayName: "Account A",
  timeZoneId: "UTC",
  targetScore: 650,
  minutesPerDay: 30,
  studyDays: "1,2",
  profileEtag: "A",
  roles: ["Learner"],
};
const actorB: User = {
  ...actorA,
  id: "22222222-2222-2222-2222-222222222222",
  email: "b@example.com",
  displayName: "Account B",
};
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status });
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: Error) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}
class Channel {
  static instances: Channel[] = [];
  onmessage?: (event: MessageEvent) => void;
  closed = false;
  sent: unknown[] = [];
  constructor() {
    Channel.instances.push(this);
  }
  postMessage(packet: unknown) {
    this.sent.push(packet);
  }
  close() {
    this.closed = true;
  }
  static remote(nonce = crypto.randomUUID()) {
    for (const channel of this.instances)
      if (!channel.closed)
        channel.onmessage?.(
          new MessageEvent("message", { data: { version: 1, nonce } }),
        );
  }
}
function PrivateDraft() {
  const { user } = useAuth();
  const [draft, setDraft] = useState("empty");
  return (
    <section data-private>
      <span>{user?.displayName}</span>
      <output>{draft}</output>
      <button onClick={() => setDraft("A private draft")}>Edit draft</button>
    </section>
  );
}
function Controls() {
  const { refresh, afterLogin, logout } = useAuth();
  return (
    <>
      <button
        onClick={() => {
          void refresh().catch(() => {});
        }}
      >
        Refresh
      </button>
      <button
        onClick={() => {
          void afterLogin().catch(() => {});
        }}
      >
        After login
      </button>
      <button
        onClick={() => {
          void logout().catch(() => {});
        }}
      >
        Logout
      </button>
    </>
  );
}
let root: Root;
let container: HTMLDivElement;
let me: () => Promise<Response>;
let fetcher: ReturnType<typeof vi.fn>;
const flush = async () => {
  await act(async () => {
    for (let index = 0; index < 6; index++) await Promise.resolve();
  });
};
async function click(text: string) {
  const button = [...container.querySelectorAll("button")].find(
    (item) => item.textContent === text,
  )!;
  expect(button).toBeTruthy();
  await act(async () => button.click());
}
async function mount(strict = false) {
  const tree = (
    <MemoryRouter initialEntries={["/dashboard"]}>
      <AuthProvider>
        <Controls />
        <Routes>
          <Route
            path="/dashboard"
            element={
              <RequireAuth>
                <PrivateDraft />
              </RequireAuth>
            }
          />
          <Route path="/login" element={<p>Signed out</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>
  );
  await act(async () =>
    root.render(strict ? <StrictMode>{tree}</StrictMode> : tree),
  );
  await flush();
}

describe("cross-tab identity lifecycle", () => {
  beforeEach(() => {
    Channel.instances = [];
    vi.stubGlobal("BroadcastChannel", Channel);
    me = async () => json(actorA);
    fetcher = vi.fn(async (url: string) =>
      url.endsWith("/me")
        ? me()
        : url.endsWith("/csrf")
          ? json({ requestToken: "token" })
          : new Response(null, { status: 204 }),
    );
    vi.stubGlobal("fetch", fetcher);
    localStorage.clear();
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
  });

  it("clears the old private tree immediately on a remote change and mounts B with an empty draft", async () => {
    await mount();
    await click("Edit draft");
    const next = deferred<Response>();
    me = () => next.promise;
    await act(async () => Channel.remote());
    expect(container.querySelector("[data-private]")).toBeNull();
    expect(sessionBinding().checking).toBe(true);
    await act(async () => next.resolve(json(actorB)));
    await flush();
    expect(container.textContent).toContain("Account B");
    expect(container.querySelector("output")?.textContent).toBe("empty");
    expect(sessionBinding().actor).toBe(actorB.id);
  });

  it("hides and disables the same-actor draft during focus verification, then preserves it", async () => {
    await mount();
    await click("Edit draft");
    const draft = container.querySelector("[data-private]");
    const next = deferred<Response>();
    me = () => next.promise;
    await act(async () => window.dispatchEvent(new Event("focus")));
    expect(draft?.parentElement?.hidden).toBe(true);
    expect(draft?.parentElement?.hasAttribute("inert")).toBe(true);
    await act(async () => next.resolve(json(actorA)));
    await flush();
    expect(container.querySelector("[data-private]")).toBe(draft);
    expect(container.querySelector("output")?.textContent).toBe(
      "A private draft",
    );
    expect(draft?.parentElement?.hidden).toBe(false);
  });

  it("replaces retained private state when focus discovers a different actor without a message", async () => {
    await mount();
    await click("Edit draft");
    const previous = container.querySelector("[data-private]");
    me = async () => json(actorB);
    await act(async () => window.dispatchEvent(new Event("focus")));
    await flush();
    expect(container.querySelector("[data-private]")).not.toBe(previous);
    expect(container.textContent).toContain("Account B");
    expect(container.querySelector("output")?.textContent).toBe("empty");
  });

  it("ignores a stale bootstrap response even if fetch disregards abort", async () => {
    const old = deferred<Response>();
    me = () => old.promise;
    await mount();
    me = async () => json(actorB);
    await act(async () => Channel.remote());
    await flush();
    await act(async () => old.resolve(json(actorA)));
    await flush();
    expect(container.textContent).toContain("Account B");
    expect(container.textContent).not.toContain("Account A");
  });

  it("keeps a failed verification hidden and allows retry without discarding same-owner work", async () => {
    await mount();
    await click("Edit draft");
    me = async () => {
      throw new Error("Offline verification");
    };
    await act(async () => window.dispatchEvent(new Event("focus")));
    await flush();
    expect(container.querySelector("[role=alert]")?.textContent).toContain(
      "Offline verification",
    );
    expect(
      container.querySelector("[data-private]")?.parentElement?.hidden,
    ).toBe(true);
    expect(container.textContent).not.toContain("Signed out");
    me = async () => json(actorA);
    await click("Thử lại");
    await flush();
    expect(container.querySelector("output")?.textContent).toBe(
      "A private draft",
    );
  });

  it("uses storage fallback and deduplicates a packet delivered by both channels", async () => {
    await mount();
    const before = fetcher.mock.calls.length;
    const packet = { version: 1, nonce: "same-event" };
    await act(async () => {
      Channel.remote(packet.nonce);
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: authSignalKey,
          newValue: JSON.stringify(packet),
        }),
      );
    });
    await flush();
    expect(fetcher.mock.calls.length).toBe(before + 1);
  });

  it("revalidates on restored pages and clears role-specific component state", async () => {
    await mount();
    await click("Edit draft");
    me = async () => json({ ...actorA, roles: ["Admin", "Learner"] });
    await act(async () =>
      window.dispatchEvent(
        new PageTransitionEvent("pageshow", { persisted: true }),
      ),
    );
    await flush();
    expect(container.querySelector("output")?.textContent).toBe("empty");
  });

  it("publishes only a nonce on login/logout, while profile refresh does not broadcast", async () => {
    await mount();
    await click("Refresh");
    await flush();
    expect(Channel.instances[0].sent).toHaveLength(0);
    await click("After login");
    await flush();
    expect(Channel.instances[0].sent).toHaveLength(1);
    const packet = Channel.instances[0].sent[0] as Record<string, unknown>;
    expect(Object.keys(packet).sort()).toEqual(["nonce", "version"]);
    expect(JSON.stringify(packet)).not.toContain(actorA.id);
    me = async () => json({ code: "unauthenticated" }, 401);
    await click("Logout");
    await flush();
    expect(Channel.instances[0].sent).toHaveLength(2);
    expect(container.textContent).toContain("Signed out");
  });

  it("cleans StrictMode listeners/channels and ignores stale failed requests", async () => {
    await mount(true);
    expect(Channel.instances.filter((channel) => !channel.closed)).toHaveLength(
      1,
    );
    const old = deferred<Response>();
    me = () => old.promise;
    await act(async () => window.dispatchEvent(new Event("focus")));
    me = async () => json(actorB);
    await act(async () => Channel.remote());
    await flush();
    await act(async () => old.reject(new Error("Obsolete outage")));
    await flush();
    expect(container.textContent).toContain("Account B");
    expect(container.textContent).not.toContain("Obsolete outage");
  });

  it("continues with storage and focus when BroadcastChannel is unavailable", async () => {
    vi.stubGlobal(
      "BroadcastChannel",
      class {
        constructor() {
          throw new Error("Unavailable");
        }
      },
    );
    await mount();
    me = async () => json(actorB);
    await act(async () =>
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: authSignalKey,
          newValue: JSON.stringify({ version: 1, nonce: "fallback" }),
        }),
      ),
    );
    await flush();
    expect(container.textContent).toContain("Account B");
  });

  it("removes storage listeners on disposal and ignores malformed packets", () => {
    const changed = vi.fn();
    const signal = authSignals(changed);
    window.dispatchEvent(
      new StorageEvent("storage", { key: authSignalKey, newValue: "not JSON" }),
    );
    expect(changed).not.toHaveBeenCalled();
    signal.dispose();
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: authSignalKey,
        newValue: JSON.stringify({ version: 1, nonce: "after-dispose" }),
      }),
    );
    expect(changed).not.toHaveBeenCalled();
  });
});

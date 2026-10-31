// @vitest-environment jsdom
import { act } from "react";
import { createRoot } from "react-dom/client";
import { it, expect, vi } from "vitest";
import { api } from "../src/api";
import { useResource } from "../src/hooks";
vi.mock("../src/api", async (original) => ({
  ...(await original<typeof import("../src/api")>()),
  api: vi.fn(),
}));
Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
it("never exposes an old snapshot under a new or disabled resource path", async () => {
  let resolveLate!: (value: unknown) => void;
  const observations: { path: string | null; data: string | undefined }[] = [];
  vi.mocked(api).mockImplementation((path) =>
    path === "/first"
      ? Promise.resolve("First source")
      : new Promise((resolve) => {
          resolveLate = resolve;
        }),
  );
  function Probe({ path }: { path: string | null }) {
    const resource = useResource<string>(path);
    observations.push({ path, data: resource.data });
    return <p>{resource.data}</p>;
  }
  const container = document.createElement("div");
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<Probe path="/first" />));
    expect(container.textContent).toBe("First source");
    await act(async () => root.render(<Probe path="/second" />));
    expect(
      observations
        .filter((x) => x.path === "/second")
        .every((x) => x.data !== "First source"),
    ).toBe(true);
    await act(async () => root.render(<Probe path={null} />));
    expect(
      observations
        .filter((x) => x.path === null)
        .every((x) => x.data === undefined),
    ).toBe(true);
    await act(async () => resolveLate("Obsolete source"));
    expect(container.textContent).toBe("");
  } finally {
    await act(async () => root.unmount());
    container.remove();
  }
});

export type RecordingSourceSuggestion = {
  versionId: string;
  segmentId: string | null;
};
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** VI: URL chỉ đề xuất ngữ cảnh; backend quyết định quyền và nguồn audio. EN: URL values suggest context only; the server authorizes and resolves the sample. */
export function recordingSourceSuggestion(
  version: string | null,
  segment: string | null,
): RecordingSourceSuggestion | undefined {
  if (
    !version ||
    !uuid.test(version) ||
    (segment !== null && !uuid.test(segment))
  )
    return undefined;
  return {
    versionId: version.toLowerCase(),
    segmentId: segment?.toLowerCase() ?? null,
  };
}

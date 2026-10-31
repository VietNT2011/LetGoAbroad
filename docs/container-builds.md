# Container build inputs / Input build container

VI: Dockerfile trước đây copy toàn repo vào stage .NET, nên sửa UI, tài liệu hoặc test cũng làm mất cache publish API/worker. Các stage nay chỉ copy input thực sự dùng; package restore trong publish bật `RestoreLockedMode=true`.

| Input | Frontend build | API publish | Worker publish |
|---|---|---|---|
| Web package/lock, index, TypeScript/Vite config, web/src | Yes | No | No |
| global.json, Directory.Build.props | No | Yes | Yes |
| Domain, Application, Infrastructure and seed JSON | No | Yes | Yes |
| API project | No | Yes | No |
| Worker project | No | No | Yes |
| Docs and test sources | No | No | No |

The embedded Foundation/Core/Exam/listening seed files remain build inputs. `seed/audio` is explicitly copied by both Dockerfiles because the Infrastructure assembly embeds its MP3s and source notices. Existing pinned base-image digests, non-root runtimes, NuGet cache mount, runtime environment/volumes, worker FFmpeg and API static output are retained. New root MSBuild imports or static-asset directories must be added to the corresponding COPY list when introduced; current frontend has no public directory or external source import.

Local checks publish both services with locked restore and launch to readiness200. An isolated copy of tracked files (including the candidate Dockerfiles) modifies inputs without changing live source: frontend-only changes preserve API publish cache; API-only changes preserve worker publish cache; changed embedded seed JSON forces API publication. All three expected cache decisions and builds pass. These are cache-dependency checks, not a general build-time or runtime performance benchmark. Runtime learner-progress checks supplement the build and no database migration is involved.

The first diagnostic exported full SDK target images and increased disk pressure. Future diagnostic runs use the cache-only exporter instead of loading SDK images. The temporary tags were removed. Scoped cache cleanup targets only inspected EnglishHub publish record IDs; no global prune or container/volume reset is used. Windows host free space can remain low after Linux cache deletion because the Docker VHDX has not compacted; local handover records the observed disk state and recovery separately. The shipped Dockerfiles build the normal runtime images.

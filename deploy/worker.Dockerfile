FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /source
# VI: Worker độc lập với UI, endpoint API và test; seed nhúng vẫn phải có. EN: Worker publish excludes frontend/API/test inputs while retaining embedded seeds.
COPY global.json Directory.Build.props ./
COPY src/EnglishHub.Domain/ ./src/EnglishHub.Domain/
COPY src/EnglishHub.Application/ ./src/EnglishHub.Application/
COPY src/EnglishHub.Infrastructure/ ./src/EnglishHub.Infrastructure/
COPY src/EnglishHub.Worker/ ./src/EnglishHub.Worker/
COPY seed/*.json ./seed/
RUN --mount=type=cache,id=englishhub-build-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish src/EnglishHub.Worker -c Release -o /out -p:RestoreLockedMode=true
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "EnglishHub.Worker.dll"]

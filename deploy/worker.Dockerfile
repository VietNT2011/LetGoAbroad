FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /source
COPY . .
RUN --mount=type=cache,id=englishhub-build-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish src/EnglishHub.Worker -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "EnglishHub.Worker.dll"]

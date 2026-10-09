FROM node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914 AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /source
COPY . .
RUN --mount=type=cache,id=englishhub-build-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish src/EnglishHub.Api -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
WORKDIR /app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown app:app /home/app/.aspnet/DataProtection-Keys
COPY --from=build /out .
COPY --from=web /web/dist ./wwwroot
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "EnglishHub.Api.dll"]

FROM node:22-alpine@sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2 AS ui-build
WORKDIR /ui

COPY src/Tracebag.Web/package*.json ./
RUN npm ci

COPY src/Tracebag.Web ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:89ce6291bde9acdf59594e79fb8277c6d84c46e4b1f5bf126a4f18766e4bd597 AS backend-build
WORKDIR /src

COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/Tracebag.Api/Tracebag.Api.csproj src/Tracebag.Api/
RUN dotnet restore src/Tracebag.Api/Tracebag.Api.csproj

COPY src/Tracebag.Api src/Tracebag.Api
RUN dotnet publish src/Tracebag.Api/Tracebag.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS runtime
WORKDIR /app

COPY --from=backend-build /app/publish ./
COPY --from=ui-build /ui/dist/tracebag-web/browser ./wwwroot
COPY --from=ui-build /ui/dist/tracebag-web/3rdpartylicenses.txt ./wwwroot/3rdpartylicenses.txt
COPY LICENSE THIRD_PARTY_NOTICES.md ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "Tracebag.Api.dll"]

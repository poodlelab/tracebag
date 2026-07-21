FROM node:26-alpine@sha256:e88a35be04478413b7c71c455cd9865de9b9360e1f43456be5951032d7ac1a66 AS ui-build
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

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine@sha256:b02ab6637e02dfe07d4205d557cbce7e2ab0e4a1d7d1285868b4f31eed20bd10 AS runtime
WORKDIR /app

COPY --from=backend-build /app/publish ./
COPY --from=ui-build /ui/dist/tracebag-web/browser ./wwwroot
COPY --from=ui-build /ui/dist/tracebag-web/3rdpartylicenses.txt ./wwwroot/3rdpartylicenses.txt
COPY LICENSE THIRD_PARTY_NOTICES.md ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "Tracebag.Api.dll"]

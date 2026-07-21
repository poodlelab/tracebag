FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:89ce6291bde9acdf59594e79fb8277c6d84c46e4b1f5bf126a4f18766e4bd597 AS build
WORKDIR /src

COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj demo/Tracebag.Demo.Api/
RUN dotnet restore demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj

COPY demo/Tracebag.Demo.Api demo/Tracebag.Demo.Api
RUN dotnet publish demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:27b6b84beeede74fd16886177d360799c8e4299ceadfbd64eef57bafead7878a AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY LICENSE THIRD_PARTY_NOTICES.md ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "Tracebag.Demo.Api.dll"]

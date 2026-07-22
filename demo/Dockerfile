FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
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

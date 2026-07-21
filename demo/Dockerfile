FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /src

COPY .editorconfig Directory.Build.props Directory.Packages.props ./
COPY demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj demo/Tracebag.Demo.Api/
RUN dotnet restore demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj

COPY demo/Tracebag.Demo.Api demo/Tracebag.Demo.Api
RUN dotnet publish demo/Tracebag.Demo.Api/Tracebag.Demo.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine@sha256:b02ab6637e02dfe07d4205d557cbce7e2ab0e4a1d7d1285868b4f31eed20bd10 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY LICENSE THIRD_PARTY_NOTICES.md ./

EXPOSE 8080
ENTRYPOINT ["dotnet", "Tracebag.Demo.Api.dll"]

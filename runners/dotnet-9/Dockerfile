FROM mcr.microsoft.com/dotnet/sdk:9.0@sha256:ee0d20fbc3dfc60ae2d9d9115a9b750c18576a47f24073119dd09d93e072cb89 AS tools

ARG DIAGNOSTICS_TOOLS_VERSION=9.0.661903

RUN dotnet tool install -g dotnet-counters --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-trace --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-dump --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-gcdump --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-stack --version "${DIAGNOSTICS_TOOLS_VERSION}"

ENV PATH="/root/.dotnet/tools:${PATH}"

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble@sha256:ed5d539b27842d656a06a5984dbcb5114d3e885fbada612a49a5a7c3c3a44e1c

ENV PATH="/root/.dotnet/tools:${PATH}" \
    DOTNET_CLI_HOME=/tmp/tracebag-dotnet-cli \
    DOTNET_NOLOGO=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

COPY --from=tools /root/.dotnet/tools /root/.dotnet/tools

WORKDIR /work

COPY runners/common/entrypoint.sh /usr/local/bin/tracebag-runner
COPY LICENSE THIRD_PARTY_NOTICES.md /work/
RUN chmod 0555 /usr/local/bin/tracebag-runner

ENTRYPOINT ["/usr/local/bin/tracebag-runner"]

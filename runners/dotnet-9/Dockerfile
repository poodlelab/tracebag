FROM mcr.microsoft.com/dotnet/sdk:9.0@sha256:cb9d975bf57fd1b0915858d1db1184bea20f7f746f0536323fcab49673144e8c AS tools

ARG DIAGNOSTICS_TOOLS_VERSION=9.0.661903

RUN dotnet tool install -g dotnet-counters --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-trace --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-dump --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-gcdump --version "${DIAGNOSTICS_TOOLS_VERSION}" \
    && dotnet tool install -g dotnet-stack --version "${DIAGNOSTICS_TOOLS_VERSION}"

ENV PATH="/root/.dotnet/tools:${PATH}"

FROM mcr.microsoft.com/dotnet/runtime:8.0-noble@sha256:0f038d362c97c1d34ce2481891e7dadfcbb67a014fed7e010ba2ef5bbb9c5820

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

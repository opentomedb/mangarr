# Mangarr image (beta readiness 2026-09-28, D3: was Dockerfile.dev). Base images pinned by tag -- the
# last .NET 6 releases, on Debian 12 (bookworm) for libicu72; the plain 6.0 tags are Debian 11.
#   docker build --build-arg MANGARR_VERSION=10.0.0.N -t mangarr-dev:latest .
FROM mcr.microsoft.com/dotnet/sdk:6.0.428-1-bookworm-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_18.x | bash - \
 && apt-get install -y nodejs && npm install -g yarn && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile --ignore-engines --network-timeout 600000
COPY . .
RUN NODE_OPTIONS=--max-old-space-size=4096 yarn run build --env production
# Release, not Debug: a Debug build runs with RuntimeInfo.IsProduction=false, which flips
# HttpRequest.AllowAutoRedirect off GLOBALLY ("developer mode") — every redirecting indexer
# nzb download (an indexer's getnzb) then fails with "Server requested a redirect".
# AssemblyVersion: without a concrete value every assembly expands the 10.0.0.* wildcard
# to its own compile-time revision, so the API and the log lines report different builds.
ARG MANGARR_VERSION=10.0.0.0
RUN dotnet build src/Readarr.sln -c Release -p:Platform=Posix -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -p:AssemblyVersion=$MANGARR_VERSION
# deploy.sh bakes changelog.json (recent commits) for the System->Updates page; an ad-hoc
# build without it gets an empty stub so the COPY below never fails and the page shows none.
RUN [ -f changelog.json ] || echo '{"generated":null,"commits":[]}' > changelog.json

FROM mcr.microsoft.com/dotnet/aspnet:6.0.36-bookworm-slim AS run
ARG MANGARR_VERSION=10.0.0.0
LABEL org.opencontainers.image.title="Mangarr" \
      org.opencontainers.image.source="https://github.com/opentomedb/mangarr" \
      org.opencontainers.image.licenses="GPL-3.0-only" \
      org.opencontainers.image.version="$MANGARR_VERSION"
# Readarr needs native SQLite + libicu (globalization) — not in aspnet-slim.
# gosu lets the entrypoint drop from root to PUID:PGID like the rest of the *arr stack.
RUN apt-get update && apt-get install -y --no-install-recommends libsqlite3-0 libicu72 gosu poppler-utils \
 && rm -rf /var/lib/apt/lists/*
# Release layout: ConfigFileProvider.UiFolder resolves "UI" INSIDE the startup folder
# for non-debug builds (Debug used the sibling ../UI), so the UI lives at /app/bin/UI.
COPY --from=build /src/_output/net6.0/ /app/bin/
COPY --from=build /src/_output/UI/ /app/bin/UI/
COPY --from=build /src/changelog.json /app/bin/changelog.json
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
WORKDIR /app/bin
EXPOSE 8787
# Match the rest of the stack (Sonarr/Chaptarr): run as nobody:users (99:100).
ENV PUID=99 PGID=100 UMASK=002
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "/app/bin/Readarr.dll", "-nobrowser", "-data=/config"]

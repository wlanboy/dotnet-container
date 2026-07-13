# ---- Build Stage ----
# -aot-Suffix bringt Clang/Build-Toolchain für die Native-AOT-Cross-Kompilierung mit
# (im normalen sdk-Image fehlen diese Tools, AOT-Publish würde sonst fehlschlagen)
FROM mcr.microsoft.com/dotnet/sdk:10.0-aot AS build
# Eigener Ordner für den Build-Kontext, getrennt von /app im Runtime-Stage
WORKDIR /src

# 1. Nur Projekt-Datei zuerst - Layer-Caching für restore
# Solange sich die .csproj (Abhängigkeiten) nicht ändert, bleibt dieser Layer
# im Cache, auch wenn sich der Sourcecode ständig ändert -> spart Build-Zeit
COPY dotnetrest.csproj ./
# -r linux-x64 restauriert direkt für die Ziel-Runtime, passend zum publish unten
# Cache-Mount für den NuGet-Package-Cache (analog zum Maven ~/.m2-Mount):
# ohne Docker-Layer-Cache-Hit (z.B. nach Codeänderung) müssten sonst alle
# Pakete erneut aus dem Netz geladen werden
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore dotnetrest.csproj -r linux-x64

# 2. Sourcecode zuletzt kopieren
# Code ändert sich häufiger als Abhängigkeiten -> invalidiert den Cache erst hier
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish dotnetrest.csproj \
    -c Release -r linux-x64 \
    -o /app/publish
# -c Release: optimierter Build, Pflicht für Produktion
# -r linux-x64: muss mit restore und einer der RIDs in der .csproj übereinstimmen

# ---- Runtime Stage ----
# Native-AOT-Binary: kein dotnet/aspnet-Image nötig, nur die OS-Abhängigkeiten
# des kompilierten Binaries. InvariantGlobalization macht icu-libs überflüssig.
# -> kleinstmögliches Image, keine .NET-Runtime, weniger Angriffsfläche
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app

# OCI-Labels für Traceability in Registry/Pipeline (welcher Commit/Repo steckt im Image)
# REVISION wird beim Build via --build-arg REVISION=$(git rev-parse HEAD) gesetzt
ARG REVISION=unknown
LABEL org.opencontainers.image.title="dotnetrest" \
      org.opencontainers.image.description="Minimal Todo REST API (.NET 10, Native AOT)" \
      org.opencontainers.image.source="https://github.com/wlanboy/dotnet-container" \
      org.opencontainers.image.revision="${REVISION}"

# Deaktiviert Diagnose-Pipes/EventPipe (Debugger-Attach, Profiling) -
# unnötiger Angriffsvektor/Overhead in Produktion
ENV DOTNET_EnableDiagnostics=0
# Port >1024, damit der non-root User (siehe USER unten) ihn binden darf
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Nur das fertige Build-Ergebnis übernehmen - SDK/AOT-Toolchain und Sourcecode
# bleiben im verworfenen Build-Stage (Multi-Stage-Prinzip für schlanke Images)
COPY --from=build /app/publish .
# non-root User - seit .NET 8 in jedem Linux-Image enthalten (UID 1654)
# begrenzt den Schaden bei einer Container-Escape/RCE-Schwachstelle
USER $APP_UID

# Exec-Form statt Shell-Form: Binary läuft als PID 1 und empfängt Signale
# (z.B. SIGTERM bei docker stop) direkt statt über eine Shell-Zwischenschicht
ENTRYPOINT ["./dotnetrest"]
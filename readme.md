# dotnet-container

Minimale Todo-REST-API auf Basis von ASP.NET Core Minimal APIs (.NET 10), gebaut als
schlankes, produktionsnahes Container-Image mit Native AOT.

## Projektstruktur

```
dotnetrest/
├── Program.cs                     # App-Setup, Middleware, Endpunkte
├── Todo.cs                        # Todo-Record
├── TodoService.cs                 # In-Memory-Datenhaltung (Demo, kein DB-Zugriff)
├── appsettings.json                # Basis-Konfiguration (Prod)
├── appsettings.Development.json    # Override für lokale Entwicklung
├── dotnetrest.csproj
├── Dockerfile
```

## Endpunkte

| Methode | Pfad             | Zweck                          |
|---------|------------------|---------------------------------|
| GET     | `/todos`         | Alle Todos auflisten            |
| GET     | `/todos/{id}`    | Einzelnes Todo per ID           |
| GET     | `/health/live`   | Liveness-Probe (K8s)            |
| GET     | `/health/ready`  | Readiness-Probe (K8s)           |
| GET     | `/openapi/v1.json` | OpenAPI-Doku (nur in Development) |

Schreibende Endpunkte (POST/PUT/DELETE) sind aktuell nicht implementiert — die
`TodoService`-Daten sind rein statisch für Demo-Zwecke.

## Warum Native AOT?

Statt eines JIT-basierten `dotnet`-Runtime-Images wird die App zu nativem
Maschinencode kompiliert (`PublishAot=true` in der `.csproj`). Das bringt für den
Container-Betrieb:

- **Schnellerer Kaltstart** (Millisekunden statt Sekunden) — relevant bei
  Scale-to-Zero / häufigen Neustarts (K8s, Cloud Run, etc.)
- **Kleineres Image**, da keine .NET-Runtime mitgeliefert werden muss, sondern nur
  das kompilierte Binary + OS-Bibliotheken (`runtime-deps`-Basisimage)
- **Weniger Angriffsfläche**, da kein JIT/Reflection-Stack im Container läuft

Preis dafür: kein Reflection zur Laufzeit, daher `CreateSlimBuilder()` +
JSON-Source-Generator (`AppJsonSerializerContext`) statt Standard-Serializer.

## Dockerfile — Design-Entscheidungen

- **Multi-Stage-Build**: Build-Stage (`sdk:10.0-aot`, enthält die für AOT nötige
  Clang-Toolchain) und Runtime-Stage (`runtime-deps:10.0`, keine .NET-Runtime)
  sind getrennt, damit SDK/Toolchain/Sourcecode nicht im finalen Image landen.
- **Layer-Caching für `restore`**: Zuerst nur die `.csproj` kopieren und
  restaurieren, danach erst den Sourcecode. Solange sich Abhängigkeiten nicht
  ändern, bleibt der Restore-Layer im Docker-Cache erhalten.
- **NuGet-Cache-Mount** (`--mount=type=cache,target=/root/.nuget/packages`):
  zusätzlich zum Layer-Cache wird der NuGet-Paket-Cache über BuildKit gemountet,
  damit auch bei einem Cache-Miss (z.B. nach `git clean` oder auf einem neuen
  Build-Agent) nicht jedes Mal alle Pakete erneut aus dem Netz geladen werden.
- **`.dockerignore`**: verhindert, dass lokale `bin/`, `obj/`, `.git/` etc. in den
  Build-Context gelangen — sonst bläht das den Context auf und es können stale
  lokale Build-Artefakte in den Publish-Schritt einfließen.
- **Non-root User** (`USER $APP_UID`): begrenzt den Schaden im Fall einer
  Container-Escape/RCE-Schwachstelle.
- **`DOTNET_EnableDiagnostics=0`**: deaktiviert Diagnose-Pipes (Debugger-Attach,
  Profiling) — unnötiger Angriffsvektor/Overhead in Produktion.
- **Port 8080 statt 80**: liegt über 1024, damit der non-root User ihn binden
  darf, ohne zusätzliche Capabilities zu benötigen.
- **Exec-Form beim `ENTRYPOINT`**: das Binary läuft als PID 1 und bekommt Signale
  (z.B. `SIGTERM` bei `kubernetes stop`) direkt, ohne Shell-Zwischenschicht — dadurch
  reagiert der Container sauber auf Graceful Shutdown.
- **OCI-Labels** (`org.opencontainers.image.*`): machen im Image nachvollziehbar,
  aus welchem Repo/Commit es gebaut wurde — wichtig für Traceability in Registry
  und CI/CD-Pipeline. `REVISION` wird beim Build gesetzt, z.B.:
  ```
  docker build --build-arg REVISION=$(git rev-parse HEAD) -t dotnetrest .
  ```

## Konfiguration — Design-Entscheidungen

- **JSON-Konsolen-Logging in Produktion** (`appsettings.json`,
  `Logging:Console:FormatterName=json`): strukturierte Logs lassen sich direkt
  von Log-Aggregatoren (Loki, ELK, Cloud-Provider-Logging) parsen, ohne
  Regex-Gefummel. In `appsettings.Development.json` wird das für lokale
  Entwicklung auf das lesbarere `simple`-Format zurückgesetzt.
- **Kestrel-Limits** (`MaxRequestBodySize`, `MaxConcurrentConnections`): schützt
  vor überdimensionierten Requests bzw. Verbindungs-Exhaustion, bevor ein
  vorgelagerter Reverse Proxy/Ingress überhaupt greift.
- **`AddProblemDetails()` + `UseExceptionHandler()`** (`Program.cs`): unbehandelte
  Exceptions liefern dadurch einen RFC-7807-konformen `ProblemDetails`-Body statt
  eines nackten, inhaltslosen 500ers — wichtig für Clients, die Fehler
  maschinell auswerten.
- **Health-Checks** (`/health/live`, `/health/ready`): getrennte Endpunkte für
  Liveness (Prozess läuft) und Readiness (Prozess kann Traffic annehmen), wie es
  Kubernetes-Probes erwarten. Aktuell prüfen beide nur statisch `Healthy()` — bei
  externen Abhängigkeiten (DB, Message-Broker) müssten sie das jeweils real
  prüfen.

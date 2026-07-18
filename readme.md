# dotnet-container

Minimale Todo-REST-API auf Basis von ASP.NET Core Minimal APIs (.NET 10), gebaut als
schlankes, produktionsnahes Container-Image mit Native AOT.

## Projektstruktur

```
dotnetrest/
├── Program.cs                      # App-Setup, Middleware, Health-Check-Wiring
├── TodoEndpoints.cs                 # Endpunkt-Definitionen (MapTodoEndpoints)
├── AppJsonSerializerContext.cs      # JSON-Source-Generator-Context (AOT)
├── Todo.cs                          # Todo-Record
├── TodoService.cs                   # In-Memory-Datenhaltung (Demo, kein DB-Zugriff)
├── appsettings.json                 # Basis-Konfiguration (Prod)
├── appsettings.Development.json     # Override für lokale Entwicklung
├── dotnetrest.csproj
├── Dockerfile
k8s/
├── namespace.yaml                   # Namespace mit istio-injection=enabled
├── deployment.yaml                  # Deployment inkl. Health-Probes, GC-Tuning
├── configmap.yaml                   # Beispiel-Overlay für appsettings.Production.json
├── service.yaml                     # ClusterIP-Service (Voraussetzung für Istio)
├── peerauthentication.yaml          # Erzwingt mTLS STRICT im Namespace
├── kubernetes-deployment.md         # Schritt-für-Schritt-Anleitung (Istio-ready)
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

## Konfiguration überschreiben (`/app/config`-Overlay)

`Program.cs` lädt zusätzlich zur image-gebackenen `appsettings.json` optional
`/app/config/appsettings.Production.json`, danach erneut Environment-Variablen
(siehe Kommentar in `Program.cs`). Priorität, von niedrig nach hoch:

1. `appsettings.json` (im Image, `ASPNETCORE_ENVIRONMENT`-Datei falls vorhanden)
2. `/app/config/appsettings.Production.json` (Overlay, optional)
3. Environment-Variablen (z.B. `ASPNETCORE_...`, per ConfigMap/Secret als env oder direkt gesetzt)

### Docker run

```bash
docker build --build-arg REVISION=$(git rev-parse HEAD) -t dotnetrest ./dotnetrest

# ohne Overlay - appsettings.json aus dem Image greift
docker run --rm -p 8080:8080 dotnetrest

# mit lokalem Overlay, analog zum ConfigMap-Mount in k8s/deployment.yaml
docker run --rm -p 8080:8080 \
  -v "$(pwd)/appsettings.Production.json:/app/config/appsettings.Production.json:ro" \
  dotnetrest
```

### Kubernetes

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -n dotnetrest -f k8s/configmap.yaml   # optional - ohne ConfigMap greift appsettings.json aus dem Image
kubectl apply -n dotnetrest -f k8s/deployment.yaml
kubectl apply -n dotnetrest -f k8s/service.yaml
kubectl apply -n dotnetrest -f k8s/peerauthentication.yaml
```

`k8s/configmap.yaml` ist ein Beispiel-Overlay (Key `appsettings.Production.json`,
gemountet unter `/app/config`, siehe `k8s/deployment.yaml`). Eigene Werte dort
anpassen oder die ConfigMap weglassen, wenn die Image-Defaults ausreichen.

Der Namespace ist mit `istio-injection: enabled` gelabelt und die
`PeerAuthentication` erzwingt STRICT-mTLS zwischen den Sidecars — Details,
Voraussetzungen (Istio muss im Cluster installiert sein) und Troubleshooting
in [`k8s/kubernetes-deployment.md`](k8s/kubernetes-deployment.md).

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
  und CI/CD-Pipeline. `REVISION` wird beim Build gesetzt (siehe `docker build`
  oben unter "Docker run").

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

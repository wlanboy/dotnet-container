# Kubernetes-Deployment für dotnetrest (Istio-ready)

Schritt-für-Schritt-Anleitung, um `dotnetrest` in einem Namespace mit
Istio-Sidecar-Injection und STRICT-mTLS auszurollen.

## Dateien

```
k8s/
├── namespace.yaml           # Namespace "dotnetrest" mit istio-injection=enabled
├── configmap.yaml           # Beispiel-Overlay fuer appsettings.Production.json (optional)
├── deployment.yaml          # Deployment inkl. Health-Probes, GC-Tuning, benanntem Port
├── service.yaml             # ClusterIP-Service, Voraussetzung fuer Istio-Traffic-Management
└── peerauthentication.yaml  # Erzwingt mTLS zwischen Sidecars im Namespace
```

Kein Gateway/VirtualService in diesem Repo: `dotnetrest` ist aktuell nur
clusterintern erreichbar (Service `dotnetrest.dotnetrest.svc.cluster.local:8080`).
Externer Ingress über Istio ist ein separater Folgeschritt, sobald ein
konkreter Hostname/Ingress-Gateway feststeht.

## Voraussetzungen

- Istio ist bereits cluster-weit installiert (z. B. `istioctl install` oder
  ein Cluster-Addon). Nicht Teil dieses Repos.
  ```bash
  istioctl version
  kubectl get pods -n istio-system
  ```
- `kubectl`-Kontext zeigt auf den richtigen Cluster.
- Optional, aber hilfreich zur Kontrolle: [`istioctl`](https://istio.io/latest/docs/reference/commands/istioctl/)
  CLI installiert.
- Image liegt bereits in der Registry (siehe `tekton/tekton.md` bzw.
  `readme.md` für den Build-Weg), z. B.
  `registry.registry.svc.cluster.local:5000/dotnetrest:latest`.

## 1. Namespace mit Sidecar-Injection anlegen

```bash
kubectl apply -f k8s/namespace.yaml
```

`namespace.yaml` setzt das Label `istio-injection: enabled`. Das wirkt nur auf
Pods, die **nach** dem Anlegen des Labels erzeugt werden. Kontrolle:

```bash
kubectl get namespace dotnetrest --show-labels
```

Optional als Default für die aktuelle Shell-Session setzen:

```bash
kubectl config set-context --current --namespace=dotnetrest
```

## 2. ConfigMap, Deployment und Service ausrollen

```bash
kubectl apply -n dotnetrest -f k8s/configmap.yaml   # optional, siehe readme.md
kubectl apply -n dotnetrest -f k8s/deployment.yaml
kubectl apply -n dotnetrest -f k8s/service.yaml
```

Der `Service` ist für Istio kein optionales Extra wie sonst in Kubernetes:
Envoy/Pilot leiten Traffic-Rules über den Service (Name, Port, Selector) her,
nicht über einzelne Pods. `deployment.yaml` und `service.yaml` benennen den
Port konsistent (`http-dotnetrest`) — das `http-`-Präfix lässt Istio das
Protokoll erkennen und L7-Features (Metriken, Tracing, Retries) statt reinem
TCP-Passthrough aktivieren.

## 3. Sidecar-Injection prüfen

```bash
kubectl get pods -n dotnetrest
```

Jeder Pod sollte `2/2` Container zeigen (`dotnetrest` + `istio-proxy`). Falls
nur `1/1` erscheint, ist das Namespace-Label nicht gesetzt oder der Pod wurde
**vor** dem Label erzeugt:

```bash
kubectl get pod -n dotnetrest -o jsonpath='{.items[0].spec.containers[*].name}'
# erwartet: dotnetrest istio-proxy

kubectl rollout restart deployment/dotnetrest -n dotnetrest
```

Zusätzliche Konfigurationsprüfung (findet z. B. fehlende Labels, verwaiste
Referenzen):

```bash
istioctl analyze -n dotnetrest
```

## 4. STRICT-mTLS aktivieren

```bash
kubectl apply -n dotnetrest -f k8s/peerauthentication.yaml
```

Ohne diese Policy erlaubt Istio standardmäßig PERMISSIVE-mTLS (Klartext und
verschlüsselt gleichzeitig möglich). STRICT lehnt unverschlüsselten
Sidecar-zu-Sidecar-Traffic im Namespace `dotnetrest` ab.

Verifizieren:

```bash
istioctl x describe pod -n dotnetrest <pod-name>
```

Erwartete Ausgabe enthält u. a. `mTLS is currently STRICT`.

## 5. Funktionstest

```bash
kubectl run curl-test --rm -it --restart=Never -n dotnetrest \
  --image=curlimages/curl -- \
  curl -sf http://dotnetrest:8080/health/ready
```

Läuft `curl-test` selbst mit Sidecar (Namespace-Label greift), ist die
Anfrage automatisch mTLS-verschlüsselt.

## Troubleshooting

### Pod bleibt bei `1/1`, kein `istio-proxy`-Container

Siehe Schritt 3 — Namespace-Label fehlte beim Pod-Start, `rollout restart`
behebt es.

### Readiness/Liveness-Probe schlägt nach Injection fehl

Aktuelle Istio-Versionen schreiben `httpGet`-Probes automatisch so um, dass
sie über den Sidecar laufen (kein manueller Eingriff nötig). Bei älteren
Istio-Versionen ohne dieses Feature ggf.
`traffic.sidecar.istio.io/excludeInboundPorts` prüfen oder Istio-Version
aktualisieren.

### `istioctl analyze` meldet `PodMissingProxy` trotz gesetztem Label

Pod wurde vor dem Label erzeugt (siehe Schritt 3), oder ein
`sidecar.istio.io/inject: "false"`-Pod-Annotation überschreibt das
Namespace-Label lokal — in diesem Repo nicht gesetzt, aber bei eigenen
Anpassungen zu beachten.

## Aufräumen

Einzelne Ressourcen entfernen:

```bash
kubectl delete -n dotnetrest -f k8s/peerauthentication.yaml -f k8s/service.yaml -f k8s/deployment.yaml -f k8s/configmap.yaml
```

Kompletten Namespace inkl. aller Ressourcen entfernen:

```bash
kubectl delete namespace dotnetrest
```

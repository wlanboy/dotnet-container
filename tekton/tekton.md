# Tekton Pipeline für dotnetrest

CI/CD-Pipeline auf Basis von [Tekton Pipelines](https://tekton.dev/), die den Container-Build
für `dotnetrest` (Native AOT) im Cluster ausführt und das Image in die lokale k3s/kind-Registry
pusht.

## Dateien

```
tekton/
├── git-clone.yaml        # Tekton-Catalog-Task: klont das Repo in ein Workspace
├── task-kaniko.yml       # Task: baut das Image per Kaniko aus dem Dockerfile und pusht es
├── pipeline.yml          # Pipeline: verkettet clone -> docker-push
├── serviceaccount.yaml   # ServiceAccount, unter dem die Pipeline-Pods laufen
└── pipeline-run.yml      # PipelineRun: konkreter, parametrisierter Lauf der Pipeline
```

Kein Maven/Java-Build-Schritt: `dotnet restore`/`publish` passieren bereits im Multi-Stage-Build
des `dotnetrest/Dockerfile`. Kaniko baut direkt aus dem Dockerfile, daher genügen zwei Stufen
(`clone` → `docker-push`).

## Voraussetzungen

- Tekton Pipelines Controller ist bereits cluster-weit installiert (Namespace `tekton-pipelines`,
  Deployments `tekton-pipelines-controller`, `-webhook`, `-events-controller`,
  `-remote-resolvers`). Muss nicht pro Namespace neu installiert werden, da der Controller alle
  Namespaces beobachtet.
- Eine lokale Registry läuft im Namespace `registry` und ist unter
  `registry.registry.svc.cluster.local:5000` erreichbar. Prüfen mit:
  ```bash
  kubectl get pods -n registry
  ```
  Falls der Pod auf `Pending` hängt (`unbound immediate PersistentVolumeClaims`), fehlt das PVC
  `registry-pvc` — das Registry-Setup ist nicht Teil dieses Repos und muss dort nachgetragen
  werden, bevor der Push-Schritt der Pipeline funktioniert.
- `kubectl`-Kontext zeigt auf den richtigen Cluster (hier: `kind-local`).
- Optional, aber komfortabler für Logs: [`tkn`](https://tekton.dev/docs/cli/) CLI installiert.

## 1. Namespace anlegen

```bash
kubectl create namespace dotnetrest-ci
```

Optional als Default für die aktuelle Shell-Session setzen (erspart `-n dotnetrest-ci` bei jedem
Befehl):

```bash
kubectl config set-context --current --namespace=dotnetrest-ci
```

## 2. Pipeline-Bausteine installieren

Tasks, Pipeline und ServiceAccount sind "einmalig installieren"-Ressourcen, kein `PipelineRun`
dabei:

```bash
kubectl apply -n dotnetrest-ci -f tekton/git-clone.yaml
kubectl apply -n dotnetrest-ci -f tekton/task-kaniko.yml
kubectl apply -n dotnetrest-ci -f tekton/pipeline.yml
kubectl apply -n dotnetrest-ci -f tekton/serviceaccount.yaml
```

Kontrolle:

```bash
kubectl get task,pipeline,serviceaccount -n dotnetrest-ci
```

Für die `dotnetrest-pipeline-sa` ist keine zusätzliche RBAC nötig: Sie dient nur als
Pod-ServiceAccount für die TaskRun-Pods. Da das Repo öffentlich über HTTPS geklont wird, bleiben
die optionalen `ssh-directory`/`basic-auth`-Workspaces des `git-clone`-Tasks ungenutzt.

## 3. Build auslösen

`pipeline-run.yml` nutzt `metadata.generateName`, deshalb `create` statt `apply` verwenden (jeder
Aufruf erzeugt einen neuen, eindeutig benannten `PipelineRun`):

```bash
kubectl create -n dotnetrest-ci -f tekton/pipeline-run.yml
```

Die Parameter im `PipelineRun` (`repo-url`, `revision`, `image`, `dockerfile`) lassen sich vor dem
`create` anpassen, z. B. um einen anderen Branch oder Image-Tag zu bauen.

## 4. Fortschritt verfolgen

Mit `tkn`:

```bash
tkn pipelinerun logs -n dotnetrest-ci --last -f
```

Mit reinem `kubectl`:

```bash
kubectl get pipelinerun -n dotnetrest-ci -w
kubectl logs -n dotnetrest-ci -l tekton.dev/pipelineRun=<generierter-name> --all-containers -f
```

Bei Erfolg landet das Image unter `registry.registry.svc.cluster.local:5000/dotnetrest:latest`,
inklusive `REVISION`-Build-Arg (aus dem `commit`-Result des `clone`-Tasks) für die OCI-Labels im
Image.

## Troubleshooting

### Kaniko-Fehler: `deleting file system after stage 0: unlinkat //product_uuid: device or resource busy`

Tritt auf, nachdem der eigentliche Build (`dotnet publish`/AOT-Codegen) bereits erfolgreich
durchgelaufen ist – Kaniko scheitert erst beim Aufräumen des Stage-0-Filesystems. Ursache: die
Cluster-Plattform (z. B. vSphere-CSI/Cloud-Provider, auch bei `kind` beobachtet) mountet eine
`/product_uuid`-Datei zur VM-Identifikation in jeden Pod. Diese Datei ist ein "busy" Bind-Mount und
kann von Kaniko nicht gelöscht werden.

Fix: In `task-kaniko.yml` den Kaniko-Executor-Args `--ignore-path=/product_uuid` hinzufügen, damit
Kaniko den Pfad beim Snapshotten/Löschen ignoriert. Siehe
[GoogleContainerTools/kaniko#2164](https://github.com/GoogleContainerTools/kaniko/issues/2164).

## Aufräumen

Einen einzelnen Lauf entfernen:

```bash
kubectl delete pipelinerun -n dotnetrest-ci -l app.kubernetes.io/name=dotnetrest
```

Kompletten Namespace inkl. aller Pipeline-Ressourcen entfernen:

```bash
kubectl delete namespace dotnetrest-ci
```

#!/usr/bin/env bash
# Rollt dotnetrest aus, siehe kubernetes-deployment.md fuer die Einzelschritte.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAMESPACE="dotnetrest"

kubectl apply -f "$SCRIPT_DIR/namespace.yaml"

kubectl apply -n "$NAMESPACE" -f "$SCRIPT_DIR/configmap.yaml"
kubectl apply -n "$NAMESPACE" -f "$SCRIPT_DIR/deployment.yaml"
kubectl apply -n "$NAMESPACE" -f "$SCRIPT_DIR/service.yaml"
kubectl apply -n "$NAMESPACE" -f "$SCRIPT_DIR/peerauthentication.yaml"

kubectl rollout status -n "$NAMESPACE" deployment/dotnetrest

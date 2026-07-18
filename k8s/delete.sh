#!/usr/bin/env bash
# Entfernt dotnetrest wieder. Ohne Argument nur die einzelnen Ressourcen,
# mit --all zusaetzlich den kompletten Namespace (siehe kubernetes-deployment.md).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAMESPACE="dotnetrest"

kubectl delete -n "$NAMESPACE" --ignore-not-found \
  -f "$SCRIPT_DIR/peerauthentication.yaml" \
  -f "$SCRIPT_DIR/service.yaml" \
  -f "$SCRIPT_DIR/deployment.yaml" \
  -f "$SCRIPT_DIR/configmap.yaml"

if [[ "${1:-}" == "--all" ]]; then
  kubectl delete namespace "$NAMESPACE" --ignore-not-found
fi

#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "usage: bash deploy/deploy.sh DEPLOYMENT_PROJECT_ID KEY_PROJECT_ID REGION" >&2
  echo "  for example: bash deploy/deploy.sh my-spectrace-deploy gen-lang-client-0123456789 europe-north1" >&2
  exit 64
fi

deploy_project="$1"
key_project="$2"
region="$3"
service=spectrace
errors="$(mktemp)"
trap 'rm -f "$errors"' EXIT

step() { printf '\n== %s\n' "$1"; }
stop() { printf '\nSTOPPED: %s\n' "$1" >&2; exit 1; }
refuse() { stop "$1 Nothing was deployed."; }

cd "$(dirname "$0")/.."

[ -f Dockerfile ] && [ -f runs/reference/manifest.json ] \
  || refuse "Run this from a clone of SpecTrace-dev: Dockerfile or runs/reference/manifest.json is missing."
[ "$deploy_project" != "$key_project" ] \
  || refuse "The deployment project and the key project must be two different projects (Implementation Plan §10.2)."

manual_check="Check it by hand in the console: open Billing, then Account management, and confirm that $key_project is not \
among the projects linked to any billing account; or select $key_project and open Billing, which must say that the \
project has no billing account."

step "1. The key project $key_project must have billing disabled"
key_billing="$(gcloud billing projects describe "$key_project" --format='value(billingEnabled)' 2>"$errors")" \
  || refuse "gcloud could not read whether billing is enabled on $key_project: $(cat "$errors") $manual_check"
case "$key_billing" in
  False)
    echo "Billing is disabled on $key_project, so the Gemini free tier on it is intact." ;;
  True)
    refuse "Billing is ENABLED on the key project $key_project. Enabling billing removes the Gemini free tier on that \
project, and every call bills from the first token (Implementation Plan §10.2). Unlink its billing account in the \
console, then run this script again." ;;
  *)
    refuse "gcloud gave no clear answer about billing on $key_project: '$key_billing'. $manual_check" ;;
esac

step "2. The deployment project $deploy_project must have billing enabled"
deploy_billing="$(gcloud billing projects describe "$deploy_project" --format='value(billingEnabled)' 2>"$errors")" \
  || refuse "gcloud could not read whether billing is enabled on $deploy_project: $(cat "$errors")"
[ "$deploy_billing" = True ] \
  || refuse "Cloud Run needs billing on the deployment project, and $deploy_project reads '$deploy_billing'. Link the \
billing account and create its budget alert first (README, Deployment)."
echo "Billing is enabled on $deploy_project."

step "3. The reference review must be committed, so that the image carries it"
run_id="$(sed -n 's/^ *"run_id": *"\([^"]*\)".*$/\1/p' runs/reference/manifest.json)"
review_log="experiments/review/$run_id.reviews.jsonl"
[ -s "$review_log" ] \
  || refuse "$review_log is missing or empty. Commit the reference review first, as the README says under Review UI."
echo "$review_log holds $(wc -l < "$review_log") decisions."

step "4. Enable the APIs a deploy from source needs"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com \
  --project "$deploy_project"

step "5. Let the build service account build and deploy"
project_number="$(gcloud projects describe "$deploy_project" --format='value(projectNumber)')"
builder="${project_number}-compute@developer.gserviceaccount.com"
gcloud projects add-iam-policy-binding "$deploy_project" \
  --member="serviceAccount:$builder" \
  --role=roles/run.builder \
  --condition=None \
  --quiet > /dev/null
echo "$builder holds roles/run.builder. A new grant takes a couple of minutes to apply: if the build below fails with"
echo "a permission error, wait two minutes and run this script again."

step "6. Build the Dockerfile in Cloud Build and deploy $service to $region"
gcloud run deploy "$service" \
  --project "$deploy_project" \
  --region "$region" \
  --source . \
  --allow-unauthenticated \
  --min 0 \
  --max 1 \
  --cpu-throttling \
  --clear-env-vars \
  --clear-secrets \
  --quiet

step "7. The service configuration must hold no environment variable and no secret"
configured="$(gcloud run services describe "$service" --project "$deploy_project" --region "$region" \
  --format='value(spec.template.spec.containers[0].env,spec.template.spec.volumes)')"
[ -z "$(printf '%s' "$configured" | tr -d '[:space:]')" ] \
  || stop "The service is deployed, but its configuration holds environment variables, secrets or volumes: \
$configured. Remove them with: gcloud run services update $service --project $deploy_project --region $region \
--clear-env-vars --clear-secrets"
echo "None. Offline mode and the public demo are switched on in the image itself."

url="$(gcloud run services describe "$service" --project "$deploy_project" --region "$region" --format='value(status.url)')"
step "Deployed"
echo "Service URL: $url"
echo "Now run the smoke test, and send its output together with the URL:"
echo "  bash deploy/smoke-test.sh $url"

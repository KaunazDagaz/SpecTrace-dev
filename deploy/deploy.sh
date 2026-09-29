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
secret=gemini-api-key
offline_variable=SPECTRACE_OFFLINE
work="$(mktemp -d)"
errors="$work/errors"
trap 'rm -rf "$work"' EXIT

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

step "4. Enable the APIs a deploy from source and a key held in Secret Manager need"
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com \
  secretmanager.googleapis.com --project "$deploy_project"

step "5. The secret $secret must hold the Gemini key made in $key_project"
read_key="printf '\e[?2004l'; read -rs GEMINI_KEY; printf '\e[?2004h\n'; printf '%s' \"\$GEMINI_KEY\" | tr -d '[:space:]' |"
paste_note="Paste the AI Studio key of $key_project when the cursor waits, then press Enter. The first printf turns off the \
terminal's bracketed paste, which would otherwise wrap the key in escape codes, and nothing is echoed or kept in the \
shell history. Never create a key in $deploy_project: calls on it would be billed."
create_secret="Create it in Cloud Shell: $read_key gcloud secrets create $secret --project $deploy_project \
--replication-policy=automatic --data-file=- ; unset GEMINI_KEY. $paste_note"
add_version="Add a clean version in Cloud Shell: $read_key gcloud secrets versions add $secret --project $deploy_project \
--data-file=- ; unset GEMINI_KEY. $paste_note"
latest="$(gcloud secrets versions describe latest --secret "$secret" --project "$deploy_project" \
  --format='value(name,state)' 2>"$errors")" \
  || refuse "The secret $secret is missing from $deploy_project, or has no version: $(cat "$errors") $create_secret"
read -r version_name version_state <<< "$latest"
secret_version="${version_name##*/}"
[ "$version_state" = ENABLED ] \
  || refuse "The latest version of $secret, $secret_version, is $version_state, not ENABLED. $add_version"
stray="$(gcloud secrets versions access "$secret_version" --secret "$secret" --project "$deploy_project" 2>"$errors" \
  | LC_ALL=C tr -d '[:graph:]' | wc -c)" \
  || refuse "gcloud could not read version $secret_version of $secret to check it: $(cat "$errors")"
[ "$((stray))" -eq 0 ] \
  || refuse "Version $secret_version of $secret holds $((stray)) character(s) that are not printable, such as a space, a \
line end, or the escape codes a terminal wraps around pasted text. Google rejects a request carrying such a key with \
an HTML 'Error 400 (Bad Request)'. $add_version Then run this script again."
echo "$secret version $secret_version is enabled and holds only printable characters; the service will read the key from it."

step "6. Let the Compute Engine default service account build the image and read the key"
project_number="$(gcloud projects describe "$deploy_project" --format='value(projectNumber)')"
compute_account="${project_number}-compute@developer.gserviceaccount.com"
gcloud projects add-iam-policy-binding "$deploy_project" \
  --member="serviceAccount:$compute_account" \
  --role=roles/run.builder \
  --condition=None \
  --quiet > /dev/null
gcloud secrets add-iam-policy-binding "$secret" \
  --project "$deploy_project" \
  --member="serviceAccount:$compute_account" \
  --role=roles/secretmanager.secretAccessor \
  --condition=None \
  --quiet > /dev/null
echo "$compute_account holds roles/run.builder on $deploy_project and roles/secretmanager.secretAccessor on $secret."
echo "A new grant takes a couple of minutes to apply: if the build or the new revision below fails with a permission"
echo "error, wait two minutes and run this script again."

step "7. Build the Dockerfile in Cloud Build and deploy $service to $region, live"
gcloud run deploy "$service" \
  --project "$deploy_project" \
  --region "$region" \
  --source . \
  --allow-unauthenticated \
  --min 0 \
  --max 1 \
  --no-cpu-throttling \
  --set-env-vars "$offline_variable=0" \
  --set-secrets "GEMINI_API_KEY=$secret:$secret_version" \
  --quiet

step "8. The service configuration must hold the key as a secret reference, and nothing else"
gcloud run services describe "$service" --project "$deploy_project" --region "$region" --format=json > "$work/service.json"
python3 - "$work/service.json" "$secret" "$secret_version" "$offline_variable" <<'PYTHON' \
  || stop "The service is deployed, but its configuration is not what this script sets. Switch it back to offline with: \
gcloud run services update $service --project $deploy_project --region $region --remove-env-vars $offline_variable \
--remove-secrets GEMINI_API_KEY --cpu-throttling"
import json
import sys

path, secret, version, offline_variable = sys.argv[1:]
template = json.load(open(path))["spec"]["template"]
spec = template["spec"]
env = spec["containers"][0].get("env", [])
problems = []

names = sorted(entry["name"] for entry in env)
if names != sorted(["GEMINI_API_KEY", offline_variable]):
    problems.append(f"its environment names {names}, not exactly GEMINI_API_KEY and {offline_variable}")

for entry in env:
    if entry["name"] == "GEMINI_API_KEY":
        reference = entry.get("valueFrom", {}).get("secretKeyRef", {})
        if "value" in entry or reference.get("name") != secret or reference.get("key") != version:
            problems.append(f"GEMINI_API_KEY is not a reference to secret {secret}, version {version}")
    if entry["name"] == offline_variable and entry.get("value") != "0":
        problems.append(f"{offline_variable} is not 0")

if spec.get("volumes"):
    problems.append("it mounts volumes")

if template.get("metadata", {}).get("annotations", {}).get("run.googleapis.com/cpu-throttling") != "false":
    problems.append("its CPU is throttled outside requests, so a live run would stall after the upload request returns")

for problem in problems:
    print(f"  - {problem}")

sys.exit(1 if problems else 0)
PYTHON
echo "GEMINI_API_KEY is read from $secret version $secret_version, $offline_variable is 0, and CPU stays allocated between"
echo "requests. Nothing else is set; the public demo is switched on in the image itself."

url="$(gcloud run services describe "$service" --project "$deploy_project" --region "$region" --format='value(status.url)')"
step "Deployed"
echo "Service URL: $url"
echo "Now run the smoke test; --live makes one small live run, about two requests from the daily quota:"
echo "  bash deploy/smoke-test.sh $url --live"
echo "To switch the service back to offline at once, without a rebuild:"
echo "  gcloud run services update $service --project $deploy_project --region $region --remove-env-vars $offline_variable --remove-secrets GEMINI_API_KEY --cpu-throttling"

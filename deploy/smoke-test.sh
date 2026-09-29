#!/usr/bin/env bash
set -uo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: bash deploy/smoke-test.sh BASE_URL" >&2
  echo "  for example: bash deploy/smoke-test.sh https://spectrace-123456789012.europe-north1.run.app" >&2
  exit 64
fi

base="${1%/}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
jar="$work/cookies"
failures=0

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; failures=$((failures + 1)); }

request() {
  curl --silent --show-error --max-time 120 --cookie "$jar" --cookie-jar "$jar" \
    --output "$work/body" --dump-header "$work/headers" --write-out '%{http_code}' "$@"
}

get() { request "$base$1"; }

has() { grep -qF -- "$1" "$work/body"; }

location() { sed -n 's/^[Ll]ocation: *\([^[:space:]]*\).*$/\1/p' "$work/headers" | tail -n 1; }

field() { sed -n "s/$1/\\1/p" "$work/body" | head -n 1; }

follow() {
  local path="$1" code=""
  for _ in $(seq 1 24); do
    code="$(get "$path")"
    has 'This run is still in progress.' || break
    sleep 5
  done
  echo "$code"
}

post_corpus() {
  local code=""
  for _ in $(seq 1 12); do
    code="$(request --data-urlencode "corpus=$1" --data-urlencode "__RequestVerificationToken=$token" "$base/?handler=Corpus")"
    [ "$code" = 409 ] || break
    sleep 10
  done
  echo "$code"
}

refuse() {
  local handler="$1"
  shift
  local fields=()
  for pair in "$@"; do
    fields+=(--data-urlencode "$pair")
  done
  request "${fields[@]}" --data-urlencode "__RequestVerificationToken=$token" "$base/runs/reference/review?handler=$handler"
}

echo "SpecTrace smoke test against $base"
echo

code=000
for _ in $(seq 1 18); do
  code="$(get /health)"
  [ "$code" = 200 ] && break
  sleep 5
done
if [ "$code" = 200 ] && has Healthy; then pass "/health answers 200 Healthy"; else fail "/health answered $code"; fi

code="$(get /)"
if [ "$code" = 200 ] && has 'id="demo"' && has 'This is an offline demo.' && has 'disappear when it restarts'; then
  pass "the run list carries the offline-demo banner"
else
  fail "the run list answered $code without the offline-demo banner"
fi
if has 'href="/runs/reference"'; then pass "the run list shows the reference run"; else fail "the run list does not show the reference run"; fi
token="$(field '.*name="__RequestVerificationToken" type="hidden" value="\([^"]*\)".*')"
offered="$(grep -o '<option value="[^"]*">' "$work/body" | sed 's/<option value="\([^"]*\)">/\1/')"
if [ -n "$token" ]; then pass "the run list holds a form token"; else fail "the run list holds no form token"; fi
case "$offered" in
  *rfc6902.txt*) pass "the new-run form offers the corpus documents: $(echo $offered)" ;;
  *) fail "the new-run form does not offer rfc6902.txt; it offers: $(echo $offered)" ;;
esac

code="$(get /runs/reference)"
reference_run_id="$(field '.*<h1>Run <code>\([^<]*\)<\/code>.*')"
if [ "$code" = 200 ] && has 'Quote verification rate' && [ -n "$reference_run_id" ]; then
  pass "the reference run overview renders: run $reference_run_id, with its quote verification rate"
else
  fail "the reference run overview answered $code without its run ID or verification rate"
fi

code="$(get /runs/reference/review)"
case_id="$(grep -o '<div class="case[^"]*" id="TC-[^"]*"' "$work/body" | head -n 1 | sed 's/.*id="\([^"]*\)"/\1/')"
item_id="$(grep -o '<div class="item" id="DQ-[^"]*"' "$work/body" | head -n 1 | sed 's/.*id="\([^"]*\)"/\1/')"
lines_before="$(field '.*Lines in the review log: \([0-9]*\)\..*')"
if [ "$code" = 200 ] && has 'id="read-only"' && ! has 'name="decision"' && [ -n "$case_id" ] && [ -n "$item_id" ]; then
  pass "the reference review page renders read-only, with no decision form; its review log holds $lines_before lines"
else
  fail "the reference review page answered $code, or is not read-only, or lists no test case or queue item"
fi

code="$(get /runs/reference/matrix)"
if [ "$code" = 200 ] && has 'REQ-'; then pass "the reference matrix renders"; else fail "the reference matrix answered $code"; fi
code="$(get /runs/reference/matrix.csv)"
cp "$work/body" "$work/matrix-before.csv"
if [ "$code" = 200 ]; then pass "the reference matrix exports as CSV"; else fail "the reference matrix CSV answered $code"; fi

code="$(refuse Case "caseId=$case_id" "decision=reject" "author=smoke test")"
if [ "$code" = 403 ] && has 'The reference run is read-only on this server'; then
  pass "rejecting test case $case_id on the reference run is refused with 403"
else
  fail "rejecting test case $case_id on the reference run answered $code"
fi
code="$(refuse Item "itemId=$item_id" "decision=defer" "author=smoke test")"
if [ "$code" = 403 ] && has 'The reference run is read-only on this server'; then
  pass "deferring queue item $item_id on the reference run is refused with 403"
else
  fail "deferring queue item $item_id on the reference run answered $code"
fi

get /runs/reference/matrix.csv > /dev/null
if cmp -s "$work/body" "$work/matrix-before.csv"; then
  pass "the reference matrix is unchanged after the refused decisions"
else
  fail "the reference matrix changed after the refused decisions"
fi
get /runs/reference/review > /dev/null
lines_after="$(field '.*Lines in the review log: \([0-9]*\)\..*')"
if [ -n "$lines_after" ] && [ "$lines_after" = "$lines_before" ]; then
  pass "the reference review log still holds $lines_after lines"
else
  fail "the reference review log held $lines_before lines and now holds $lines_after"
fi

for name in $offered; do
  code="$(post_corpus "$name")"
  if [ "$code" != 302 ]; then
    fail "starting $name from the form answered $code"
    continue
  fi
  run_id="$(location)"
  run_id="${run_id#/runs/}"
  code="$(follow "/runs/$run_id")"
  if [ "$code" = 200 ] && has 'Review this run'; then
    review_code="$(get "/runs/$run_id/review")"
    matrix_code="$(get "/runs/$run_id/matrix")"
    if [ "$review_code" = 200 ] && [ "$matrix_code" = 200 ]; then
      pass "$name replays to completion as run $run_id; its review and matrix render"
    else
      fail "$name completed as run $run_id, but its review answered $review_code and its matrix $matrix_code"
    fi
  else
    fail "$name did not complete as run $run_id; its overview answered $code"
  fi
  if [ "$name" = rfc6902.txt ]; then
    if [ -n "$reference_run_id" ] && [ "$run_id" = "$reference_run_id" ]; then
      pass "rfc6902.txt replays to the reference run's ID, $run_id: the same document, model and prompts"
    else
      fail "rfc6902.txt replayed as run $run_id, not the reference run $reference_run_id"
    fi
  fi
done

printf 'SpecTrace smoke test\n\n1.  Introduction\n\n   A smoke test MUST NOT reach a model.\n' > "$work/not-in-the-cache.txt"
code="$(cd "$work" && request -F "document=@not-in-the-cache.txt;type=text/plain" -F "__RequestVerificationToken=$token" "$base/")"
if [ "$code" = 302 ]; then
  run_id="$(location)"
  run_id="${run_id#/runs/}"
  code="$(follow "/runs/$run_id")"
  if has 'This run failed.' && has 'This document is not in the cache, and the server runs offline'; then
    pass "an upload that is not in the cache fails closed as run $run_id, with the offline message"
  else
    fail "an upload that is not in the cache did not fail with the offline message; run $run_id answered $code"
  fi
  code="$(get "/runs/$run_id/review")"
  if [ "$code" = 404 ]; then pass "the failed run is not offered for review"; else fail "the failed run's review answered $code"; fi
else
  fail "uploading a document that is not in the cache answered $code"
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "All checks passed."
else
  echo "$failures check(s) failed."
  exit 1
fi

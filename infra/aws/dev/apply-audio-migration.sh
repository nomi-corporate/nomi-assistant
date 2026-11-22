#!/usr/bin/env bash
set -euo pipefail

# One-time migration for the Nomi development Assistant database.
sql="$HOME/nomi/migrations/add-audio-transcriptions.sql"
certificate="$HOME/global-bundle.pem"
expected_sha256=e46c6165958e8d7b8aa0fc727efe470f8f188cde6999def83163812fe4ebf18a

if [[ ! -f "$sql" || ! -r "$certificate" ]]; then
  echo 'Migration SQL or RDS certificate is unavailable.' >&2
  exit 1
fi
if ! printf '%s  %s\n' "$expected_sha256" "$sql" | sha256sum --check --status; then
  echo 'Migration SQL differs from the reviewed version.' >&2
  exit 1
fi

export PGSSLMODE=verify-full
export PGSSLROOTCERT="$certificate"
export PGCONNECT_TIMEOUT=10
unset PGPASSWORD

psql_auth=()
if [[ -t 0 ]]; then
  psql_auth=(-W)
else
  IFS= read -r PGPASSWORD
  if [[ -z "$PGPASSWORD" ]]; then
    echo 'Migration password was not supplied.' >&2
    exit 1
  fi
  export PGPASSWORD
  trap 'unset PGPASSWORD' EXIT
fi

psql_args=(
  -X "${psql_auth[@]}" -v ON_ERROR_STOP=1
  --host nomi-dev-assistant.cj44smkqsw7k.us-east-2.rds.amazonaws.com
  --port 5432
  --dbname nomi_assistant
  --username nomi_assistant_migrator
)

if [[ "${1:-}" == '--check' ]]; then
  psql "${psql_args[@]}" -Atqc "SELECT current_user || '/' || current_database()"
else
  psql "${psql_args[@]}" --file "$sql"
fi

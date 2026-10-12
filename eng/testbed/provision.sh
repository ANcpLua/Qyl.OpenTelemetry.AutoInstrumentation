#!/usr/bin/env bash
# Creates or repairs the Railway project qyl-testbed from .railway/railway.ts and writes the
# connection strings to ~/.config/vitals/qyl-testbed.env. Idempotent: services and secrets that
# exist are kept, so a second run only rewrites the env file.
#
#   eng/testbed/provision.sh            services, secrets, railway config apply, env file
#   eng/testbed/provision.sh secrets    services and secrets only
#   eng/testbed/provision.sh env        env file only
#
# Requires the worktree to be linked to qyl-testbed (railway link --project qyl-testbed).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="$HOME/.config/vitals/qyl-testbed.env"
# How long a client retries a sleeping service before it fails. Measured on 2026-10-12: a slept
# service answered within 10 s of the first connection, and a fresh container (after a redeploy)
# within 25 s (RabbitMQ). 120 adds room for a wake on another Railway host that pulls the image
# first. See README "Testbed on Railway".
WAKE_SECONDS_MAX=120

# service:variable pairs whose values exist only on Railway (preserve() in .railway/railway.ts).
SECRETS=(
  rabbitmq:RABBITMQ_DEFAULT_PASS
  kafka:SASL_PLAIN_PASSWORD
  mongodb:MONGO_INITDB_ROOT_PASSWORD
  redis:REDIS_PASSWORD
  postgres:POSTGRES_PASSWORD
  mysql:MYSQL_PASSWORD
  mysql:MYSQL_ROOT_PASSWORD
  oracle:ORACLE_PASSWORD
  sqlserver:MSSQL_SA_PASSWORD
)
SERVICES=(rabbitmq kafka mongodb redis postgres mysql oracle sqlserver)

cd "$ROOT"

# 32 characters, starting with a letter and holding upper, lower and digit, so SQL Server's
# complexity rule and Oracle's identifier rule accept every secret.
generate() {
  local value
  while :; do
    value="$(openssl rand -base64 32 | tr -d '/+=' | cut -c1-32)"
    [[ ${#value} -eq 32 && $value =~ ^[A-Za-z] && $value =~ [A-Z] && $value =~ [a-z] && $value =~ [0-9] ]] && break
  done
  printf '%s' "$value"
}

ensure_services() {
  local existing service
  existing="$(railway service list --json | jq -r '.[].name')"
  for service in "${SERVICES[@]}"; do
    grep -qx "$service" <<<"$existing" || railway add --service "$service" --json >/dev/null
  done
}

ensure_secrets() {
  local pair service key
  for pair in "${SECRETS[@]}"; do
    service="${pair%%:*}"
    key="${pair#*:}"
    if ! railway variable list --service "$service" --json | jq -e --arg k "$key" 'has($k)' >/dev/null; then
      generate | railway variable set "$key" --stdin --service "$service" --skip-deploys --json >/dev/null
      echo "set $service $key"
    fi
  done
}

# Prints the env lines of one service from its Railway variables (raw values) without echoing them
# anywhere else. $2 is a jq string template over that service's variables.
render() {
  railway variable list --service "$1" --json | jq -er --arg service "$1" '
    if (.RAILWAY_TCP_PROXY_DOMAIN // "") == "" or (.RAILWAY_TCP_PROXY_PORT // "") == ""
    then error("service \($service) has no TCP proxy yet; run railway config apply first")
    else . end | '"$2"
}

TMP=""
trap '[[ -z "$TMP" ]] || rm -f "$TMP"' EXIT

write_env() {
  mkdir -p "$(dirname "$ENV_FILE")"
  TMP="$(mktemp "$(dirname "$ENV_FILE")/.qyl-testbed.env.XXXXXX")"
  chmod 600 "$TMP"
  {
    render rabbitmq '"QYL_TESTBED_RABBITMQ_URL=amqp://\(.RABBITMQ_DEFAULT_USER):\(.RABBITMQ_DEFAULT_PASS)@\(.RAILWAY_TCP_PROXY_DOMAIN):\(.RAILWAY_TCP_PROXY_PORT)"'
    render kafka '"QYL_TESTBED_KAFKA_BOOTSTRAP=\(.RAILWAY_TCP_PROXY_DOMAIN):\(.RAILWAY_TCP_PROXY_PORT)\nQYL_TESTBED_KAFKA_USER=\(.SASL_PLAIN_USERNAME)\nQYL_TESTBED_KAFKA_PASSWORD=\(.SASL_PLAIN_PASSWORD)"'
    render mongodb '"QYL_TESTBED_MONGODB_URL=mongodb://\(.MONGO_INITDB_ROOT_USERNAME):\(.MONGO_INITDB_ROOT_PASSWORD)@\(.RAILWAY_TCP_PROXY_DOMAIN):\(.RAILWAY_TCP_PROXY_PORT)/?authSource=admin"'
    render redis '"QYL_TESTBED_REDIS_URL=redis://:\(.REDIS_PASSWORD)@\(.RAILWAY_TCP_PROXY_DOMAIN):\(.RAILWAY_TCP_PROXY_PORT)"'
    render postgres '"QYL_TESTBED_POSTGRES_URL=Host=\(.RAILWAY_TCP_PROXY_DOMAIN);Port=\(.RAILWAY_TCP_PROXY_PORT);Username=\(.POSTGRES_USER);Password=\(.POSTGRES_PASSWORD);Database=\(.POSTGRES_DB)"'
    render mysql '"QYL_TESTBED_MYSQL_URL=Server=\(.RAILWAY_TCP_PROXY_DOMAIN);Port=\(.RAILWAY_TCP_PROXY_PORT);User ID=\(.MYSQL_USER);Password=\(.MYSQL_PASSWORD);Database=\(.MYSQL_DATABASE)"'
    render oracle '"QYL_TESTBED_ORACLE_URL=User Id=system;Password=\(.ORACLE_PASSWORD);Data Source=\(.RAILWAY_TCP_PROXY_DOMAIN):\(.RAILWAY_TCP_PROXY_PORT)/FREEPDB1"'
    render sqlserver '"QYL_TESTBED_SQLSERVER_URL=Server=\(.RAILWAY_TCP_PROXY_DOMAIN),\(.RAILWAY_TCP_PROXY_PORT);User ID=sa;Password=\(.MSSQL_SA_PASSWORD);TrustServerCertificate=True;Encrypt=True"'
    echo "QYL_TESTBED_WAKE_SECONDS_MAX=$WAKE_SECONDS_MAX"
  } >"$TMP"
  mv -f "$TMP" "$ENV_FILE"
  TMP=""
  echo "wrote $ENV_FILE"
}

case "${1:-all}" in
  secrets) ensure_services; ensure_secrets ;;
  env) write_env ;;
  all)
    ensure_services
    ensure_secrets
    railway config apply --yes
    write_env
    ;;
  *) echo "usage: $0 [secrets|env]" >&2; exit 2 ;;
esac

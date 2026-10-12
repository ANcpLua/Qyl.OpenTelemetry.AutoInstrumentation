import { defineRailway, image, preserve, project, service } from "railway/iac";

// The Railway project qyl-testbed: the eight databases and brokers the demo verifiers connect to,
// so no developer machine pulls or runs these containers. This repository owns the whole project
// and still exports a named partial, as every qyl-dev repo does, so another repo can add its own
// slice without this file deleting it.
//
// Every service sleeps once Railway has seen no outbound traffic from it for 5 to 10 minutes and
// wakes on the next connection through its TCP proxy. Nothing is persisted: a woken service starts
// from an empty data directory, which is what a verifier expects of a fresh container.
//
// Passwords exist only on Railway (preserve()); eng/testbed/provision.sh generates the missing ones
// before an apply and writes the connection strings to ~/.config/vitals/qyl-testbed.env.
export const partial = "autoinstrumentation";

const REGION = "europe-west4-drams3a";
const MIB = 1024 * 1024;

type Env = Record<string, string | ReturnType<typeof preserve>>;

function backing(
  name: string,
  config: { image: string; port: number; cpu: number; memoryMiB: number; start?: string; env: Env },
) {
  return service(name, {
    source: image(config.image),
    deploy: {
      ...(config.start === undefined ? {} : { startCommand: config.start }),
      sleepApplication: true,
      limitOverride: { containers: { cpu: config.cpu, memoryBytes: config.memoryMiB * MIB } },
    },
    replicas: { [REGION]: 1 },
    tcp: [config.port],
    env: config.env,
  });
}

export default defineRailway(() => {
  // AMQP for the masstransit and rabbitmq lanes.
  const rabbitmq = backing("rabbitmq", {
    image: "rabbitmq:4.1-alpine",
    port: 5672,
    cpu: 1,
    memoryMiB: 512,
    env: {
      RABBITMQ_DEFAULT_USER: "qyl",
      RABBITMQ_DEFAULT_PASS: preserve(),
    },
  });

  // Single-node KRaft. The proxied listener speaks SASL/PLAIN and advertises the proxy address,
  // so a client's metadata round trip lands on the proxy again; the broker's own traffic stays on
  // loopback listeners the proxy never reaches.
  const kafka = backing("kafka", {
    image: "apache/kafka:4.1.0",
    port: 9092,
    cpu: 1,
    memoryMiB: 1024,
    env: {
      KAFKA_NODE_ID: "1",
      KAFKA_PROCESS_ROLES: "broker,controller",
      KAFKA_LISTENERS: "EXTERNAL://:9092,INTERNAL://localhost:19092,CONTROLLER://localhost:9093",
      KAFKA_ADVERTISED_LISTENERS:
        "EXTERNAL://${{RAILWAY_TCP_PROXY_DOMAIN}}:${{RAILWAY_TCP_PROXY_PORT}},INTERNAL://localhost:19092",
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: "EXTERNAL:SASL_PLAINTEXT,INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT",
      KAFKA_CONTROLLER_LISTENER_NAMES: "CONTROLLER",
      KAFKA_CONTROLLER_QUORUM_VOTERS: "1@localhost:9093",
      KAFKA_INTER_BROKER_LISTENER_NAME: "INTERNAL",
      KAFKA_SASL_ENABLED_MECHANISMS: "PLAIN",
      KAFKA_LISTENER_NAME_EXTERNAL_PLAIN_SASL_JAAS_CONFIG:
        'org.apache.kafka.common.security.plain.PlainLoginModule required user_${{SASL_PLAIN_USERNAME}}="${{SASL_PLAIN_PASSWORD}}";',
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: "1",
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: "1",
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: "1",
      KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS: "0",
      KAFKA_HEAP_OPTS: "-Xms512m -Xmx512m",
      SASL_PLAIN_USERNAME: "qyl",
      SASL_PLAIN_PASSWORD: preserve(),
    },
  });

  const mongodb = backing("mongodb", {
    image: "mongo:8-noble",
    port: 27017,
    cpu: 1,
    memoryMiB: 1024,
    env: {
      MONGO_INITDB_ROOT_USERNAME: "qyl",
      MONGO_INITDB_ROOT_PASSWORD: preserve(),
    },
  });

  // The image reads no password variable, so the start command passes it; nothing is saved.
  const redis = backing("redis", {
    image: "redis:8-alpine",
    port: 6379,
    cpu: 1,
    memoryMiB: 512,
    start: `/bin/sh -c 'exec redis-server --requirepass "$REDIS_PASSWORD" --save "" --appendonly no'`,
    env: {
      REDIS_PASSWORD: preserve(),
    },
  });

  // The npgsql lane.
  const postgres = backing("postgres", {
    image: "postgres:18-alpine",
    port: 5432,
    cpu: 1,
    memoryMiB: 512,
    env: {
      POSTGRES_USER: "qyl",
      POSTGRES_DB: "qyl",
      POSTGRES_PASSWORD: preserve(),
    },
  });

  // The mysqlconnector and mysqldata lanes.
  const mysql = backing("mysql", {
    image: "mysql:9",
    port: 3306,
    cpu: 1,
    memoryMiB: 1024,
    env: {
      MYSQL_DATABASE: "qyl",
      MYSQL_USER: "qyl",
      MYSQL_PASSWORD: preserve(),
      MYSQL_ROOT_PASSWORD: preserve(),
    },
  });

  // The oraclemda lane: user system, service FREEPDB1. The faststart image ships the data files
  // unpacked and deletes the archive in its last layer, but on Railway the archive is present, and
  // the entrypoint's unpack into the existing files stops at 7-Zip's overwrite prompt. The start
  // command removes the archive when the data files are there.
  const oracle = backing("oracle", {
    image: "gvenzl/oracle-free:23-slim-faststart",
    port: 1521,
    cpu: 2,
    memoryMiB: 3072,
    start: `/bin/bash -c '[ -d "$ORACLE_BASE/oradata/$ORACLE_SID" ] && rm -f "$ORACLE_BASE/$ORACLE_SID.7z"; exec container-entrypoint.sh'`,
    env: {
      ORACLE_PASSWORD: preserve(),
    },
  });

  // The sqlclient lane. Customer feedback is SQL Server's only outbound traffic; turned off, the
  // service can sleep. The start command replaces the image entrypoint.
  const sqlserver = backing("sqlserver", {
    image: "mcr.microsoft.com/mssql/server:2022-latest",
    port: 1433,
    cpu: 2,
    memoryMiB: 2048,
    start: `/bin/bash -c 'printf "[telemetry]\\ncustomerfeedback = false\\n" > /var/opt/mssql/mssql.conf && exec /opt/mssql/bin/sqlservr'`,
    env: {
      ACCEPT_EULA: "Y",
      MSSQL_PID: "Developer",
      MSSQL_SA_PASSWORD: preserve(),
    },
  });

  return project("qyl-testbed", {
    resources: [rabbitmq, kafka, mongodb, redis, postgres, mysql, oracle, sqlserver],
  });
});

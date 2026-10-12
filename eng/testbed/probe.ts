// Wakes the eight qyl-testbed services on Railway and proves each one answers a real query:
//
//   bun eng/testbed/probe.ts
//
// Reads ~/.config/vitals/qyl-testbed.env (written by eng/testbed/provision.sh), connects to every
// service at once and retries each until it answers or QYL_TESTBED_WAKE_SECONDS_MAX runs out. A
// sleeping service wakes on the first connection through its TCP proxy, so the time printed per
// service is wake plus connect plus query. Exits 1 when any service fails. --verbose prints every
// failed attempt.
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import amqplib from "amqplib";
import Redis from "ioredis";
import { Kafka, logLevel } from "kafkajs";
import { MongoClient } from "mongodb";
import mssql from "mssql";
import mysql from "mysql2/promise";
import oracledb from "oracledb";
import pg from "pg";

const ENV_FILE = join(homedir(), ".config", "vitals", "qyl-testbed.env");
const KEYS = [
  "QYL_TESTBED_RABBITMQ_URL",
  "QYL_TESTBED_KAFKA_BOOTSTRAP",
  "QYL_TESTBED_KAFKA_USER",
  "QYL_TESTBED_KAFKA_PASSWORD",
  "QYL_TESTBED_MONGODB_URL",
  "QYL_TESTBED_REDIS_URL",
  "QYL_TESTBED_POSTGRES_URL",
  "QYL_TESTBED_MYSQL_URL",
  "QYL_TESTBED_ORACLE_URL",
  "QYL_TESTBED_SQLSERVER_URL",
  "QYL_TESTBED_WAKE_SECONDS_MAX",
] as const;
type Key = (typeof KEYS)[number];

// One attempt may not outlast this; a wake that holds the connection open is retried.
const ATTEMPT_SECONDS = 30;
const RETRY_PAUSE_MS = 2_000;

function readEnv(): Record<Key, string> {
  const values = new Map<string, string>();
  for (const line of readFileSync(ENV_FILE, "utf8").split("\n")) {
    const at = line.indexOf("=");
    if (at > 0 && !line.startsWith("#")) values.set(line.slice(0, at), line.slice(at + 1));
  }
  const missing = KEYS.filter((key) => !values.get(key));
  if (missing.length > 0) throw new Error(`${ENV_FILE} lacks ${missing.join(", ")}`);
  return Object.fromEntries(KEYS.map((key) => [key, values.get(key)!])) as Record<Key, string>;
}

// The ADO.NET forms: Key=Value pairs separated by ';', keys compared case-insensitively.
function adoNet(connectionString: string): Map<string, string> {
  const pairs = new Map<string, string>();
  for (const part of connectionString.split(";")) {
    const at = part.indexOf("=");
    if (at > 0) pairs.set(part.slice(0, at).trim().toLowerCase(), part.slice(at + 1).trim());
  }
  return pairs;
}

function required(pairs: Map<string, string>, key: string): string {
  const value = pairs.get(key);
  if (value === undefined) throw new Error(`connection string lacks ${key}`);
  return value;
}

function withTimeout<T>(work: Promise<T>, seconds: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout>;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`attempt exceeded ${seconds}s`)), seconds * 1000);
  });
  return Promise.race([work, timeout]).finally(() => clearTimeout(timer));
}

type Check = (env: Record<Key, string>) => Promise<string>;

const checks: Record<string, Check> = {
  async rabbitmq(env) {
    const connection = await amqplib.connect(env.QYL_TESTBED_RABBITMQ_URL, { timeout: 10_000 });
    try {
      const channel = await connection.createChannel();
      const { queue } = await channel.assertQueue("", { exclusive: true, autoDelete: true });
      const body = randomUUID();
      channel.sendToQueue(queue, Buffer.from(body));
      for (let i = 0; i < 50; i++) {
        const message = await channel.get(queue, { noAck: true });
        if (message && message.content.toString() === body) {
          return `publish+get round trip, RabbitMQ ${connection.connection.serverProperties.version}`;
        }
        await Bun.sleep(100);
      }
      throw new Error("published message never arrived");
    } finally {
      await connection.close().catch(() => {});
    }
  },

  async kafka(env) {
    const bootstrap = env.QYL_TESTBED_KAFKA_BOOTSTRAP;
    const kafka = new Kafka({
      clientId: "qyl-testbed-probe",
      brokers: [bootstrap],
      sasl: { mechanism: "plain", username: env.QYL_TESTBED_KAFKA_USER, password: env.QYL_TESTBED_KAFKA_PASSWORD },
      ssl: false,
      connectionTimeout: 10_000,
      requestTimeout: 20_000,
      retry: { retries: 2 },
      logLevel: logLevel.NOTHING,
    });
    // The broker must advertise the proxy address, or every client after the bootstrap call fails.
    const admin = kafka.admin();
    await admin.connect();
    let advertised: string;
    try {
      const { brokers } = await admin.describeCluster();
      advertised = brokers.map((broker) => `${broker.host}:${broker.port}`).join(",");
      if (advertised !== bootstrap) throw new Error(`broker advertises ${advertised}, not ${bootstrap}`);
      await admin.createTopics({ topics: [{ topic: "qyl-testbed-probe", numPartitions: 1 }], waitForLeaders: true });
    } finally {
      await admin.disconnect();
    }

    const nonce = randomUUID();
    const groupId = `qyl-testbed-probe-${nonce}`;
    const consumer = kafka.consumer({ groupId });
    const producer = kafka.producer();
    try {
      await consumer.connect();
      await consumer.subscribe({ topic: "qyl-testbed-probe", fromBeginning: true });
      const received = new Promise<void>((resolve) => {
        void consumer.run({
          eachMessage: async ({ message }) => {
            if (message.value?.toString() === nonce) resolve();
          },
        });
      });
      await producer.connect();
      await producer.send({ topic: "qyl-testbed-probe", messages: [{ value: nonce }] });
      await withTimeout(received, 20);
      return `SASL/PLAIN produce+consume round trip, advertised ${advertised}`;
    } finally {
      await producer.disconnect().catch(() => {});
      await consumer.disconnect().catch(() => {});
      await admin
        .connect()
        .then(() => admin.deleteGroups([groupId]))
        .finally(() => admin.disconnect())
        .catch(() => {});
    }
  },

  async mongodb(env) {
    const client = new MongoClient(env.QYL_TESTBED_MONGODB_URL, {
      serverSelectionTimeoutMS: 10_000,
      connectTimeoutMS: 10_000,
    });
    try {
      await client.connect();
      const collection = client.db("qyl").collection("probe");
      const nonce = randomUUID();
      await collection.insertOne({ nonce });
      const found = await collection.findOne({ nonce });
      if (!found) throw new Error("inserted document not found");
      await collection.deleteOne({ nonce });
      const info = await client.db("admin").command({ buildInfo: 1 });
      return `insert+find round trip, MongoDB ${info.version}`;
    } finally {
      await client.close().catch(() => {});
    }
  },

  async redis(env) {
    const redis = new Redis(env.QYL_TESTBED_REDIS_URL, {
      lazyConnect: true,
      connectTimeout: 10_000,
      maxRetriesPerRequest: 0,
      retryStrategy: () => null,
    });
    redis.on("error", () => {});
    try {
      await redis.connect();
      const nonce = randomUUID();
      await redis.set("qyl-testbed-probe", nonce, "EX", 60);
      if ((await redis.get("qyl-testbed-probe")) !== nonce) throw new Error("GET did not return the SET value");
      const server = (await redis.info("server")).match(/redis_version:(\S+)/)?.[1];
      return `SET+GET round trip, Redis ${server}`;
    } finally {
      redis.disconnect();
    }
  },

  async postgres(env) {
    const pairs = adoNet(env.QYL_TESTBED_POSTGRES_URL);
    const client = new pg.Client({
      host: required(pairs, "host"),
      port: Number(required(pairs, "port")),
      user: required(pairs, "username"),
      password: required(pairs, "password"),
      database: required(pairs, "database"),
      connectionTimeoutMillis: 10_000,
    });
    client.on("error", () => {});
    try {
      await client.connect();
      const { rows } = await client.query("SELECT current_database() AS db, current_setting('server_version') AS version");
      return `SELECT in ${rows[0].db}, PostgreSQL ${rows[0].version}`;
    } finally {
      await client.end().catch(() => {});
    }
  },

  async mysql(env) {
    const pairs = adoNet(env.QYL_TESTBED_MYSQL_URL);
    const connection = await mysql.createConnection({
      host: required(pairs, "server"),
      port: Number(required(pairs, "port")),
      user: required(pairs, "user id"),
      password: required(pairs, "password"),
      database: required(pairs, "database"),
      connectTimeout: 10_000,
    });
    try {
      const [rows] = await connection.query<mysql.RowDataPacket[]>("SELECT DATABASE() AS db, VERSION() AS version");
      return `SELECT in ${rows[0].db}, MySQL ${rows[0].version}`;
    } finally {
      await connection.end().catch(() => {});
    }
  },

  async oracle(env) {
    const pairs = adoNet(env.QYL_TESTBED_ORACLE_URL);
    const connection = await oracledb.getConnection({
      user: required(pairs, "user id"),
      password: required(pairs, "password"),
      connectString: required(pairs, "data source"),
      connectTimeout: 10,
    });
    try {
      const result = await connection.execute<[string, string]>(
        "SELECT sys_context('USERENV', 'CON_NAME'), (SELECT version_full FROM product_component_version WHERE ROWNUM = 1) FROM dual",
      );
      const [container, version] = result.rows![0];
      if (container !== "FREEPDB1") throw new Error(`connected to ${container}, not FREEPDB1`);
      return `SELECT in ${container}, Oracle ${version}`;
    } finally {
      await connection.close().catch(() => {});
    }
  },

  async sqlserver(env) {
    const pairs = adoNet(env.QYL_TESTBED_SQLSERVER_URL);
    const [server, port] = required(pairs, "server").split(",");
    const pool = new mssql.ConnectionPool({
      server: server!,
      port: Number(port),
      user: required(pairs, "user id"),
      password: required(pairs, "password"),
      options: {
        encrypt: required(pairs, "encrypt").toLowerCase() === "true",
        trustServerCertificate: required(pairs, "trustservercertificate").toLowerCase() === "true",
      },
      connectionTimeout: 15_000,
      requestTimeout: 15_000,
      pool: { max: 1, min: 0 },
    });
    pool.on("error", () => {});
    try {
      await pool.connect();
      const result = await pool.request().query<{ version: string }>("SELECT @@VERSION AS version");
      return `SELECT @@VERSION, ${result.recordset[0]!.version.split("\n")[0]!.trim()}`;
    } finally {
      await pool.close().catch(() => {});
    }
  },
};

type Outcome = { name: string; ok: boolean; seconds: number; attempts: number; detail: string };

async function probe(name: string, check: Check, env: Record<Key, string>, deadline: number): Promise<Outcome> {
  const started = performance.now();
  let attempts = 0;
  let lastError = "";
  while (true) {
    attempts++;
    try {
      const detail = await withTimeout(check(env), ATTEMPT_SECONDS);
      return { name, ok: true, seconds: (performance.now() - started) / 1000, attempts, detail };
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
      if (verbose) {
        const at = ((performance.now() - started) / 1000).toFixed(1);
        console.error(`     ${name} attempt ${attempts} failed at ${at}s: ${redact(lastError, env)}`);
      }
    }
    if (performance.now() + RETRY_PAUSE_MS >= deadline) {
      return { name, ok: false, seconds: (performance.now() - started) / 1000, attempts, detail: lastError };
    }
    await Bun.sleep(RETRY_PAUSE_MS);
  }
}

// Driver errors can quote what they were given; no secret from the env file reaches the terminal.
function redact(text: string, env: Record<Key, string>): string {
  const secrets = new Set<string>([env.QYL_TESTBED_KAFKA_PASSWORD]);
  for (const key of KEYS) {
    for (const match of env[key].matchAll(/(?:password=|:)([A-Za-z0-9]{32})(?=[@;]|$)/gi)) secrets.add(match[1]!);
  }
  let redacted = text;
  for (const secret of secrets) redacted = redacted.replaceAll(secret, "***");
  return redacted.replaceAll(/\s+/g, " ").slice(0, 300);
}

const verbose = process.argv.includes("--verbose");
const env = readEnv();
const wakeSecondsMax = Number(env.QYL_TESTBED_WAKE_SECONDS_MAX);
if (!Number.isFinite(wakeSecondsMax) || wakeSecondsMax <= 0) {
  throw new Error("QYL_TESTBED_WAKE_SECONDS_MAX is not a positive number");
}
const deadline = performance.now() + wakeSecondsMax * 1000;
const color = process.stdout.isTTY;
const paint = (ok: boolean, text: string) => (color ? `\x1b[${ok ? 32 : 31}m${text}\x1b[0m` : text);

const outcomes = await Promise.all(
  Object.entries(checks).map(async ([name, check]) => {
    const outcome = await probe(name, check, env, deadline);
    console.log(
      paint(
        outcome.ok,
        `${outcome.ok ? "ok  " : "FAIL"} ${name.padEnd(10)} ${outcome.seconds.toFixed(1).padStart(6)}s  ` +
          `${String(outcome.attempts).padStart(3)} attempt${outcome.attempts === 1 ? " " : "s"}  ${redact(outcome.detail, env)}`,
      ),
    );
    return outcome;
  }),
);

const failed = outcomes.filter((outcome) => !outcome.ok).map((outcome) => outcome.name);
console.log(
  failed.length === 0
    ? `all ${outcomes.length} testbed services answered; slowest ${Math.max(...outcomes.map((o) => o.seconds)).toFixed(1)}s of ${wakeSecondsMax}s allowed`
    : `failed: ${failed.join(", ")}`,
);
process.exit(failed.length === 0 ? 0 : 1);

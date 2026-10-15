import { existsSync, readFileSync } from "fs"
import Logger from "./utils/logger"
import { z } from "zod"

// config must be at project root (or override via CONFIG_PATH env var for tests)
const CONFIG_PATH = process.env.CONFIG_PATH || "./config.json"

export const ConfigSchema = z.object({
    auth: z.object({
        api_keys: z.array(z.string().min(1)).default([])
    }),
    general: z.object({
        mode: z.enum(["development", "production"]).default("development"),
        public_api_port: z.number().int().min(1).max(65535).default(3000),
        private_api_port: z.number().int().min(1).max(65535).default(3001),
        api_prefix: z.string().default("/api"),
        grpc_socket_path: z.string().default("/tmp/osu-tools-calc.sock"),
        reqs_per_min: z.number().int().min(1).default(60)
    }),
    cache: z.object({
        path: z.string().default("./cache"),
        ttl_days: z.number().min(0).default(1)
    }),
    webhook: z
        .object({
            url: z.union([z.url(), z.literal("")]).default(""),
            token: z.string().default("")
        })
        .prefault({}),
    loki: z
        .object({
            url: z.union([z.url(), z.literal("")]).default(""),
            labels: z.record(z.string(), z.string()).default({})
        })
        .prefault({})
})
type Config = z.infer<typeof ConfigSchema>

let loadedConfig: Config | null = null

// Config is read once at startup, so synchronous fs is acceptable here
function loadConfig(): Config {
    if (!existsSync(CONFIG_PATH)) {
        console.error(`Config file not found at ${CONFIG_PATH}. Copy config-example.json to config.json and edit it.`)
        process.exit(1)
    }

    const raw = JSON.parse(readFileSync(CONFIG_PATH, { encoding: "utf-8" }))
    const result = ConfigSchema.safeParse(raw)

    if (!result.success) {
        console.error(`Invalid config: ${result.error.message}`)
        process.exit(1)
    }

    return result.data
}

export function getConfig(): Config {
    if (!loadedConfig) {
        loadedConfig = loadConfig()
    }
    return loadedConfig
}

/**
 * @param module which part of the app we are logging (stored as loki label)
 * @param submodule which subpart of the app we are logging (NOT stored as loki label)
 */
export function createLogger(module: string, submodule?: string): Logger {
    const config = getConfig()
    return new Logger(
        {
            name: "osu-tools-api",
            mode: config.general.mode,
            webhookUrl: config.webhook.url || undefined,
            webhookToken: config.webhook.token || undefined,
            lokiUrl: config.loki.url || undefined,
            lokiLabels: config.loki.labels
        },
        module,
        submodule
    )
}

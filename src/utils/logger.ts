import winston from "winston"
import LokiTransport from "winston-loki"
import Transport from "winston-transport"
import { request } from "undici"
import { Request } from "express"

/**
 * @param req The Express request object
 * @returns The complete path of the request no matter the route, without query parameters
 */
export function formatPath(req: Pick<Request, "baseUrl" | "path">): string {
    return (req.baseUrl + req.path).replace(/\/$/, "")
}

export interface ILogData {
    message: string
    time?: number
    error?: any
    fatal_error?: any
    /**
     * @description make it so we don't try to log the error/fatal_error to the webhook!
     * set this to true if the error is coming from the webhook so we don't try to post to webhook infinitely
     */
    no_webhook?: boolean
    ip?: string

    // api values
    api_method?: string
    api_path?: string
    api_status_code?: number
    api_middleware?: string
}

export interface ILoggerConfig {
    name: string
    mode: string
    webhookUrl?: string
    webhookToken?: string
    lokiUrl?: string
}

const LOG_LEVELS = {
    fatal: 0,
    api_abuse: 1,
    rate_limit: 2,
    error: 3,
    warn: 4,
    info: 5,
    debug: 6
} as const

const consoleLog = winston.format.printf((info: any) => {
    let { module, level, timestamp, message } = info

    if (info.error) {
        message += ": " + info.error.stack
    } else if (info.fatal_error) {
        message += ": " + info.fatal_error.stack
    }

    let ip = info.ip ? ` - ${info.ip}` : ""

    if (info.api_path) {
        let { api_path } = info
        if (info.api_method && info.api_status_code) {
            message += ` (${info.api_method} ${info.api_status_code})`
        }
        if (info.time) message += ` | ${info.time.toFixed(2)}ms`

        if (info.api_middleware) api_path += `:${info.api_middleware}`

        return `${timestamp} [${level}] ${module}${api_path}${ip} | ${message}`
    } else {
        if (info.api_middleware) module += `:${info.api_middleware}`
        if (info.submoduleFirst) {
            module += `:${info.submoduleFirst}`
        }
        if (info.time) message += ` | ${info.time.toFixed(2)}ms`
        return `${timestamp} [${level}] ${module}${ip}: ${message}`
    }
})

const lokiMessage = winston.format.printf((info: any) => {
    let { message } = info
    if (info.error) {
        message += ": " + info.error.toString()
    } else if (info.fatal_error) {
        message += ": " + info.fatal_error.toString()
    }

    if (info.api_path && info.api_method && info.api_status_code) {
        message += ` (${info.api_method} ${info.api_status_code})`
    }

    if (info.time) {
        message += ` | ${info.time.toFixed(2)}ms`
    }

    let prefix = info.module ?? ""
    if (info.api_path) prefix += info.api_path
    if (info.api_middleware) prefix += `:${info.api_middleware}`
    if (info.submoduleFirst) prefix += `:${info.submoduleFirst}`
    if (info.ip) prefix += ` - ${info.ip}`

    return prefix + ": " + message
})

async function sendWebhookNotification(webhookUrl: string, level: string, message: string, token?: string) {
    try {
        const headers: Record<string, string> = { "Content-Type": "application/json" }
        if (token) headers["Authorization"] = token

        const response = await request(webhookUrl, {
            method: "POST",
            headers,
            body: JSON.stringify({ message: `osu-tools-api: ${level} - ${message}` })
        })
        if (response.statusCode >= 400) {
            console.error(`Couldn't send webhook notification: HTTP ${response.statusCode}`)
        }
    } catch (err) {
        console.error("Couldn't send webhook notification", err)
    }
}

export default class Logger {
    private config: ILoggerConfig
    private winstonLogger: winston.Logger

    constructor(config: ILoggerConfig, module: string, submodule?: string) {
        this.config = config

        let transports: Transport[] = [new winston.transports.Console({ format: consoleLog })]
        if (config.lokiUrl) {
            transports.push(
                new LokiTransport({
                    host: config.lokiUrl,
                    interval: 5,
                    timeout: 300000,
                    format: lokiMessage,
                    useWinstonMetaAsLabels: true,
                    ignoredMeta: ["error", "fatal_error", "no_webhook", "time", "api_path", "api_status_code", "api_method", "api_middleware", "submodule", "submoduleFirst", "ip"],
                    onConnectionError: (err: any) => this.fatal({ message: "Cannot connect to Loki", fatal_error: err, no_webhook: true }),
                    level: "debug"
                })
            )
        }

        this.winstonLogger = winston.createLogger({
            defaultMeta: { module, submoduleFirst: submodule, service: config.name, mode: config.mode },
            levels: LOG_LEVELS,
            transports,
            format: winston.format.combine(winston.format.timestamp()),
            exitOnError: false,
            level: "debug"
        })
    }

    public fatal(content: ILogData) {
        this.winstonLog("fatal", { ...content })
        if (!content.no_webhook && this.config.webhookUrl) {
            const message = content.fatal_error ? `${content.message}: ${content.fatal_error.toString()}` : content.message
            sendWebhookNotification(this.config.webhookUrl, "fatal", message, this.config.webhookToken)
        }
    }

    public apiAbuse(content: ILogData) {
        this.winstonLog("api_abuse", { ...content })
    }

    public rateLimit(content: ILogData) {
        this.winstonLog("rate_limit", { ...content })
    }

    public error(content: ILogData) {
        this.winstonLog("error", { ...content })
        if (!content.no_webhook && this.config.webhookUrl) {
            const message = content.error ? `${content.message}: ${content.error.toString()}` : content.message
            sendWebhookNotification(this.config.webhookUrl, "error", message, this.config.webhookToken)
        }
    }

    public warn(content: ILogData) {
        this.winstonLog("warn", { ...content })
    }

    public info(content: ILogData) {
        this.winstonLog("info", { ...content })
    }

    public debug(content: ILogData) {
        this.winstonLog("debug", { ...content })
    }

    private winstonLog(level: string, content: ILogData) {
        this.winstonLogger.log(level, { ...content })
    }
}

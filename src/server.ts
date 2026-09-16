import express, { Request, Response, NextFunction } from "express"
import cors from "cors"
import responseTime from "response-time"
import rateLimit from "express-rate-limit"
import { getConfig, createLogger } from "./config"
import { httpRequestDuration, httpRequestsTotal } from "./services/prometheus"
import { apiKeyAuth } from "./middleware/auth"
import { formatPath } from "./utils/logger"
import difficultyRouter from "./routes/difficulty"
import performanceRouter from "./routes/performance"
import metricsRouter from "./routes/metrics"

const logger = createLogger("server")

export function createPublicServer() {
    const config = getConfig()
    const app = express()

    app.set("trust proxy", 1)
    app.disable("x-powered-by")

    app.use(
        cors({
            origin: "*",
            methods: ["GET", "POST", "OPTIONS"],
            exposedHeaders: ["Content-Type", "Authorization"],
            credentials: false
        })
    )

    app.use(express.json({ limit: "128kb" }))
    app.use(express.urlencoded({ extended: true, limit: "128kb" }))

    // Response time tracking for Prometheus
    app.use(
        responseTime((req: Request, res: Response, time: number) => {
            const route = req.route ? formatPath(req) : "unmatched" // if we don't have a route then don't have prometheus log the request to avoid cardinality issues
            const method = req.method
            const statusCode = res.statusCode

            httpRequestDuration.labels(method, route, String(statusCode)).observe(time / 1000)
            httpRequestsTotal.labels(method, route, String(statusCode)).inc()
        })
    )

    // Rate limiting
    const apiLimiter = rateLimit({
        windowMs: 1 * 60 * 1000, // 1 min
        max: config.general.reqs_per_min,
        legacyHeaders: false,
        handler: (req, res) => {
            logger.rateLimit({
                message: "Rate limit hit",
                ip: req.ip,
                api_method: req.method,
                api_path: formatPath(req)
            })
            return res.status(429).json({ error: "Too many requests" })
        }
    })

    // API routes
    app.use(`${config.general.api_prefix}/difficulty`, apiLimiter, apiKeyAuth, difficultyRouter)
    app.use(`${config.general.api_prefix}/performance`, apiLimiter, apiKeyAuth, performanceRouter)

    // 404 handler
    app.use((_: Request, res: Response) => {
        return res.status(404).json({ error: "Not found" })
    })

    // Error handler
    app.use((err: any, req: Request, res: Response, next: NextFunction) => {
        // Malformed JSON body from express.json()
        if (err instanceof SyntaxError && (err as any).type === "entity.parse.failed") {
            return res.status(400).json({ error: "Invalid JSON body" })
        }

        if (err instanceof SyntaxError && (err as any).type === "entity.too.large") {
            return res.status(413).json({ error: "Request body too large" })
        }

        logger.error({
            message: `Unhandled error: ${err.message}`,
            error: err,
            ip: req.ip,
            api_method: req.method,
            api_path: formatPath(req),
            api_status_code: 500
        })
        return res.status(500).json({ error: "Internal server error" })
    })

    return app
}

export function createPrivateServer() {
    const app = express()

    app.disable("x-powered-by")

    // Metrics endpoint
    app.use("/metrics", metricsRouter)

    return app
}

export function startServer() {
    const config = getConfig()

    const publicApi = createPublicServer()
    publicApi
        .listen(config.general.public_api_port, () => {
            logger.info({ message: `Public API listening on port ${config.general.public_api_port}` })
        })
        .on("error", err => {
            logger.fatal({ message: `Public API can't listen on port ${config.general.public_api_port}`, fatal_error: err })
            process.exit(1)
        })

    const privateApi = createPrivateServer()
    privateApi
        .listen(config.general.private_api_port, () => {
            logger.info({ message: `Private API listening on port ${config.general.private_api_port}` })
        })
        .on("error", err => {
            logger.fatal({ message: `Private API can't listen on port ${config.general.private_api_port}`, fatal_error: err })
            process.exit(1)
        })
}

import { Request, Response, NextFunction } from "express"
import { getConfig, createLogger } from "../config"
import { formatPath } from "../utils/logger"

const logger = createLogger("public_api")

export function apiKeyAuth(req: Request, res: Response, next: NextFunction) {
    const config = getConfig()

    // If no API keys are configured, auth is disabled
    if (config.auth.api_keys.length === 0) {
        return next()
    }

    const authHeader = req.headers.authorization
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        logger.apiAbuse({
            message: "Missing or malformed Authorization header",
            ip: req.ip,
            api_method: req.method,
            api_path: formatPath(req),
            api_status_code: 401,
            api_middleware: "auth"
        })
        return res.status(401).json({ error: "Missing or invalid Authorization header. Use: Bearer <key>" })
    }

    // remove "Bearer " to check against our keys
    const key = authHeader.slice(7)

    if (!config.auth.api_keys.includes(key)) {
        logger.apiAbuse({
            message: "Invalid API key",
            ip: req.ip,
            api_method: req.method,
            api_path: formatPath(req),
            api_status_code: 401,
            api_middleware: "auth"
        })
        return res.status(401).json({ error: "Invalid API key" })
    }

    next()
}

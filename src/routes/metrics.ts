import { Router, Request, Response } from "express"
import promClient from "prom-client"
import { createLogger } from "../config"

const logger = createLogger("private_api")
const router = Router()

/**
 * @description private api endpoint to serve prometheus metrics
 */
router.get("/", async (req: Request, res: Response) => {
    try {
        res.set("Content-Type", promClient.register.contentType)
        return res.send(await promClient.register.metrics())
    } catch (err) {
        logger.error({ message: "Failed to collect metrics", error: err })
        return res.status(500).json({ error: "Failed to collect metrics" })
    }
})

export default router

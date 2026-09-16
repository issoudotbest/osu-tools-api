import { Router, Request, Response } from "express"
import { z } from "zod"
import { getBeatmapData } from "../services/beatmap_manager"
import { calculateDifficulty } from "../services/grpc_client"
import { createLogger } from "../config"
import { formatPath } from "../utils/logger"
import { formatZodError, grpcErrorToStatusCode } from "../utils/validation"

const logger = createLogger("public_api")
const router = Router()

export const DifficultyRequestSchema = z.object({
    beatmapId: z.number().int().min(1),
    rulesetId: z.number().int().min(-1).max(3).optional().default(-1),
    mods: z
        .array(
            z.object({
                acronym: z.string().min(1),
                settings: z.record(z.string(), z.string()).optional()
            })
        )
        .optional()
        .default([])
})

router.post("/", async (req: Request, res: Response) => {
    let data: z.infer<typeof DifficultyRequestSchema>
    try {
        data = DifficultyRequestSchema.parse(req.body)
    } catch (err) {
        logger.apiAbuse({
            message: `Difficulty request validation failed: ${(err as z.ZodError).message}`,
            ip: req.ip,
            api_method: req.method,
            api_path: formatPath(req),
            api_status_code: 400
        })
        return res.status(400).json({ error: "Invalid request body", details: formatZodError(err as z.ZodError) })
    }

    // Get beatmap data
    const beatmapResult = await getBeatmapData(data.beatmapId)
    if (!beatmapResult.ok) {
        return res.status(502).json({ error: beatmapResult.error })
    }

    // Call gRPC
    const result = await calculateDifficulty({
        beatmapData: beatmapResult.data,
        rulesetId: data.rulesetId,
        mods: data.mods
    })

    if (!result.ok) {
        return res.status(grpcErrorToStatusCode(result.error)).json({ error: result.error })
    }

    const diff = result.data
    logger.info({
        message: `Difficulty calculated for beatmap ${data.beatmapId} (${data.mods.length} mods)`,
        ip: req.ip,
        api_method: req.method,
        api_path: formatPath(req),
        api_status_code: 200
    })
    return res.status(200).json({
        starRating: diff.starRating,
        approachRate: diff.approachRate,
        overallDifficulty: diff.overallDifficulty,
        circleSize: diff.circleSize,
        drainRate: diff.drainRate,
        maxCombo: diff.maxCombo,
        bpm: diff.bpm,
        length: diff.length,
        drainLength: diff.drainLength,
        attributes: diff.attributes
    })
})

export default router

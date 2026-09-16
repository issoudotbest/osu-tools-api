import { Router, Request, Response } from "express"
import { z } from "zod"
import { getBeatmapData } from "../services/beatmap_manager"
import { calculatePerformance } from "../services/grpc_client"
import { createLogger } from "../config"
import { formatPath } from "../utils/logger"
import { formatZodError, grpcErrorToStatusCode } from "../utils/validation"

const logger = createLogger("public_api")
const router = Router()

export const PerformanceRequestSchema = z.object({
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
        .default([]),
    accuracy: z.number().min(0).max(100).optional().default(100),
    misses: z.number().int().min(0).optional().default(0),
    mehs: z.number().int().min(0).optional(),
    goods: z.number().int().min(0).optional(),
    oks: z.number().int().min(0).optional(),
    greats: z.number().int().min(0).optional(),
    combo: z.number().int().min(0).optional(),
    percentCombo: z.number().min(0).max(100).optional().default(100),
    largeTickMisses: z.number().int().min(0).optional().default(0),
    sliderTailMisses: z.number().int().min(0).optional().default(0),
    tinyDroplets: z.number().int().min(0).optional(),
    droplets: z.number().int().min(0).optional(),
    legacyTotalScore: z.number().int().min(0).optional(),
    precalculatedDifficulty: z.string().optional()
})

router.post("/", async (req: Request, res: Response) => {
    let data: z.infer<typeof PerformanceRequestSchema>
    try {
        data = PerformanceRequestSchema.parse(req.body)
    } catch (err) {
        logger.apiAbuse({
            message: `Performance request validation failed: ${(err as z.ZodError).message}`,
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
    const result = await calculatePerformance({
        beatmapData: beatmapResult.data,
        rulesetId: data.rulesetId,
        mods: data.mods,
        accuracy: data.accuracy,
        misses: data.misses,
        mehs: data.mehs,
        goods: data.goods,
        oks: data.oks,
        greats: data.greats,
        combo: data.combo,
        percentCombo: data.percentCombo,
        largeTickMisses: data.largeTickMisses,
        sliderTailMisses: data.sliderTailMisses,
        tinyDroplets: data.tinyDroplets,
        droplets: data.droplets,
        legacyTotalScore: data.legacyTotalScore,
        precalculatedDifficulty: data.precalculatedDifficulty
    })

    if (!result.ok) {
        return res.status(grpcErrorToStatusCode(result.error)).json({ error: result.error })
    }

    const perf = result.data
    logger.info({
        message: `Performance calculated for beatmap ${data.beatmapId} (${data.mods.length} mods)`,
        ip: req.ip,
        api_method: req.method,
        api_path: formatPath(req),
        api_status_code: 200
    })
    return res.status(200).json({
        performance: perf.performance,
        difficulty: {
            starRating: perf.difficulty.starRating,
            approachRate: perf.difficulty.approachRate,
            overallDifficulty: perf.difficulty.overallDifficulty,
            circleSize: perf.difficulty.circleSize,
            drainRate: perf.difficulty.drainRate,
            maxCombo: perf.difficulty.maxCombo,
            bpm: perf.difficulty.bpm,
            length: perf.difficulty.length,
            drainLength: perf.difficulty.drainLength,
            attributes: perf.difficulty.attributes
        },
        score: perf.score,
        performanceAttributes: perf.performanceAttributes
    })
})

export default router

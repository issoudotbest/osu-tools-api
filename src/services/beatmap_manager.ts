import path from "path"
import fs from "fs/promises"
import NodeCache from "node-cache"
import { request } from "undici"
import { getConfig, createLogger } from "../config"
import { beatmapCacheHits, beatmapCacheMisses } from "./prometheus"
import { ServiceResult } from "./types"
import pkg from "../../package.json"

const logger = createLogger("service", "beatmap_manager")

// osu! first, mino as fallback
const BEATMAP_SOURCES = ["https://osu.ppy.sh/osu", "https://catboy.best/osu"]
const USER_AGENT = `osu-tools-api/${pkg.version} (+https://github.com/issoudotbest/osu-tools-api)`

const memoryCache = new NodeCache({
    stdTTL: 3600, // cache results for 1 hour
    checkperiod: 60,
    maxKeys: 1000
})

// Deduplicates concurrent download requests for the same beatmap ID
const inflightRequests = new Map<number, Promise<ServiceResult<Buffer>>>()

export async function getBeatmapData(beatmapId: number): Promise<ServiceResult<Buffer>> {
    const config = getConfig()
    const cacheKey = `beatmap_${beatmapId}`
    const useDiskCache = config.cache.ttl_days > 0

    // Check in-memory cache (always active, 1h TTL)
    const cached = memoryCache.get<Buffer>(cacheKey)
    if (cached) {
        beatmapCacheHits.inc()
        logger.debug({ message: `Beatmap ${beatmapId} found in memory cache` })
        return { ok: true, data: cached }
    }

    // Check disk cache
    const cacheDir = config.cache.path
    const filePath = path.join(cacheDir, `${beatmapId}.osu`)

    if (useDiskCache) {
        try {
            const stats = await fs.stat(filePath)
            const ttlMs = config.cache.ttl_days * 24 * 60 * 60 * 1000
            const isExpired = Date.now() - stats.mtimeMs > ttlMs

            if (!isExpired) {
                beatmapCacheHits.inc()
                const data = await fs.readFile(filePath)
                memoryCache.set(cacheKey, data)
                logger.debug({ message: `Beatmap ${beatmapId} found in disk cache` })
                return { ok: true, data }
            }
        } catch {
            // File doesn't exist, proceed to download
        }
    }

    // Download from beatmap sources (osu!, then Mino as fallback)
    // Deduplicate concurrent requests for the same beatmap ID
    const inflight = inflightRequests.get(beatmapId)
    if (inflight) return inflight

    const downloadPromise = downloadBeatmap(beatmapId, config, cacheKey, filePath, useDiskCache)
    inflightRequests.set(beatmapId, downloadPromise)
    try {
        return await downloadPromise
    } finally {
        inflightRequests.delete(beatmapId)
    }
}

async function downloadBeatmap(beatmapId: number, config: ReturnType<typeof getConfig>, cacheKey: string, filePath: string, useDiskCache: boolean): Promise<ServiceResult<Buffer>> {
    beatmapCacheMisses.inc()

    let data: Buffer | null = null
    let lastError = ""

    for (const source of BEATMAP_SOURCES) {
        const sourceName = new URL(source).host
        logger.info({ message: `Downloading beatmap ${beatmapId} from ${sourceName}` })

        try {
            const response = await request(`${source}/${beatmapId}`, {
                headers: { "User-Agent": USER_AGENT },
                headersTimeout: 10_000,
                bodyTimeout: 30_000
            })

            if (response.statusCode !== 200) {
                lastError = `HTTP ${response.statusCode}`
                logger.warn({ message: `Beatmap ${beatmapId}: ${sourceName} returned HTTP ${response.statusCode}` })
                continue
            }

            const body = Buffer.from(await response.body.arrayBuffer())

            if (!body.toString("latin1").startsWith("osu file format v")) {
                lastError = "Beatmap not found or invalid"
                logger.warn({ message: `Beatmap ${beatmapId}: ${sourceName} returned invalid content (${body.length} bytes)` })
                continue
            }

            data = body
            break
        } catch (err) {
            lastError = (err as Error).message
            logger.warn({ message: `Beatmap ${beatmapId}: ${sourceName} request failed: ${lastError}` })
        }
    }

    if (!data) {
        logger.error({ message: `Failed to download beatmap ${beatmapId} from all sources` })
        return { ok: false, error: `Failed to download beatmap ${beatmapId}: ${lastError}` }
    }

    if (useDiskCache) {
        try {
            await fs.mkdir(config.cache.path, { recursive: true })
            await fs.writeFile(filePath, data)
        } catch (err) {
            logger.error({ message: `Failed to write beatmap ${beatmapId} to disk cache: ${(err as Error).message}` })
        }
    }
    memoryCache.set(cacheKey, data)

    logger.info({ message: `Downloaded beatmap ${beatmapId} (${data.length} bytes)` })
    return { ok: true, data }
}

/**
 * @description used in tests
 */
export function clearMemoryCache(beatmapId?: number) {
    if (beatmapId) {
        memoryCache.del(`beatmap_${beatmapId}`)
    } else {
        memoryCache.flushAll()
    }
}

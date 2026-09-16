import path from "path"
import * as grpc from "@grpc/grpc-js"
import * as protoLoader from "@grpc/proto-loader"
import { getConfig, createLogger } from "../config"
import { grpcCallDuration } from "./prometheus"
import { ServiceResult } from "./types"
import type { ProtoGrpcType } from "../generated/calculator"
import type { CalculatorClient } from "../generated/osutoolsapi/Calculator"
import type { DifficultyResponse__Output } from "../generated/osutoolsapi/DifficultyResponse"
import type { PerformanceResponse__Output } from "../generated/osutoolsapi/PerformanceResponse"

const logger = createLogger("service", "grpc_client")

/** A mod with its acronym and optional string-typed settings (parsed by osu). */
export interface Mod {
    acronym: string
    settings?: Record<string, string>
}

/** Parsed difficulty attributes extracted from a gRPC DifficultyResponse. */
export interface DifficultyResult {
    starRating: number
    approachRate: number
    overallDifficulty: number
    circleSize: number
    drainRate: number
    maxCombo: number
    bpm: number
    length: number
    drainLength: number
    attributes: Record<string, unknown>
    error?: string
}

/** Parsed score statistics extracted from a gRPC PerformanceResponse. */
export interface ScoreStatistics {
    rulesetId: number
    beatmapId: number
    beatmapName: string
    accuracy: number
    combo: number
    statistics: Record<string, number>
}

/** Parsed performance point breakdown extracted from a gRPC PerformanceResponse. */
export interface PerformanceAttributes {
    total: number
    aim: number
    speed: number
    accuracyPp: number
    flashlight: number
    effectiveMissCount: number
    extra: Record<string, unknown>
}

/** Parsed performance calculation result extracted from a gRPC PerformanceResponse. */
export interface PerformanceResult {
    performance: number
    difficulty: DifficultyResult
    score: ScoreStatistics
    performanceAttributes: PerformanceAttributes
    error?: string
}

const PROTO_PATH = path.join(process.cwd(), "proto", "calculator.proto")

const GRPC_DEADLINE_MS = 30000

let client: CalculatorClient | null = null

function getClient(): CalculatorClient {
    if (client) return client

    const config = getConfig()
    const packageDefinition = protoLoader.loadSync(PROTO_PATH, {
        keepCase: false,
        longs: Number,
        enums: String,
        defaults: true,
        oneofs: true
    })
    const protoDescriptor = grpc.loadPackageDefinition(packageDefinition) as unknown as ProtoGrpcType
    const calculatorProto = protoDescriptor.osutoolsapi

    const socketPath = `unix://${config.general.grpc_socket_path}`
    client = new calculatorProto.Calculator(socketPath, grpc.credentials.createInsecure())

    logger.info({ message: `gRPC client connecting to ${socketPath}` })

    return client
}

function parseJsonSafe(json: string | undefined): Record<string, unknown> {
    if (!json) return {}
    try {
        return JSON.parse(json)
    } catch {
        return {}
    }
}

export function parseDifficultyResponse(resp: DifficultyResponse__Output | undefined): DifficultyResult {
    if (!resp) {
        return {
            starRating: 0,
            approachRate: 0,
            overallDifficulty: 0,
            circleSize: 0,
            drainRate: 0,
            maxCombo: 0,
            bpm: 0,
            length: 0,
            drainLength: 0,
            attributes: {},
            error: undefined
        }
    }
    return {
        starRating: resp.starRating ?? 0,
        approachRate: resp.approachRate ?? 0,
        overallDifficulty: resp.overallDifficulty ?? 0,
        circleSize: resp.circleSize ?? 0,
        drainRate: resp.drainRate ?? 0,
        maxCombo: Number(resp.maxCombo ?? 0),
        bpm: resp.bpm ?? 0,
        length: resp.length ?? 0,
        drainLength: resp.drainLength ?? 0,
        attributes: parseJsonSafe(resp.attributesJson),
        error: resp.error || undefined
    }
}

export function parsePerformanceResponse(resp: PerformanceResponse__Output): PerformanceResult {
    const difficulty = parseDifficultyResponse(resp.difficulty)
    let extra: Record<string, unknown> = {}
    if (resp.performanceAttributes?.extraJson) {
        extra = parseJsonSafe(resp.performanceAttributes.extraJson)
    }
    return {
        performance: resp.performance ?? 0,
        difficulty,
        score: {
            rulesetId: resp.score?.rulesetId ?? 0,
            beatmapId: resp.score?.beatmapId ?? 0,
            beatmapName: resp.score?.beatmapName ?? "",
            accuracy: resp.score?.accuracy ?? 0,
            combo: resp.score?.combo ?? 0,
            statistics: resp.score?.statistics ?? {}
        },
        performanceAttributes: {
            total: resp.performanceAttributes?.total ?? 0,
            aim: resp.performanceAttributes?.aim ?? 0,
            speed: resp.performanceAttributes?.speed ?? 0,
            accuracyPp: resp.performanceAttributes?.accuracyPp ?? 0,
            flashlight: resp.performanceAttributes?.flashlight ?? 0,
            effectiveMissCount: resp.performanceAttributes?.effectiveMissCount ?? 0,
            extra
        },
        error: resp.error || undefined
    }
}

function callGrpc<T, R extends { error?: string }>(method: string, fn: (client: CalculatorClient, options: grpc.CallOptions, callback: (err: grpc.ServiceError | null, response: R | undefined) => void) => void, parser: (resp: R) => T): Promise<ServiceResult<T>> {
    return new Promise(resolve => {
        const startTime = process.hrtime.bigint()
        const deadline = Date.now() + GRPC_DEADLINE_MS

        fn(getClient(), { deadline }, (err, response) => {
            const durationNs = Number(process.hrtime.bigint() - startTime)
            const durationSec = durationNs / 1e9
            const status = err ? "error" : "ok"

            grpcCallDuration.labels(method, status).observe(durationSec)

            if (err) {
                logger.error({
                    message: `gRPC call ${method} failed: ${err.message}`,
                    error: err,
                    time: durationSec * 1000
                })
                resolve({ ok: false, error: err.message })
                return
            }

            if (!response) {
                resolve({ ok: false, error: "Empty response from gRPC service" })
                return
            }

            if (response.error) {
                logger.warn({
                    message: `gRPC call ${method} returned error: ${response.error}`,
                    time: durationSec * 1000
                })
                resolve({ ok: false, error: response.error })
                return
            }

            logger.debug({
                message: `gRPC call ${method} completed`,
                time: durationSec * 1000
            })
            resolve({ ok: true, data: parser(response) })
        })
    })
}

/**
 * @description Sends a difficulty calculation request to the C# gRPC service.
 * @param req Beatmap data, ruleset, and mods to calculate with.
 * @returns The difficulty attributes, or an error if the call fails.
 */
export function calculateDifficulty(req: { beatmapData: Buffer; rulesetId: number; mods: Mod[] }): Promise<ServiceResult<DifficultyResult>> {
    return callGrpc<DifficultyResult, DifficultyResponse__Output>(
        "CalculateDifficulty",
        (client, options, cb) =>
            client.CalculateDifficulty(
                {
                    beatmapData: req.beatmapData,
                    rulesetId: req.rulesetId,
                    mods: req.mods
                },
                options,
                cb
            ),
        parseDifficultyResponse
    )
}

/**
 * @description Sends a performance (PP) calculation request to the C# gRPC service.
 * @param req Beatmap data, ruleset, mods, and score parameters (accuracy, misses, combo, etc.).
 * @returns The performance result with difficulty, score statistics, and PP breakdown, or an error if the call fails.
 */
export function calculatePerformance(req: { beatmapData: Buffer; rulesetId: number; mods: Mod[]; accuracy: number; misses: number; mehs?: number; goods?: number; oks?: number; greats?: number; combo?: number; percentCombo: number; largeTickMisses: number; sliderTailMisses: number; tinyDroplets?: number; droplets?: number; legacyTotalScore?: number; precalculatedDifficulty?: string }): Promise<ServiceResult<PerformanceResult>> {
    return callGrpc<PerformanceResult, PerformanceResponse__Output>(
        "CalculatePerformance",
        (client, options, cb) =>
            client.CalculatePerformance(
                {
                    beatmapData: req.beatmapData,
                    rulesetId: req.rulesetId,
                    mods: req.mods,
                    accuracy: req.accuracy,
                    misses: req.misses,
                    mehs: req.mehs ?? 0,
                    goods: req.goods ?? 0,
                    oks: req.oks ?? 0,
                    greats: req.greats ?? 0,
                    combo: req.combo ?? 0,
                    percentCombo: req.percentCombo,
                    largeTickMisses: req.largeTickMisses,
                    sliderTailMisses: req.sliderTailMisses,
                    tinyDroplets: req.tinyDroplets ?? 0,
                    droplets: req.droplets ?? 0,
                    legacyTotalScore: req.legacyTotalScore ?? 0,
                    precalculatedDifficulty: req.precalculatedDifficulty ?? ""
                },
                options,
                cb
            ),
        parsePerformanceResponse
    )
}

/** Checks whether the gRPC client is connected and ready to accept requests. */
export function isGrpcReady(): boolean {
    if (!client) return false
    try {
        const channel = (client as unknown as grpc.Client).getChannel() as any
        const state = channel.getState()
        return state === grpc.connectivityState.READY || state === grpc.connectivityState.IDLE
    } catch {
        // getChannel may not be available on all client types
        return false
    }
}

/**
 * Polls the gRPC socket until the C# service is ready or the timeout expires.
 * @param timeoutMs Maximum time to wait in milliseconds (default 30s).
 * @returns `true` if the service became ready, `false` on timeout.
 */
export function waitForGrpc(timeoutMs = 30000): Promise<boolean> {
    return new Promise(resolve => {
        const config = getConfig()
        const socketPath = `unix://${config.general.grpc_socket_path}`
        const deadline = Date.now() + timeoutMs
        let cancelled = false

        const check = () => {
            if (cancelled) return
            if (Date.now() > deadline) {
                cancelled = true
                resolve(false)
                return
            }

            const testClient = new grpc.Client(socketPath, grpc.credentials.createInsecure())
            testClient.waitForReady(Date.now() + 2000, err => {
                testClient.close()
                if (cancelled) return
                if (!err) {
                    cancelled = true
                    getClient()
                    resolve(true)
                } else {
                    setTimeout(check, 500)
                }
            })
        }

        check()
    })
}

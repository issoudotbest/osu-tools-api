import { describe, test, expect, jest, beforeEach } from "@jest/globals"
import request from "supertest"

jest.mock("../../src/services/grpc_client", () => ({
    calculateDifficulty: jest.fn(),
    calculatePerformance: jest.fn(),
    waitForGrpc: jest.fn(() => Promise.resolve(true)),
    isGrpcReady: jest.fn(() => true)
}))

jest.mock("../../src/services/beatmap_manager", () => ({
    getBeatmapData: jest.fn(),
    clearMemoryCache: jest.fn()
}))

import { createPublicServer } from "../../src/server"
import { calculatePerformance } from "../../src/services/grpc_client"
import { getBeatmapData } from "../../src/services/beatmap_manager"

const mockCalculatePerformance = calculatePerformance as jest.MockedFunction<typeof calculatePerformance>
const mockGetBeatmapData = getBeatmapData as jest.MockedFunction<typeof getBeatmapData>

const VALID_KEY = "test-api-key"
const AUTH_HEADER = { Authorization: `Bearer ${VALID_KEY}` }

describe("POST /api/performance", () => {
    beforeEach(() => {
        jest.clearAllMocks()
        mockGetBeatmapData.mockResolvedValue({ ok: true, data: Buffer.from("test osu data") })
    })

    test("returns performance attributes on success", async () => {
        mockCalculatePerformance.mockResolvedValue({
            ok: true,
            data: {
                performance: 456.78,
                difficulty: {
                    starRating: 5.25,
                    approachRate: 9,
                    overallDifficulty: 8,
                    circleSize: 4,
                    drainRate: 5,
                    maxCombo: 500,
                    bpm: 180,
                    length: 120,
                    drainLength: 100,
                    attributes: { star_rating: 5.25 }
                },
                score: {
                    rulesetId: 0,
                    beatmapId: 75,
                    beatmapName: "Test - Test Beatmap [Normal]",
                    accuracy: 99.5,
                    combo: 500,
                    statistics: { great: 490, ok: 5, meh: 3, miss: 2 }
                },
                performanceAttributes: {
                    total: 456.78,
                    aim: 200,
                    speed: 150,
                    accuracyPp: 50,
                    flashlight: 0,
                    effectiveMissCount: 2,
                    extra: {}
                }
            }
        })

        const app = createPublicServer()
        const res = await request(app)
            .post("/api/performance")
            .set(AUTH_HEADER)
            .send({ beatmapId: 75, accuracy: 99, mods: [{ acronym: "HD" }, { acronym: "DT" }] })

        expect(res.status).toBe(200)
        expect(res.body.performance).toBe(456.78)
        expect(res.body.difficulty.starRating).toBe(5.25)
        expect(res.body.score.statistics.great).toBe(490)
        expect(res.body.performanceAttributes.total).toBe(456.78)
    })

    test("returns 401 without auth header", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/performance").send({ beatmapId: 75 })

        expect(res.status).toBe(401)
    })

    test("returns 400 for missing beatmapId", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/performance").set(AUTH_HEADER).send({})

        expect(res.status).toBe(400)
    })

    test("returns 400 for accuracy out of range", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/performance").set(AUTH_HEADER).send({ beatmapId: 75, accuracy: 150 })

        expect(res.status).toBe(400)
    })

    test("returns 502 when beatmap download fails", async () => {
        mockGetBeatmapData.mockResolvedValue({ ok: false, error: "Download failed" })

        const app = createPublicServer()
        const res = await request(app).post("/api/performance").set(AUTH_HEADER).send({ beatmapId: 999 })

        expect(res.status).toBe(502)
    })

    test("returns 502 when gRPC calculation fails", async () => {
        mockCalculatePerformance.mockResolvedValue({ ok: false, error: "Calculation error" })

        const app = createPublicServer()
        const res = await request(app).post("/api/performance").set(AUTH_HEADER).send({ beatmapId: 75 })

        expect(res.status).toBe(502)
    })

    test("returns 503 when gRPC service is unavailable", async () => {
        mockCalculatePerformance.mockResolvedValue({ ok: false, error: "UNAVAILABLE: channel is in TRANSIENT_FAILURE" })

        const app = createPublicServer()
        const res = await request(app).post("/api/performance").set(AUTH_HEADER).send({ beatmapId: 75 })

        expect(res.status).toBe(503)
    })

    test("passes all performance parameters to gRPC client", async () => {
        mockCalculatePerformance.mockResolvedValue({
            ok: true,
            data: {
                performance: 300,
                difficulty: { starRating: 5, approachRate: 9, overallDifficulty: 8, circleSize: 4, drainRate: 5, maxCombo: 500, bpm: 180, length: 120, drainLength: 100, attributes: {} },
                score: { rulesetId: 0, beatmapId: 75, beatmapName: "test", accuracy: 95, combo: 450, statistics: {} },
                performanceAttributes: { total: 300, aim: 100, speed: 100, accuracyPp: 50, flashlight: 0, effectiveMissCount: 1, extra: {} }
            }
        })

        const app = createPublicServer()
        await request(app)
            .post("/api/performance")
            .set(AUTH_HEADER)
            .send({
                beatmapId: 75,
                rulesetId: 0,
                mods: [{ acronym: "HR" }],
                accuracy: 98,
                misses: 5,
                combo: 450,
                percentCombo: 90
            })

        expect(mockCalculatePerformance).toHaveBeenCalledWith(
            expect.objectContaining({
                accuracy: 98,
                misses: 5,
                combo: 450,
                percentCombo: 90
            })
        )
    })
})

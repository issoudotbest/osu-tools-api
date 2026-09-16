import { describe, test, expect, jest, beforeEach } from "@jest/globals"
import request from "supertest"

// Mock the gRPC client before importing the server
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
import { calculateDifficulty } from "../../src/services/grpc_client"
import { getBeatmapData } from "../../src/services/beatmap_manager"

const mockCalculateDifficulty = calculateDifficulty as jest.MockedFunction<typeof calculateDifficulty>
const mockGetBeatmapData = getBeatmapData as jest.MockedFunction<typeof getBeatmapData>

const VALID_KEY = "test-api-key"
const AUTH_HEADER = { Authorization: `Bearer ${VALID_KEY}` }

describe("POST /api/difficulty", () => {
    beforeEach(() => {
        jest.clearAllMocks()
        mockGetBeatmapData.mockResolvedValue({ ok: true, data: Buffer.from("test osu data") })
    })

    test("returns difficulty attributes on success", async () => {
        mockCalculateDifficulty.mockResolvedValue({
            ok: true,
            data: {
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
            }
        })

        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({ beatmapId: 75 })

        expect(res.status).toBe(200)
        expect(res.body).toEqual({
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
        })
    })

    test("returns 401 without auth header", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").send({ beatmapId: 75 })

        expect(res.status).toBe(401)
    })

    test("returns 401 with invalid API key", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set({ Authorization: "Bearer wrong-key" }).send({ beatmapId: 75 })

        expect(res.status).toBe(401)
    })

    test("returns 400 for missing beatmapId", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({})

        expect(res.status).toBe(400)
        expect(res.body.error).toBeDefined()
    })

    test("returns 400 for non-number beatmapId", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({ beatmapId: "not-a-number" })

        expect(res.status).toBe(400)
    })

    test("returns 502 when beatmap download fails", async () => {
        mockGetBeatmapData.mockResolvedValue({ ok: false, error: "Download failed" })

        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({ beatmapId: 999 })

        expect(res.status).toBe(502)
        expect(res.body.error).toBe("Download failed")
    })

    test("returns 502 when gRPC calculation fails", async () => {
        mockCalculateDifficulty.mockResolvedValue({ ok: false, error: "Calculation error" })

        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({ beatmapId: 75 })

        expect(res.status).toBe(502)
        expect(res.body.error).toBe("Calculation error")
    })

    test("returns 503 when gRPC service is unavailable", async () => {
        mockCalculateDifficulty.mockResolvedValue({ ok: false, error: "UNAVAILABLE: channel is in TRANSIENT_FAILURE" })

        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set(AUTH_HEADER).send({ beatmapId: 75 })

        expect(res.status).toBe(503)
    })

    test("passes mods and ruleset to gRPC client", async () => {
        mockCalculateDifficulty.mockResolvedValue({
            ok: true,
            data: {
                starRating: 6,
                approachRate: 10,
                overallDifficulty: 9,
                circleSize: 4,
                drainRate: 5,
                maxCombo: 500,
                bpm: 270,
                length: 80,
                drainLength: 60,
                attributes: {}
            }
        })

        const app = createPublicServer()
        await request(app)
            .post("/api/difficulty")
            .set(AUTH_HEADER)
            .send({ beatmapId: 75, rulesetId: 0, mods: [{ acronym: "DT", settings: { speed_change: "1.5" } }, { acronym: "HD" }] })

        expect(mockCalculateDifficulty).toHaveBeenCalledWith({
            beatmapData: Buffer.from("test osu data"),
            rulesetId: 0,
            mods: [{ acronym: "DT", settings: { speed_change: "1.5" } }, { acronym: "HD" }]
        })
    })
})

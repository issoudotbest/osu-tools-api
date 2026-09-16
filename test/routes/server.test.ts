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

describe("Public server edge cases", () => {
    beforeEach(() => {
        jest.clearAllMocks()
    })

    test("returns 404 for unknown routes", async () => {
        const app = createPublicServer()
        const res = await request(app).get("/api/nonexistent").set({ Authorization: "Bearer test-api-key" })

        expect(res.status).toBe(404)
        expect(res.body.error).toBe("Not found")
    })

    test("returns 404 for unknown non-api routes", async () => {
        const app = createPublicServer()
        const res = await request(app).get("/health")

        expect(res.status).toBe(404)
    })

    test("returns 400 for malformed JSON body", async () => {
        const app = createPublicServer()
        const res = await request(app).post("/api/difficulty").set({ Authorization: "Bearer test-api-key", "Content-Type": "application/json" }).send("{ invalid json }")

        expect(res.status).toBe(400)
        expect(res.body.error).toContain("Invalid JSON")
    })
})

describe("Rate limiting", () => {
    beforeEach(() => {
        jest.clearAllMocks()
    })

    test("returns 429 when rate limit is exceeded", async () => {
        const app = createPublicServer()
        const authHeader = { Authorization: "Bearer test-api-key" }

        // Test config has reqs_per_min=60, so the 61st request should be rate limited.
        // Send empty bodies so Zod validation returns 400 quickly without hitting gRPC.
        for (let i = 0; i < 60; i++) {
            await request(app).post("/api/difficulty").set(authHeader).send({})
        }

        const res = await request(app).post("/api/difficulty").set(authHeader).send({})

        expect(res.status).toBe(429)
        expect(res.body.error).toContain("Too many requests")
    })
})

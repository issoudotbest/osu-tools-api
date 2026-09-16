import { describe, test, expect, jest, beforeEach } from "@jest/globals"
import request from "supertest"
import promClient from "prom-client"

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

import { createPrivateServer } from "../../src/server"

describe("GET /metrics", () => {
    beforeEach(() => {
        jest.clearAllMocks()
    })

    test("returns Prometheus-format metrics", async () => {
        const app = createPrivateServer()
        const res = await request(app).get("/metrics")

        expect(res.status).toBe(200)
        expect(res.text).toContain("http_request_duration_seconds")
        expect(res.text).toContain("http_requests_total")
        expect(res.text).toContain("grpc_call_duration_seconds")
        expect(res.text).toContain("beatmap_cache_hits_total")
        expect(res.text).toContain("beatmap_cache_misses_total")
    })

    test("returns valid Prometheus text format", async () => {
        const app = createPrivateServer()
        const res = await request(app).get("/metrics")

        // Prometheus text format starts with HELP and TYPE lines
        expect(res.text).toMatch(/^#\s+(HELP|TYPE)\s+/m)
    })
})

import { describe, test, expect, jest, beforeEach } from "@jest/globals"
import express from "express"
import request from "supertest"
import { apiKeyAuth } from "../../src/middleware/auth"

jest.mock("../../src/config", () => ({
    getConfig: jest.fn(() => ({
        auth: { api_keys: ["test-api-key"] },
        general: { mode: "development" }
    })),
    createLogger: () => ({
        fatal: jest.fn(),
        error: jest.fn(),
        warn: jest.fn(),
        info: jest.fn(),
        debug: jest.fn(),
        apiAbuse: jest.fn(),
        rateLimit: jest.fn()
    })
}))

import { getConfig } from "../../src/config"
const mockGetConfig = getConfig as jest.MockedFunction<typeof getConfig>

function createTestApp() {
    const app = express()
    app.use(express.json())
    app.use("/protected", apiKeyAuth, (req, res) => res.status(200).json({ ok: true }))
    return app
}

describe("API key auth middleware", () => {
    test("allows access with valid API key", async () => {
        const app = createTestApp()
        const res = await request(app).get("/protected").set({ Authorization: "Bearer test-api-key" })

        expect(res.status).toBe(200)
        expect(res.body.ok).toBe(true)
    })

    test("returns 401 when Authorization header is missing", async () => {
        const app = createTestApp()
        const res = await request(app).get("/protected")

        expect(res.status).toBe(401)
        expect(res.body.error).toContain("Authorization")
    })

    test("returns 401 when Authorization header is malformed", async () => {
        const app = createTestApp()
        const res = await request(app).get("/protected").set({ Authorization: "Basic test-api-key" })

        expect(res.status).toBe(401)
    })

    test("returns 401 when API key is invalid", async () => {
        const app = createTestApp()
        const res = await request(app).get("/protected").set({ Authorization: "Bearer wrong-key" })

        expect(res.status).toBe(401)
        expect(res.body.error).toContain("Invalid")
    })

    test("skips auth when api_keys is empty", async () => {
        mockGetConfig.mockReturnValueOnce({
            auth: { api_keys: [] },
            general: { mode: "development" }
        } as any)

        const app = createTestApp()
        const res = await request(app).get("/protected")

        expect(res.status).toBe(200)
        expect(res.body.ok).toBe(true)
    })
})

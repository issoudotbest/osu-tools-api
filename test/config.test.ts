import { describe, test, expect } from "@jest/globals"
import { ConfigSchema } from "../src/config"
import { PerformanceRequestSchema } from "../src/routes/performance"
import { DifficultyRequestSchema } from "../src/routes/difficulty"

describe("Config schema validation", () => {
    test("applies defaults for missing optional fields", () => {
        const result = ConfigSchema.safeParse({
            auth: { api_keys: ["key"] },
            general: { mode: "development" },
            cache: {},
            urls: {}
        })

        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.general.public_api_port).toBe(3000)
            expect(result.data.general.private_api_port).toBe(3001)
            expect(result.data.general.api_prefix).toBe("/api")
            expect(result.data.general.grpc_socket_path).toBe("/tmp/osu-tools-calc.sock")
            expect(result.data.general.reqs_per_min).toBe(60)
            expect(result.data.cache.path).toBe("./cache")
            expect(result.data.cache.ttl_days).toBe(1)
            expect(result.data.urls.webhook).toBe("")
            expect(result.data.urls.webhook_token).toBe("")
            expect(result.data.urls.loki).toBe("")
        }
    })

    test("accepts custom reqs_per_min", () => {
        const result = ConfigSchema.safeParse({
            auth: { api_keys: ["key"] },
            general: { mode: "development", reqs_per_min: 120 },
            cache: {},
            urls: {}
        })

        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.general.reqs_per_min).toBe(120)
        }
    })

    test("rejects invalid configs", () => {
        const invalid = [
            { auth: { api_keys: ["key"] }, general: { mode: "invalid_mode" }, cache: {}, urls: {} },
            { auth: { api_keys: ["key"] }, general: { mode: "development", public_api_port: 99999 }, cache: {}, urls: {} }
        ]

        for (const config of invalid) {
            expect(ConfigSchema.safeParse(config).success).toBe(false)
        }
    })

    test("accepts empty api_keys (auth disabled)", () => {
        const result = ConfigSchema.safeParse({
            auth: { api_keys: [] },
            general: { mode: "development" },
            cache: {},
            urls: {}
        })

        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.auth.api_keys).toEqual([])
        }
    })
})

describe("DifficultyRequest schema validation", () => {
    test("accepts valid request and applies defaults", () => {
        const result = DifficultyRequestSchema.safeParse({ beatmapId: 75 })
        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.beatmapId).toBe(75)
            expect(result.data.rulesetId).toBe(-1)
            expect(result.data.mods).toEqual([])
        }
    })

    test("accepts mods with settings", () => {
        const result = DifficultyRequestSchema.safeParse({
            beatmapId: 75,
            rulesetId: 0,
            mods: [{ acronym: "DT", settings: { speed_change: "1.5" } }, { acronym: "HD" }]
        })
        expect(result.success).toBe(true)
    })

    test("rejects invalid input", () => {
        expect(DifficultyRequestSchema.safeParse({}).success).toBe(false)
        expect(DifficultyRequestSchema.safeParse({ beatmapId: -1 }).success).toBe(false)
        expect(DifficultyRequestSchema.safeParse({ beatmapId: 75, rulesetId: 5 }).success).toBe(false)
    })
})

describe("PerformanceRequest schema validation", () => {
    test("accepts valid request and applies defaults", () => {
        const result = PerformanceRequestSchema.safeParse({ beatmapId: 75 })
        expect(result.success).toBe(true)
        if (result.success) {
            expect(result.data.accuracy).toBe(100)
            expect(result.data.misses).toBe(0)
            expect(result.data.percentCombo).toBe(100)
        }
    })

    test("accepts all fields", () => {
        const result = PerformanceRequestSchema.safeParse({
            beatmapId: 75,
            rulesetId: 0,
            mods: [{ acronym: "HR" }],
            accuracy: 98.5,
            misses: 5,
            mehs: 10,
            goods: 20,
            combo: 450,
            percentCombo: 90
        })
        expect(result.success).toBe(true)
    })

    test("rejects out-of-range values", () => {
        expect(PerformanceRequestSchema.safeParse({ beatmapId: 75, accuracy: 150 }).success).toBe(false)
        expect(PerformanceRequestSchema.safeParse({ beatmapId: 75, misses: -1 }).success).toBe(false)
        expect(PerformanceRequestSchema.safeParse({ beatmapId: 75, percentCombo: 150 }).success).toBe(false)
    })
})

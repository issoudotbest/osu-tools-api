import { describe, test, expect } from "@jest/globals"
import { parseDifficultyResponse, parsePerformanceResponse, isGrpcReady } from "../../src/services/grpc_client"
import type { DifficultyResponse__Output } from "../../src/generated/osutoolsapi/DifficultyResponse"
import type { PerformanceResponse__Output } from "../../src/generated/osutoolsapi/PerformanceResponse"

describe("parseDifficultyResponse", () => {
    test("parses a full response with all fields", () => {
        const resp: DifficultyResponse__Output = {
            starRating: 5.25,
            approachRate: 9,
            overallDifficulty: 8,
            circleSize: 4,
            drainRate: 5,
            maxCombo: 500 as any,
            bpm: 180,
            length: 120,
            drainLength: 100,
            attributesJson: '{"star_rating":5.25,"max_combo":500}',
            error: ""
        }

        const result = parseDifficultyResponse(resp)

        expect(result.starRating).toBe(5.25)
        expect(result.approachRate).toBe(9)
        expect(result.overallDifficulty).toBe(8)
        expect(result.circleSize).toBe(4)
        expect(result.drainRate).toBe(5)
        expect(result.maxCombo).toBe(500)
        expect(result.bpm).toBe(180)
        expect(result.length).toBe(120)
        expect(result.drainLength).toBe(100)
        expect(result.attributes).toEqual({ star_rating: 5.25, max_combo: 500 })
        expect(result.error).toBeUndefined()
    })

    test("applies defaults for missing fields", () => {
        const resp: DifficultyResponse__Output = {}

        const result = parseDifficultyResponse(resp)

        expect(result.starRating).toBe(0)
        expect(result.approachRate).toBe(0)
        expect(result.overallDifficulty).toBe(0)
        expect(result.circleSize).toBe(0)
        expect(result.drainRate).toBe(0)
        expect(result.maxCombo).toBe(0)
        expect(result.bpm).toBe(0)
        expect(result.length).toBe(0)
        expect(result.drainLength).toBe(0)
        expect(result.attributes).toEqual({})
        expect(result.error).toBeUndefined()
    })

    test("parses attributesJson as empty object when empty string", () => {
        const resp: DifficultyResponse__Output = { attributesJson: "" }

        const result = parseDifficultyResponse(resp)

        expect(result.attributes).toEqual({})
    })

    test("preserves error field when set", () => {
        const resp: DifficultyResponse__Output = { error: "Something went wrong" }

        const result = parseDifficultyResponse(resp)

        expect(result.error).toBe("Something went wrong")
    })

    test("returns all-zero defaults for undefined input", () => {
        const result = parseDifficultyResponse(undefined)

        expect(result.starRating).toBe(0)
        expect(result.approachRate).toBe(0)
        expect(result.overallDifficulty).toBe(0)
        expect(result.circleSize).toBe(0)
        expect(result.drainRate).toBe(0)
        expect(result.maxCombo).toBe(0)
        expect(result.bpm).toBe(0)
        expect(result.length).toBe(0)
        expect(result.drainLength).toBe(0)
        expect(result.attributes).toEqual({})
        expect(result.error).toBeUndefined()
    })

    test("handles malformed attributesJson gracefully", () => {
        const resp: DifficultyResponse__Output = { attributesJson: "not valid json" }

        const result = parseDifficultyResponse(resp)

        expect(result.attributes).toEqual({})
    })
})

describe("parsePerformanceResponse", () => {
    test("parses a full performance response", () => {
        const resp: PerformanceResponse__Output = {
            performance: 456.78,
            difficulty: {
                starRating: 5.25,
                approachRate: 9,
                overallDifficulty: 8,
                circleSize: 4,
                drainRate: 5,
                maxCombo: 500 as any,
                bpm: 180,
                length: 120,
                drainLength: 100,
                attributesJson: '{"star_rating":5.25}',
                error: ""
            },
            score: {
                rulesetId: 0,
                beatmapId: 75,
                beatmapName: "Test - Beatmap [Normal]",
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
                extraJson: '{"custom":"value"}'
            },
            error: ""
        }

        const result = parsePerformanceResponse(resp)

        expect(result.performance).toBe(456.78)
        expect(result.difficulty.starRating).toBe(5.25)
        expect(result.score.rulesetId).toBe(0)
        expect(result.score.beatmapId).toBe(75)
        expect(result.score.beatmapName).toBe("Test - Beatmap [Normal]")
        expect(result.score.accuracy).toBe(99.5)
        expect(result.score.combo).toBe(500)
        expect(result.score.statistics).toEqual({ great: 490, ok: 5, meh: 3, miss: 2 })
        expect(result.performanceAttributes.total).toBe(456.78)
        expect(result.performanceAttributes.aim).toBe(200)
        expect(result.performanceAttributes.speed).toBe(150)
        expect(result.performanceAttributes.accuracyPp).toBe(50)
        expect(result.performanceAttributes.flashlight).toBe(0)
        expect(result.performanceAttributes.effectiveMissCount).toBe(2)
        expect(result.performanceAttributes.extra).toEqual({ custom: "value" })
        expect(result.error).toBeUndefined()
    })

    test("applies defaults for missing fields", () => {
        const resp: PerformanceResponse__Output = {}

        const result = parsePerformanceResponse(resp)

        expect(result.performance).toBe(0)
        expect(result.difficulty.starRating).toBe(0)
        expect(result.score.rulesetId).toBe(0)
        expect(result.score.beatmapId).toBe(0)
        expect(result.score.beatmapName).toBe("")
        expect(result.score.accuracy).toBe(0)
        expect(result.score.combo).toBe(0)
        expect(result.score.statistics).toEqual({})
        expect(result.performanceAttributes.total).toBe(0)
        expect(result.performanceAttributes.aim).toBe(0)
        expect(result.performanceAttributes.speed).toBe(0)
        expect(result.performanceAttributes.accuracyPp).toBe(0)
        expect(result.performanceAttributes.flashlight).toBe(0)
        expect(result.performanceAttributes.effectiveMissCount).toBe(0)
        expect(result.performanceAttributes.extra).toEqual({})
    })

    test("handles invalid extraJson gracefully", () => {
        const resp: PerformanceResponse__Output = {
            performanceAttributes: {
                extraJson: "not valid json"
            }
        }

        const result = parsePerformanceResponse(resp)

        expect(result.performanceAttributes.extra).toEqual({})
    })

    test("handles empty extraJson", () => {
        const resp: PerformanceResponse__Output = {
            performanceAttributes: {
                extraJson: ""
            }
        }

        const result = parsePerformanceResponse(resp)

        expect(result.performanceAttributes.extra).toEqual({})
    })
})

describe("isGrpcReady", () => {
    test("returns false when no client has been created", () => {
        expect(isGrpcReady()).toBe(false)
    })
})

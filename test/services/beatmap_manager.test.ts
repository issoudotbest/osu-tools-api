import { describe, test, expect, jest, beforeEach, afterEach } from "@jest/globals"
import { existsSync, unlinkSync } from "fs"
import path from "path"

jest.mock("undici", () => ({
    request: jest.fn()
}))

let mockTtlDays = 1

jest.mock("../../src/config", () => ({
    getConfig: () => ({
        auth: { api_keys: ["test"] },
        general: { mode: "development", public_api_port: 3000, private_api_port: 3001, api_prefix: "/api", grpc_socket_path: "/tmp/test.sock", reqs_per_min: 60 },
        cache: { path: "./cache", ttl_days: mockTtlDays },
        urls: { webhook: "", webhook_token: "", loki: "" }
    }),
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

import { getBeatmapData, clearMemoryCache } from "../../src/services/beatmap_manager"
import { request as mockRequest } from "undici"

const mockedRequest = mockRequest as jest.MockedFunction<typeof mockRequest>

describe("Beatmap manager", () => {
    // Use unique IDs per test to avoid cache collision
    const downloadTestId = 11111
    const errorTestId = 22222
    const networkFailId = 33333
    const fallbackTestId = 44444
    const cacheHitTestId = 55555
    const clearCacheTestId = 66666
    const noDiskCacheTestId = 77777
    const dedupTestId = 88888

    beforeEach(() => {
        jest.clearAllMocks()
        clearMemoryCache()
        mockTtlDays = 1
    })

    afterEach(() => {
        // Clean up any downloaded cache files
        const cacheDir = "./cache"
        const testIds: number[] = [downloadTestId, errorTestId, networkFailId, fallbackTestId, cacheHitTestId, clearCacheTestId, noDiskCacheTestId, dedupTestId]
        testIds.forEach((id: number) => {
            const f = path.join(cacheDir, `${id}.osu`)
            if (existsSync(f)) {
                try {
                    unlinkSync(f)
                } catch {
                    /* ignore */
                }
            }
        })
    })

    test("downloads beatmap from osu.ppy.sh on cache miss", async () => {
        const fakeData = Buffer.from("osu file format v14\n")
        mockedRequest.mockResolvedValue({
            statusCode: 200,
            body: {
                arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                text: async () => fakeData.toString()
            }
        } as any)

        const result = await getBeatmapData(downloadTestId)

        expect(result.ok).toBe(true)
        if (result.ok) {
            expect(result.data).toEqual(fakeData)
        }
        expect(mockedRequest).toHaveBeenCalledTimes(1)
        const calledUrl = mockedRequest.mock.calls[0][0] as string
        expect(calledUrl).toContain(String(downloadTestId))
        expect(calledUrl).toContain("osu.ppy.sh/osu/")
    })

    test("falls back to catboy.best when osu.ppy.sh fails", async () => {
        const fakeData = Buffer.from("osu file format v14\n")
        const errorResponse = {
            statusCode: 503,
            body: {
                arrayBuffer: async () => new ArrayBuffer(0),
                text: async () => "Service unavailable"
            }
        } as any
        const successResponse = {
            statusCode: 200,
            body: {
                arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                text: async () => fakeData.toString()
            }
        } as any

        mockedRequest.mockResolvedValueOnce(errorResponse).mockResolvedValueOnce(successResponse)

        const result = await getBeatmapData(fallbackTestId)

        expect(result.ok).toBe(true)
        if (result.ok) {
            expect(result.data).toEqual(fakeData)
        }
        expect(mockedRequest).toHaveBeenCalledTimes(2)
        expect(mockedRequest.mock.calls[0][0] as string).toContain("osu.ppy.sh")
        expect(mockedRequest.mock.calls[1][0] as string).toContain("catboy.best")
    })

    test("returns error when all sources fail with HTTP error", async () => {
        mockedRequest.mockResolvedValue({
            statusCode: 404,
            body: {
                arrayBuffer: async () => new ArrayBuffer(0),
                text: async () => "Not found"
            }
        } as any)

        const result = await getBeatmapData(errorTestId)

        expect(result.ok).toBe(false)
        if (!result.ok) {
            expect(result.error).toContain("404")
        }
        expect(mockedRequest).toHaveBeenCalledTimes(2)
    })

    test("returns error when all sources fail with network failure", async () => {
        mockedRequest.mockRejectedValue(new Error("Network error"))

        const result = await getBeatmapData(networkFailId)

        expect(result.ok).toBe(false)
        if (!result.ok) {
            expect(result.error).toContain("Network error")
        }
        expect(mockedRequest).toHaveBeenCalledTimes(2)
    })

    test("serves from memory cache on second call without downloading", async () => {
        const fakeData = Buffer.from("osu file format v14\n")
        mockedRequest.mockResolvedValue({
            statusCode: 200,
            body: {
                arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                text: async () => fakeData.toString()
            }
        } as any)

        const first = await getBeatmapData(cacheHitTestId)
        expect(first.ok).toBe(true)
        expect(mockedRequest).toHaveBeenCalledTimes(1)

        const second = await getBeatmapData(cacheHitTestId)
        expect(second.ok).toBe(true)
        if (second.ok) {
            expect(second.data).toEqual(fakeData)
        }
        // Should not have downloaded again
        expect(mockedRequest).toHaveBeenCalledTimes(1)
    })

    test("clearMemoryCache for specific beatmap forces re-download", async () => {
        const fakeData = Buffer.from("osu file format v14\n")
        mockedRequest.mockResolvedValue({
            statusCode: 200,
            body: {
                arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                text: async () => fakeData.toString()
            }
        } as any)

        await getBeatmapData(clearCacheTestId)
        expect(mockedRequest).toHaveBeenCalledTimes(1)

        // Clear both memory and disk cache to force re-download
        clearMemoryCache(clearCacheTestId)
        const diskPath = path.join("./cache", `${clearCacheTestId}.osu`)
        if (existsSync(diskPath)) unlinkSync(diskPath)

        await getBeatmapData(clearCacheTestId)
        expect(mockedRequest).toHaveBeenCalledTimes(2)
    })

    test("works with ttl_days=0 (disk cache disabled, memory cache active)", async () => {
        mockTtlDays = 0
        const fakeData = Buffer.from("osu file format v14\n")
        mockedRequest.mockResolvedValue({
            statusCode: 200,
            body: {
                arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                text: async () => fakeData.toString()
            }
        } as any)

        const result = await getBeatmapData(noDiskCacheTestId)
        expect(result.ok).toBe(true)
        if (result.ok) {
            expect(result.data).toEqual(fakeData)
        }
        expect(mockedRequest).toHaveBeenCalledTimes(1)

        // Second call should hit memory cache
        const second = await getBeatmapData(noDiskCacheTestId)
        expect(second.ok).toBe(true)
        expect(mockedRequest).toHaveBeenCalledTimes(1)

        // No disk file should have been written
        const diskPath = path.join("./cache", `${noDiskCacheTestId}.osu`)
        expect(existsSync(diskPath)).toBe(false)
    })

    test("deduplicates concurrent requests for the same beatmap ID", async () => {
        const fakeData = Buffer.from("osu file format v14\n")

        // Simulate a slow response so both requests are truly concurrent
        let resolveFirst: (value: any) => void
        const slowPromise = new Promise(resolve => {
            resolveFirst = resolve
        })
        mockedRequest.mockImplementation(async () => {
            await slowPromise
            return {
                statusCode: 200,
                body: {
                    arrayBuffer: async () => fakeData.buffer.slice(fakeData.byteOffset, fakeData.byteOffset + fakeData.byteLength),
                    text: async () => fakeData.toString()
                }
            } as any
        })

        // Fire two concurrent requests for the same beatmap
        const p1 = getBeatmapData(dedupTestId)
        const p2 = getBeatmapData(dedupTestId)

        // Let both requests start and hit the inflight dedup, then resolve
        setTimeout(() => resolveFirst!(undefined), 50)

        const [result1, result2] = await Promise.all([p1, p2])

        expect(result1.ok).toBe(true)
        expect(result2.ok).toBe(true)
        // Only one download should have happened
        expect(mockedRequest).toHaveBeenCalledTimes(1)
    })
})

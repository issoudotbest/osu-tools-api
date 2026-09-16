import { describe, test, expect } from "@jest/globals"
import Logger, { formatPath } from "../../src/utils/logger"

describe("Logger", () => {
    test("can be instantiated and logs without throwing", () => {
        const logger = new Logger({ name: "test", mode: "development" }, "test-module", "submodule")
        expect(logger).toBeDefined()
        expect(() => logger.info({ message: "test info" })).not.toThrow()
        expect(() => logger.error({ message: "test error", error: new Error("test") })).not.toThrow()
        expect(() => logger.fatal({ message: "test fatal", fatal_error: new Error("fatal") })).not.toThrow()
        expect(() => logger.debug({ message: "test debug" })).not.toThrow()
    })

    test("does not throw when webhook URL is configured but unreachable", () => {
        const logger = new Logger({ name: "test", mode: "development", webhookUrl: "http://nonexistent:9999/webhook" }, "test")
        expect(() => logger.error({ message: "test webhook" })).not.toThrow()
    })
})

describe("formatPath", () => {
    test("concatenates baseUrl and path", () => {
        expect(formatPath({ baseUrl: "/api", path: "/difficulty" })).toBe("/api/difficulty")
    })

    test("strips trailing slash", () => {
        expect(formatPath({ baseUrl: "/api", path: "/" })).toBe("/api")
    })

    test("handles empty baseUrl", () => {
        expect(formatPath({ baseUrl: "", path: "/metrics" })).toBe("/metrics")
    })
})

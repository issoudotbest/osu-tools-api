import { describe, test, expect } from "@jest/globals"
import { z } from "zod"
import { formatZodError, grpcErrorToStatusCode } from "../../src/utils/validation"

describe("formatZodError", () => {
    test("formats a simple field error", () => {
        const schema = z.object({ name: z.string().min(1) })
        const result = schema.safeParse({ name: "" })
        expect(result.success).toBe(false)
        if (!result.success) {
            const formatted = formatZodError(result.error)
            expect(formatted).toHaveLength(1)
            expect(formatted[0].path).toBe("name")
            expect(formatted[0].message).toBeTruthy()
        }
    })

    test("formats nested array index errors", () => {
        const schema = z.object({
            items: z.array(z.number().min(0)).min(1)
        })
        const result = schema.safeParse({ items: [-1] })
        expect(result.success).toBe(false)
        if (!result.success) {
            const formatted = formatZodError(result.error)
            const itemError = formatted.find(e => e.path.includes("[0]"))
            expect(itemError).toBeDefined()
        }
    })

    test("formats multiple errors", () => {
        const schema = z.object({
            a: z.string().min(1),
            b: z.number().min(0)
        })
        const result = schema.safeParse({ a: "", b: -1 })
        expect(result.success).toBe(false)
        if (!result.success) {
            const formatted = formatZodError(result.error)
            expect(formatted.length).toBeGreaterThanOrEqual(2)
        }
    })
})

describe("grpcErrorToStatusCode", () => {
    test("returns 503 for UNAVAILABLE errors", () => {
        expect(grpcErrorToStatusCode("UNAVAILABLE: channel is in TRANSIENT_FAILURE")).toBe(503)
    })

    test("returns 503 for CONNECTION errors", () => {
        expect(grpcErrorToStatusCode("CONNECTION failed")).toBe(503)
    })

    test("returns 503 for TRANSIENT_FAILURE errors", () => {
        expect(grpcErrorToStatusCode("TRANSIENT_FAILURE")).toBe(503)
    })

    test("returns 503 for DEADLINE_EXCEEDED errors", () => {
        expect(grpcErrorToStatusCode("DEADLINE_EXCEEDED: deadline exceeded")).toBe(503)
    })

    test("returns 400 for INVALID_ARGUMENT errors", () => {
        expect(grpcErrorToStatusCode("INVALID_ARGUMENT: beatmap data is required")).toBe(400)
    })

    test("returns 502 for other errors", () => {
        expect(grpcErrorToStatusCode("Calculation error")).toBe(502)
        expect(grpcErrorToStatusCode("Internal error")).toBe(502)
    })
})

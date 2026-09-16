import { z } from "zod"

/**
 * @description Format a ZodError into a clean array of { path, message } objects to send to the user
 */
export function formatZodError(err: z.ZodError): Array<{ path: string; message: string }> {
    return err.issues.map(issue => ({
        path: issue.path
            .map(p => (typeof p === "number" ? `[${p}]` : p))
            .join(".")
            .replace(/\.\[/, "["),
        message: issue.message
    }))
}

/**
 * @description Maps a gRPC error message to an appropriate HTTP status code.
 * Connection errors -> 503, invalid argument -> 400, other -> 502.
 */
export function grpcErrorToStatusCode(error: string): number {
    if (/UNAVAILABLE|CONNECTION|channel|TRANSIENT_FAILURE|DEADLINE_EXCEEDED/i.test(error)) return 503
    if (/INVALID_ARGUMENT/i.test(error)) return 400
    return 502
}

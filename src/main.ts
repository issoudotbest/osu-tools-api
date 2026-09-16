import { spawn } from "child_process"
import path from "path"
import { getConfig, createLogger } from "./config"
import { startServer } from "./server"
import { waitForGrpc } from "./services/grpc_client"

const logger = createLogger("main")

const CSHARP_EXE = path.join(process.cwd(), "OsuToolsService", "bin", "Release", "net10.0", "linux-x64", "publish", "OsuToolsService")

let csharpProcess: ReturnType<typeof spawn> | null = null
let isRestarting = false
let isShuttingDown = false
let restartAttempts = 0
const MAX_RESTART_ATTEMPTS = 5
const RESTART_DELAY_MS = 2000

/**
 * @description start the c# service handling calculations
 */
function startCsharpService(): Promise<void> {
    return new Promise((resolve, reject) => {
        const config = getConfig()

        logger.info({ message: "Starting C# gRPC service..." })

        csharpProcess = spawn(CSHARP_EXE, {
            env: { ...process.env, GRPC_SOCKET_PATH: config.general.grpc_socket_path },
            stdio: ["ignore", "pipe", "pipe"]
        })

        let resolved = false

        csharpProcess.stdout?.on("data", (data: Buffer) => {
            const line = data.toString().trim()
            if (line) logger.info({ message: `C# service: ${line}` })
        })

        csharpProcess.stderr?.on("data", (data: Buffer) => {
            const line = data.toString().trim()
            if (line) logger.warn({ message: `C# service: ${line}` })
        })

        csharpProcess.on("error", err => {
            if (!resolved) {
                resolved = true
                reject(err)
            }
        })

        csharpProcess.on("exit", code => {
            csharpProcess = null
            if (!resolved) {
                resolved = true
                reject(new Error(`C# service exited before becoming ready (code ${code})`))
                return
            }
            if (isShuttingDown) return
            logger.error({ message: `C# service exited with code ${code}` })
            restartCsharpService()
        })

        csharpProcess.on("spawn", () => {
            if (!resolved) {
                resolved = true
                resolve()
            }
        })
    })
}

function stopCsharpService() {
    isShuttingDown = true
    if (csharpProcess) {
        logger.info({ message: "Stopping C# gRPC service..." })
        csharpProcess.kill("SIGTERM")
        csharpProcess = null
    }
}

async function restartCsharpService() {
    if (isRestarting) return
    if (restartAttempts >= MAX_RESTART_ATTEMPTS) {
        logger.fatal({ message: `C# service restart failed after ${MAX_RESTART_ATTEMPTS} attempts. Exiting.` })
        process.exit(1)
    }

    isRestarting = true
    restartAttempts++
    const delay = RESTART_DELAY_MS * restartAttempts
    logger.info({ message: `Restarting C# service (attempt ${restartAttempts}/${MAX_RESTART_ATTEMPTS}) in ${delay}ms...` })

    await new Promise(resolve => setTimeout(resolve, delay))

    try {
        await startCsharpService()
        const ready = await waitForGrpc(30000)
        if (ready) {
            logger.info({ message: "C# service restarted successfully" })
            restartAttempts = 0
        } else {
            logger.error({ message: "C# service restarted but gRPC not ready" })
            if (csharpProcess) csharpProcess.kill("SIGTERM")
        }
    } catch (err) {
        logger.fatal({ message: "C# service restart failed. Exiting.", fatal_error: err })
        process.exit(1)
    } finally {
        isRestarting = false
    }
}

async function main() {
    const config = getConfig()

    logger.info({ message: `osu-tools-api is starting up in ${config.general.mode} mode` })

    // Start the C# gRPC service as a child process
    try {
        await startCsharpService()
    } catch (err) {
        logger.fatal({ message: "Failed to start C# gRPC service", fatal_error: err })
        process.exit(1)
    }

    // Wait for the gRPC service to be available
    logger.info({ message: "Waiting for C# gRPC service..." })
    const grpcReady = await waitForGrpc(30000)

    if (!grpcReady) {
        logger.warn({ message: "C# gRPC service not available. Starting in degraded mode: calculations will fail until the service is up." })
    } else {
        logger.info({ message: "C# gRPC service is ready" })
    }

    // Start Express API server
    startServer()
}

main()

process.on("uncaughtException", err => {
    logger.fatal({ message: "Uncaught exception", fatal_error: err })
})

process.on("unhandledRejection", err => {
    logger.fatal({ message: "Unhandled rejection", fatal_error: err })
})

process.on("SIGINT", () => {
    stopCsharpService()
    process.exit(0)
})

process.on("SIGTERM", () => {
    stopCsharpService()
    process.exit(0)
})

process.on("exit", () => {
    stopCsharpService()
})

import promClient from "prom-client"
import { getConfig, createLogger } from "../config"

function getDevPrefix(): string {
    try {
        const config = getConfig()
        return config.general.mode === "development" ? "dev" : ""
    } catch {
        return ""
    }
}

const logger = createLogger("service", "prometheus")
logger.info({ message: "Prometheus exporter running" })

// HTTP Metrics

export const httpRequestDuration = new promClient.Histogram({
    name: "osu_tools_api" + getDevPrefix() + "_http_request_duration_seconds",
    help: "HTTP request duration in seconds",
    labelNames: ["method", "route", "status_code"],
    buckets: [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
})

export const httpRequestsTotal = new promClient.Counter({
    name: "osu_tools_api" + getDevPrefix() + "_http_requests_total",
    help: "Total HTTP requests",
    labelNames: ["method", "route", "status_code"]
})

// gRPC Metrics

export const grpcCallDuration = new promClient.Histogram({
    name: "osu_tools_api" + getDevPrefix() + "_grpc_call_duration_seconds",
    help: "gRPC call duration in seconds",
    labelNames: ["method", "status"],
    buckets: [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
})

// Beatmap Cache Metrics

export const beatmapCacheHits = new promClient.Counter({
    name: "osu_tools_api" + getDevPrefix() + "_beatmap_cache_hits_total",
    help: "Beatmap cache hits"
})

export const beatmapCacheMisses = new promClient.Counter({
    name: "osu_tools_api" + getDevPrefix() + "_beatmap_cache_misses_total",
    help: "Beatmap cache misses"
})

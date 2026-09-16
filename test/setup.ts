import { existsSync, copyFileSync, writeFileSync, readFileSync } from "fs"
import { join } from "path"

const TEST_API_KEY = "test-api-key"

// Use a separate config file for tests so we never modify the user's config.json
const testConfigPath = join(process.cwd(), "config.test.json")
const examplePath = join(process.cwd(), "config-example.json")

// Point config.ts at our test config before any test module imports it
process.env.CONFIG_PATH = testConfigPath

if (!existsSync(testConfigPath)) {
    if (existsSync(examplePath)) {
        copyFileSync(examplePath, testConfigPath)
    } else {
        writeFileSync(
            testConfigPath,
            JSON.stringify(
                {
                    auth: { api_keys: [TEST_API_KEY] },
                    general: {
                        mode: "development",
                        public_api_port: 3000,
                        private_api_port: 3001,
                        api_prefix: "/api",
                        grpc_socket_path: "/tmp/osu-tools-calc-test.sock",
                        reqs_per_min: 60
                    },
                    cache: { path: "./cache", ttl_days: 1 },
                    urls: { webhook: "", webhook_token: "", loki: "" }
                },
                null,
                4
            )
        )
    }
}

// Ensure the test API key is present and api_prefix is /api for test routes
const config = JSON.parse(readFileSync(testConfigPath, "utf-8"))
if (!config.auth.api_keys.includes(TEST_API_KEY)) {
    config.auth.api_keys.push(TEST_API_KEY)
}
config.general.api_prefix = "/api"
writeFileSync(testConfigPath, JSON.stringify(config, null, 4))

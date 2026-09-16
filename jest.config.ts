import { createDefaultPreset } from "ts-jest"

const tsJestTransformCfg = createDefaultPreset().transform

/** @type {import("jest").Config} **/
export default {
    testEnvironment: "node",
    transform: {
        ...tsJestTransformCfg
    },
    testMatch: ["**/test/**/*.test.ts"],
    testPathIgnorePatterns: ["node_modules", "references"],
    modulePathIgnorePatterns: ["<rootDir>/build/"],
    setupFiles: ["<rootDir>/test/setup.ts"]
}

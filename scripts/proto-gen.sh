#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Generating TypeScript stubs from proto"
npx proto-loader-gen-types \
    --grpcLib=@grpc/grpc-js \
    --outDir="$ROOT_DIR/src/generated" \
    "$ROOT_DIR/proto/calculator.proto"

echo "Proto generation complete"
echo "Note: C# gRPC stubs are auto-generated during 'dotnet build' via the <Protobuf> item in the .csproj"

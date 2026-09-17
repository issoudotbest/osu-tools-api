# Stage 1: Build the C# gRPC service (self-contained, no .NET runtime needed in final image)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS cs-build
WORKDIR /src
COPY proto/ ./proto/
COPY OsuToolsService/ ./OsuToolsService/
RUN dotnet publish -c Release --self-contained true --runtime linux-x64 \
    ./OsuToolsService/OsuToolsService.csproj \
    -o /publish

# Stage 2: Build the TypeScript API
FROM node:22-slim AS ts-build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY proto/ ./proto/
COPY scripts/ ./scripts/
COPY tsconfig.json ./
COPY src/ ./src/
RUN bash scripts/proto-gen.sh
RUN npx tsc

# Stage 3: Runtime image
FROM node:22-slim
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY --from=ts-build /app/build/ ./build/
COPY --from=cs-build /publish/ ./OsuToolsService/bin/Release/net10.0/linux-x64/publish/
COPY proto/ ./proto/

EXPOSE 3000 3001
CMD ["npm", "start"]

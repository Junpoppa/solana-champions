# Solana Champions — one container serves the website + websocket lobby on one port.
# The Unity WebGL build ships in web/public/unity, so no Unity is needed here.
FROM node:20-alpine AS build
WORKDIR /app

# web: deps + build (dist embeds the committed Unity build from web/public/unity)
COPY web/package*.json web/
RUN cd web && npm ci
COPY web/ web/
RUN cd web && npm run build

# server: deps + compile TS
COPY server/package*.json server/
RUN cd server && npm ci
COPY server/ server/
RUN cd server && npm run build

FROM node:20-alpine
WORKDIR /app
# server resolves the site at ../../web/dist relative to its own files — keep the sibling layout
COPY --from=build /app/server/dist server/dist
COPY --from=build /app/server/package*.json server/
RUN cd server && npm ci --omit=dev
COPY --from=build /app/web/dist web/dist

ENV PORT=8787
EXPOSE 8787
WORKDIR /app/server
CMD ["npm", "start"]

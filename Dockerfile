FROM node:22-bookworm-slim AS client
WORKDIR /build/client
COPY client/package*.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY Directory.Build.props Directory.Packages.props global.json ImageStore.slnx ./
COPY src/ src/
COPY --from=client /build/client/dist/image-store-client/browser/ src/ImageStore.Api/wwwroot/
RUN dotnet publish src/ImageStore.Api -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish/ ./
RUN mkdir -p /data/uploads /data/keys && chown -R app:app /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ImageStore.Api.dll"]

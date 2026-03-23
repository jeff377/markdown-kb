# Stage 1 – build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/MarkdownKB/MarkdownKB.csproj src/MarkdownKB/
RUN dotnet restore src/MarkdownKB/MarkdownKB.csproj

COPY . .
RUN dotnet publish src/MarkdownKB/MarkdownKB.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# Stage 2 – runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MarkdownKB.dll"]

# Stage 1 – build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MarkdownKB.Core/MarkdownKB.Core.csproj src/MarkdownKB.Core/
COPY src/MarkdownKB.Search/MarkdownKB.Search.csproj src/MarkdownKB.Search/
COPY src/MarkdownKB.Web/MarkdownKB.Web.csproj src/MarkdownKB.Web/
RUN dotnet restore src/MarkdownKB.Web/MarkdownKB.Web.csproj

COPY . .
RUN dotnet publish src/MarkdownKB.Web/MarkdownKB.Web.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# Stage 2 – runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MarkdownKB.Web.dll"]

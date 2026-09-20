# Build context is the repository root:
#   docker build -f PaymentOrchestratorAssessment.Api/Dockerfile -t payment-orchestrator-api .
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy every project file first so the restore layer is cached independently of
# source changes. The Api project transitively references the other three.
COPY ["PaymentOrchestratorAssessment.Core/PaymentOrchestratorAssessment.Core.csproj", "PaymentOrchestratorAssessment.Core/"]
COPY ["PaymentOrchestratorAssessment.Application/PaymentOrchestratorAssessment.Application.csproj", "PaymentOrchestratorAssessment.Application/"]
COPY ["PaymentOrchestratorAssessment.Infrastructure/PaymentOrchestratorAssessment.Infrastructure.csproj", "PaymentOrchestratorAssessment.Infrastructure/"]
COPY ["PaymentOrchestratorAssessment.Api/PaymentOrchestratorAssessment.Api.csproj", "PaymentOrchestratorAssessment.Api/"]
RUN dotnet restore "PaymentOrchestratorAssessment.Api/PaymentOrchestratorAssessment.Api.csproj"

COPY PaymentOrchestratorAssessment.Core/ PaymentOrchestratorAssessment.Core/
COPY PaymentOrchestratorAssessment.Application/ PaymentOrchestratorAssessment.Application/
COPY PaymentOrchestratorAssessment.Infrastructure/ PaymentOrchestratorAssessment.Infrastructure/
COPY PaymentOrchestratorAssessment.Api/ PaymentOrchestratorAssessment.Api/

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaymentOrchestratorAssessment.Api/PaymentOrchestratorAssessment.Api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Created and chowned while still root, because a named volume mounted here
# inherits the ownership of the image directory underneath it. Without this the
# non-root app user cannot write the SQLite file.
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Payments="Data Source=/app/data/payments.db"

ENTRYPOINT ["dotnet", "PaymentOrchestratorAssessment.Api.dll"]

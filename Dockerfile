FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore with layer caching
COPY src/FamilyTrustFund.Domain/FamilyTrustFund.Domain.csproj src/FamilyTrustFund.Domain/
COPY src/FamilyTrustFund.Application/FamilyTrustFund.Application.csproj src/FamilyTrustFund.Application/
COPY src/FamilyTrustFund.Infrastructure/FamilyTrustFund.Infrastructure.csproj src/FamilyTrustFund.Infrastructure/
COPY src/FamilyTrustFund.Api/FamilyTrustFund.Api.csproj src/FamilyTrustFund.Api/
RUN dotnet restore src/FamilyTrustFund.Api/FamilyTrustFund.Api.csproj

COPY src/ src/
RUN dotnet publish src/FamilyTrustFund.Api/FamilyTrustFund.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql probes for the GSSAPI/Kerberos native library at startup. The slim
# .NET runtime image does not ship it, so PostgreSQL connections log
# "Cannot load library libgssapi_krb5.so.2". Install it so connection setup
# (and password authentication) initialises cleanly.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Evidence storage root (AGENTS §9 / ADR-036). Created and owned by the
# non-root `app` user so the named volume in docker-compose works out of
# the box.
RUN mkdir -p /var/lib/familytrustfund/storage \
    && chown -R app:app /var/lib/familytrustfund/storage

COPY --from=build /app/publish .
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FamilyTrustFund.Api.dll"]
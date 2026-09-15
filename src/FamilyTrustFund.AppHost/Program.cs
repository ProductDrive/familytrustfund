using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("postgres-user", "postgres");
var pgPassword = builder.AddParameter("postgres-password", "postgres");

var postgres = builder.AddPostgres("postgres", userName: pgUser, password: pgPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume(name: "familytrustfund-postgres-data")
    .WithEndpoint("tcp", endpoint => endpoint.Port = 5433);

var db = postgres.AddDatabase("familytrustfund");

var pgAdmin = builder.AddContainer("pgadmin", "dpage/pgadmin4")
    .WithEnvironment("PGADMIN_DEFAULT_EMAIL", "admin@familytrustfund.com")
    .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", "admin")
    .WithEnvironment("PGADMIN_CONFIG_SERVER_MODE", "False")
    .WithEnvironment("PGADMIN_CONFIG_MASTER_PASSWORD_REQUIRED", "False")
    .WithBindMount(
        Path.Combine(builder.Environment.ContentRootPath, "pgadmin", "servers.json"),
        "/pgadmin4/servers.json")
    .WithBindMount(
        Path.Combine(builder.Environment.ContentRootPath, "pgadmin", "pgpass"),
        "/pgpass")
    .WithHttpEndpoint(port: 5050, targetPort: 80, name: "pgadmin-http")
    .WaitFor(postgres);

builder.AddProject<Projects.FamilyTrustFund_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.Build().Run();

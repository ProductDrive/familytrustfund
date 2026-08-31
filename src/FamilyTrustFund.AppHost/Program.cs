using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("postgres-user", "postgres");
var pgPassword = builder.AddParameter("postgres-password", "postgres");

var postgres = builder.AddPostgres("postgres", userName: pgUser, password: pgPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEndpoint("tcp", endpoint => endpoint.Port = 5433);

var db = postgres.AddDatabase("familytrustfund");

builder.AddProject<Projects.FamilyTrustFund_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.Build().Run();

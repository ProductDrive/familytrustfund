using System.Security.Claims;
using FamilyTrustFund.Api.Antiforgery;
using FamilyTrustFund.Api.Auth;
using FamilyTrustFund.Api.Administration;
using FamilyTrustFund.Api.Authorization;
using FamilyTrustFund.Api.Contributions;
using FamilyTrustFund.Api.Evidence;
using FamilyTrustFund.Api.Funds;
using FamilyTrustFund.Api.Loans;
using FamilyTrustFund.Api.Membership;
using FamilyTrustFund.Api.Payments;
using FamilyTrustFund.Api.Repayments;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Data;
using FamilyTrustFund.Infrastructure.Evidence;
using FamilyTrustFund.Infrastructure.Identity;
using FamilyTrustFund.Infrastructure.Payments;
using FamilyTrustFund.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ---- Database (PostgreSQL) ----
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("familytrustfund")
        ?? "Host=localhost;Port=5432;Database=familytrustfund;Username=postgres;Password=postgres"));

// ---- Application services ----
builder.Services.AddScoped<FamilyTrustFund.Application.Audit.IAuditLog, FamilyTrustFund.Infrastructure.Audit.AuditLog>();
builder.Services.AddScoped<FamilyTrustFund.Application.Funds.IFundRepository, FamilyTrustFund.Infrastructure.Funds.FundRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Funds.FundService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Funds.FundTransitionService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Membership.IMembershipRepository, FamilyTrustFund.Infrastructure.Membership.MembershipRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Membership.MembershipService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Loans.ILoanRepository, FamilyTrustFund.Infrastructure.Loans.LoanRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Loans.LoanService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Contributions.IContributionRepository, FamilyTrustFund.Infrastructure.Contributions.ContributionRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Contributions.ContributionService>();

// ---- Payments (Phase 8): provider abstraction + disbursement ----
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection(PaystackOptions.SectionName));
builder.Services.AddHttpClient<PaystackPaymentProvider>((sp, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<PaystackPaymentProvider>());
builder.Services.AddScoped<IPaymentProviderRegistry, PaymentProviderRegistry>();
builder.Services.AddScoped<FamilyTrustFund.Application.Payments.IPaymentRepository, FamilyTrustFund.Infrastructure.Payments.PaymentRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Payments.ICapitalFundingRepository, FamilyTrustFund.Infrastructure.Payments.CapitalFundingRepository>();
builder.Services.AddScoped<DisbursementService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Payments.CapitalFundingService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Repayments.IRepaymentRepository, FamilyTrustFund.Infrastructure.Repayments.RepaymentRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Repayments.RepaymentService>();
builder.Services.AddScoped<FamilyTrustFund.Application.Repayments.IPendingRepaymentRepository, FamilyTrustFund.Infrastructure.Repayments.PendingRepaymentRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Repayments.PendingRepaymentService>();

// ---- Evidence + storage (Phase 14): payment evidence attachments ----
builder.Services.Configure<FileSystemStorageOptions>(builder.Configuration.GetSection(FileSystemStorageOptions.SectionName));
builder.Services.AddSingleton<FamilyTrustFund.Application.Storage.IFileStorage, FileSystemStorage>();
builder.Services.AddScoped<FamilyTrustFund.Application.Evidence.IEvidenceRepository, EvidenceRepository>();
builder.Services.AddScoped<FamilyTrustFund.Application.Evidence.EvidenceService>();

// ---- JSON options (readable string enums in API contracts) ----
// Enums are serialized with their exact member names (e.g. "Active",
// "Family", "Weekly") without camelCasing, matching the frontend's type
// definitions exactly.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Anti-forgery for form-based (multipart) endpoints such as payment
// evidence uploads. Minimal API endpoints that bind IFormFile automatically
// carry anti-forgery metadata; this service registers the validator and
// app.UseAntiforgery() enables the middleware.
builder.Services.AddAntiforgery(options =>
{
    options.FormFieldName = "__RequestVerificationToken";
});

// ---- ASP.NET Core Identity ----
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "FamilyTrustFund.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/api/auth/login";
    options.LogoutPath = "/api/auth/logout";
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// ---- Google OIDC (production path; skipped when no client id configured) ----
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
// Public base URL of the deployed frontend. Set this when the SPA is served
// from a different origin than the API (e.g. Vercel) and reaches the API
// through a same-origin rewrite. The auth cookie is host-scoped, so the OIDC
// redirect_uri must point at the frontend origin; otherwise Google sends the
// browser straight to the API host and the session cookie is never sent back
// to the frontend. Leave empty to derive the redirect from the request (local
// development). This must also be registered in the Google OAuth client.
var googlePublicBaseUrl = builder.Configuration["Authentication:Google:PublicBaseUrl"];
var hasGoogleConfig = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);
if (hasGoogleConfig)
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect("Google", options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.CallbackPath = "/api/auth/google/callback";
            options.ResponseType = "code";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    if (!string.IsNullOrWhiteSpace(googlePublicBaseUrl))
                    {
                        context.ProtocolMessage.RedirectUri =
                            googlePublicBaseUrl.TrimEnd('/') + options.CallbackPath;
                    }

                    return Task.CompletedTask;
                },
            };
        });
}

// Development-only mock sign-in, always available in Development so the app
// remains usable even when Google OIDC is configured. Exercises the real
// Identity + cookie + role pipeline. Never enabled outside development.
builder.Services.Configure<DevAuthOptions>(o =>
{
    o.UseDevAuth = builder.Environment.IsDevelopment();
    o.GoogleConfigured = hasGoogleConfig;
});

// ---- Authorization policies ----
builder.Services.AddAuthorizationBuilder().AddApiPolicies();

// ---- OpenAPI document (used by Swagger UI) ----
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Family Trust Fund API";
        document.Info.Version = "v1";
        document.Info.Description = "REST API for the Family Trust Fund lending platform " +
            "(guarantor-led, closed-group lending). Authentication is cookie-based; " +
            "authorize via /api/auth/google/challenge and use the session cookie.";
        document.Info.Contact = new Microsoft.OpenApi.OpenApiContact
        {
            Name = "Family Trust Fund",
        };
        return Task.CompletedTask;
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:5173")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// ---- Apply migrations on startup (local/dev convenience) ----
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
}

// ---- API documentation (OpenAPI + Swagger UI) ----
// Enabled in Development by default; can be enabled in other environments
// through configuration ("Features:EnableApiDocs": true).
var enableApiDocs = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Features:EnableApiDocs");
if (enableApiDocs)
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Family Trust Fund API v1");
        options.DocumentTitle = "Family Trust Fund API";
        options.RoutePrefix = "swagger";
        options.DisplayRequestDuration();
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapAntiforgeryEndpoints();
app.MapFundEndpoints();
app.MapMembershipEndpoints();
app.MapLoanEndpoints();
app.MapContributionEndpoints();
app.MapEvidenceEndpoints();
app.MapPaymentEndpoints();
app.MapCapitalFundingEndpoints();
app.MapRepaymentEndpoints();
app.MapPendingRepaymentEndpoints();
app.MapAdminEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapDevAuthEndpoints();
    app.MapDevPaymentEndpoints();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/", () =>
{
    var payload = new Dictionary<string, string>
    {
        ["service"] = "FamilyTrustFund.Api",
    };
    if (enableApiDocs)
    {
        payload["openapi"] = "/openapi/v1.json";
        payload["swagger"] = "/swagger";
    }
    return Results.Ok(payload);
});

app.Run();

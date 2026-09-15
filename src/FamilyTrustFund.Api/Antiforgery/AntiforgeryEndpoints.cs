using Microsoft.AspNetCore.Antiforgery;

namespace FamilyTrustFund.Api.Antiforgery;

public static class AntiforgeryEndpoints
{
    public static IEndpointRouteBuilder MapAntiforgeryEndpoints(this IEndpointRouteBuilder app)
    {
        // Issues a request anti-forgery token (and its matching cookie) for use
        // by the SPA. The frontend submits it as the "__RequestVerificationToken"
        // form field on multipart/form-data requests (e.g. payment evidence
        // uploads), which ASP.NET Core validates via app.UseAntiforgery().
        app.MapGet("/api/antiforgery/token", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new AntiforgeryTokenResponse(tokens.RequestToken));
        });

        return app;
    }

    public sealed record AntiforgeryTokenResponse(string? RequestToken);
}
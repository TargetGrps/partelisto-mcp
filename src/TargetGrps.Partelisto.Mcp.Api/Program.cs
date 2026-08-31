using Microsoft.AspNetCore.Authentication.JwtBearer;
using TargetGrps.Partelisto.Mcp.Api.Auth;
using TargetGrps.Partelisto.Mcp.Api.Tools;
using TargetGrps.Partelisto.Mcp.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddPartelistoGateway(builder.Configuration);

string keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? throw new InvalidOperationException("Keycloak:Authority is not configured.");
string keycloakAudience = builder.Configuration["Keycloak:Audience"] ?? "partelisto-mcp";

// Validates the token's signature/issuer/expiry against Keycloak (via its standard OIDC discovery
// document — no manual key management). Populates HttpContext.User so PartelistoTools.RequireScope
// can read the "scope" claim per call. This is the one place a token is actually verified: the raw
// bearer string still gets forwarded to the gateway unchanged afterwards, which does its own,
// unrelated OwnerAccess/tenant check — see PartelistoTools' class doc for why both layers exist.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakAuthority;
        options.Audience = keycloakAudience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "partelisto", Title = "Partelisto", Version = "1.0.0" };
        options.ServerInstructions =
            "Guest check-in and SES.HOSPEDAJES (police registration) compliance for the signed-in host's " +
            "short-term rental properties. Tools return status/completeness only — never guest documents, " +
            "email, phone, or passport/DNI numbers. Read tools need the partelisto:read grant; " +
            "send_guest_checkin_link additionally needs partelisto:write, sends a real email, and should " +
            "be confirmed with the host before calling it.";
    })
    .WithHttpTransport()
    .WithToolsFromAssembly();

WebApplication app = builder.Build();

// Populates HttpContext.User from the bearer token when one is present and valid; does not itself
// reject unauthenticated requests (no [Authorize] / RequireAuthorization here) — tools/list must stay
// reachable without a token, and each tool enforces its own required scope. See PartelistoTools.
app.UseAuthentication();

app.MapMcp("/mcp");

// RFC 9728 protected-resource metadata, so spec-compliant MCP OAuth clients can discover the
// authorization server (Keycloak) and the two grantable scopes, instead of the host having to paste a
// token in by hand. Points at the same realm the web app and gateway already trust.
app.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) =>
{
    string resource = $"{request.Scheme}://{request.Host}";

    return Results.Json(new
    {
        resource,
        authorization_servers = new[] { keycloakAuthority },
        bearer_methods_supported = new[] { "header" },
        scopes_supported = new[] { PartelistoScopes.Read, PartelistoScopes.Write }
    });
});

// Named /healthz, not /health, to match the other TargetGrps services' convention.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

// Kept for consistency with the other TargetGrps services' logging/observability convention, even
// though this service does not use ApiServiceBootstrapper (it owns no data — no Mongo, no tenancy
// middleware to bootstrap; see README for why it deviates from the usual service-structure template).
public partial class Program
{
    public const string AppName = "targetgrps-partelisto-mcp";
}

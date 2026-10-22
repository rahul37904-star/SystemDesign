using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;

var builder = WebApplication.CreateBuilder(args);
var identity = builder.Configuration.GetSection("AzureAd");

foreach (var setting in new[] { "Instance", "TenantId", "ClientId", "Audience" })
{
    if (string.IsNullOrWhiteSpace(identity[setting]))
    {
        throw new InvalidOperationException($"Missing configuration: AzureAd:{setting}");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(identity);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("UserAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireScope("access_as_user");
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/hello", () => Results.Ok(new { message = "Hello from the learning API!" }));

app.MapGet("/api/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        name = user.FindFirst("name")?.Value,
        objectId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
    }))
    .RequireAuthorization("UserAccess");

app.Run();

using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .Build();

var tenantId = configuration["Entra:TenantId"]
    ?? throw new InvalidOperationException("Missing setting: Entra:TenantId");
var clientId = configuration["Entra:ClientId"]
    ?? throw new InvalidOperationException("Missing setting: Entra:ClientId");
var apiScope = configuration["Entra:ApiScope"]
    ?? throw new InvalidOperationException("Missing setting: Entra:ApiScope");
var apiUrl = configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException($"Missing setting: ApiBaseUrl in appsettings.{environment}.json");

var identityClient = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithRedirectUri("http://localhost")
    .Build();

var token = await identityClient
    .AcquireTokenInteractive(new[] { apiScope })
    .WithUseEmbeddedWebView(false)
    .ExecuteAsync();

using var http = new HttpClient { BaseAddress = new Uri(apiUrl) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

using var response = await http.GetAsync("/api/me");
var body = await response.Content.ReadAsStringAsync();
if (!response.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"API returned {(int)response.StatusCode}: {body}");
}

response.EnsureSuccessStatusCode();

Console.WriteLine(body);

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Homespun.Client;
using Homespun.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient to point to the server API
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register HTTP API services
builder.Services.AddScoped<HttpProjectApiService>();
builder.Services.AddScoped<HttpSessionApiService>();
builder.Services.AddScoped<HttpPullRequestApiService>();
builder.Services.AddScoped<HttpIssueApiService>();
builder.Services.AddScoped<HttpCloneApiService>();
builder.Services.AddScoped<HttpNotificationApiService>();
builder.Services.AddScoped<HttpOrchestrationApiService>();

await builder.Build().RunAsync();

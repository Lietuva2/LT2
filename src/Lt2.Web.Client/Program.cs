using System.Globalization;
using Lt2.Web.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var culture = new CultureInfo("lt-LT");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Owner, 2026-11-17: the browser's console shows warnings and errors only, in every environment; the HTTP client factory
// otherwise logs every API request at Information.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddLocalization();
builder.Services.AddLt2HttpApiClients(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();

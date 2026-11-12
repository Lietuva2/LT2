using System.Globalization;
using Lt2.Web.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var culture = new CultureInfo("lt-LT");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddLocalization();
builder.Services.AddLt2HttpApiClients(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();

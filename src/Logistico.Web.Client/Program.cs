using System.Globalization;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Client;
using Logistico.Web.Client.Services;
using Logistico.Web.Client.Ui;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// UI strings live in Resources/UiText[.ar].resx (see UiText.cs).
builder.Services.AddLocalization(options => options.ResourcesPath = UiText.ResourcesPath);

// Same-origin API: the browser sends the sign-in cookie automatically.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(30),
});
builder.Services.AddScoped<ShipmentsApi>();
builder.Services.AddScoped<OperationsApi>();
builder.Services.AddScoped<OpsState>();
builder.Services.AddScoped<Confirmer>();
builder.Services.AddScoped<MerchantsApi>();
builder.Services.AddScoped<AccountApi>();
builder.Services.AddScoped<TeamApi>();
builder.Services.AddScoped<CourierApi>();
builder.Services.AddScoped<Toaster>();

// The same staff policies as the API (StaffPolicies), so pages and menus hide what the user's role cannot use.
builder.Services.AddAuthorizationCore(options =>
{
    foreach (var policy in StaffPolicies.All)
    {
        options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(StaffPolicies.RolesFor(policy)));
    }
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

var host = builder.Build();

// The server renders <html lang="…"> from the user's choice (Arabic by default); the client follows it.
var js = host.Services.GetRequiredService<IJSRuntime>();
var name = await js.InvokeAsync<string>("lx.culture");
var culture = AppCultures.Create(name);
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
host.Services.GetRequiredService<HttpClient>().DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture.Name);

await host.RunAsync();

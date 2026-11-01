using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Logistico.Web.Client;
using Logistico.Web.Client.Services;
using Logistico.Web.Client.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Logistico.Web.Client.Tests;

/// <summary>
/// Renders the console's components as the browser does: localised texts from the real resource files, the API
/// answered by <see cref="Api"/>, browser storage and other JavaScript calls answered loosely (empty unless set up).
/// </summary>
public abstract class ClientTestContext : BunitContext
{
    protected ClientTestContext(string culture = "ar-IQ")
    {
        UseCulture(culture);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization(options => options.ResourcesPath = UiText.ResourcesPath);
        Services.AddSingleton(new HttpClient(Api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<CompanyApi>();
        Services.AddScoped<Toaster>();
    }

    /// <summary>Answers the API calls the component makes; unknown paths are 404.</summary>
    protected FakeApi Api { get; } = new();

    protected static void UseCulture(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }
}

/// <summary>A minimal stand-in for the REST API: one JSON answer per path.</summary>
public sealed class FakeApi : HttpMessageHandler
{
    private readonly Dictionary<string, object> answers = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public void Answer(string path, object body) => answers[path.TrimStart('/')] = body;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        Calls.Add($"{request.Method} {path}");
        return Task.FromResult(answers.TryGetValue(path, out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

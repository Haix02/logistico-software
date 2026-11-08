using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Logistico.SharedKernel.Mobile;

namespace Logistico.Qa;

/// <summary>A person using the system: signed in with their own account through the API the mobile app uses.</summary>
public sealed class Agent(string name, HttpClient http)
{
    public string Name { get; } = name;

    public HttpClient Http { get; } = http;

    public static async Task<Agent> SignInAsync(QaServer server, Journal journal, string email, string password = QaServer.Password)
    {
        var http = Anonymous(server, journal, email).Http;
        var response = await http.PostAsJsonAsync("/" + MobileApiRoutes.Token, new { email, password });
        Assert.True(response.IsSuccessStatusCode, $"{email} could not sign in: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("accessToken").GetString());
        return new Agent(email, http);
    }

    /// <summary>A visitor without an account, such as a customer on the tracking page.</summary>
    public static Agent Anonymous(QaServer server, Journal journal, string name) =>
        new(name, new HttpClient(new Journal.Recorder(journal, name)) { BaseAddress = server.BaseAddress, Timeout = TimeSpan.FromSeconds(60) });

    public async Task<T> GetAsync<T>(string route)
    {
        var response = await Http.GetAsync("/" + route);
        Assert.True(response.IsSuccessStatusCode, $"{Name}: GET {route} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<T> PostAsync<T>(string route, object? body)
    {
        var response = await PostAsync(route, body);
        Assert.True(response.IsSuccessStatusCode, $"{Name}: POST {route} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public Task<HttpResponseMessage> PostAsync(string route, object? body) => Http.PostAsJsonAsync("/" + route, body);

    public async Task OkAsync(string route, object? body)
    {
        var response = await PostAsync(route, body);
        Assert.True(response.IsSuccessStatusCode, $"{Name}: POST {route} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>A change guarded by the record's ETag, read first as the console does.</summary>
    public async Task PostWithETagAsync(string readRoute, string route, object body)
    {
        var read = await Http.GetAsync("/" + readRoute);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/" + route) { Content = JsonContent.Create(body) };
        message.Headers.IfMatch.Add(read.Headers.ETag!);
        var response = await Http.SendAsync(message);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent or HttpStatusCode.Created,
            $"{Name}: POST {route} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}

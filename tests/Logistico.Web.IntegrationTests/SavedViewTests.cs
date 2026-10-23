using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Each person keeps their own named list views; read-only roles may too, since a view changes nobody's data.</summary>
[Collection("Api")]
public sealed class SavedViewTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task People_keep_their_own_views_and_saving_a_name_again_replaces_it()
    {
        var company = Guid.CreateVersion7();
        var viewer = factory.CreateUserClient("viewer.views@logistico.test", company, StaffRoles.Viewer, userId: "user-viewer");
        var colleague = factory.CreateUserClient("ops.views@logistico.test", company, StaffRoles.Operations, userId: "user-ops");

        var saved = await SaveAsync(viewer, "البصرة", "?governorate=IQ-BA&cols=Status,Courier,Cod");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var first = (await saved.Content.ReadFromJsonAsync<SavedViewItem>())!;

        // The same name (any letter case) replaces the view instead of adding another.
        var again = (await (await SaveAsync(viewer, "البصرة", "?governorate=IQ-BA&status=OutForDelivery")).Content.ReadFromJsonAsync<SavedViewItem>())!;
        Assert.Equal(first.Id, again.Id);
        var mine = await ViewsAsync(viewer);
        Assert.Equal("?governorate=IQ-BA&status=OutForDelivery", Assert.Single(mine).Query);

        // A colleague sees none of it and cannot remove it.
        Assert.Empty(await ViewsAsync(colleague));
        Assert.Equal(HttpStatusCode.NotFound, (await colleague.DeleteAsync("/" + ShipmentsApiRoutes.ViewById(first.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await viewer.DeleteAsync("/" + ShipmentsApiRoutes.ViewById(first.Id))).StatusCode);
        Assert.Empty(await ViewsAsync(viewer));
    }

    [Fact]
    public async Task Views_are_checked_and_limited()
    {
        var person = factory.CreateUserClient("many.views@logistico.test", Guid.CreateVersion7(), StaffRoles.Finance, userId: "user-many");
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(person, "bad", "governorate=IQ-BA")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await person.PostAsJsonAsync("/" + ShipmentsApiRoutes.Views, new SaveViewRequest { List = "elsewhere", Name = "x", Query = "" })).StatusCode);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SaveAsync(person, $"view {i}", $"?page={i}")).StatusCode);
        }

        var tooMany = await SaveAsync(person, "one more", "?late=true");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
        Assert.Contains("views.too_many", await tooMany.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string name, string query) =>
        client.PostAsJsonAsync("/" + ShipmentsApiRoutes.Views, new SaveViewRequest { List = SavedViewLists.Shipments, Name = name, Query = query });

    private static async Task<List<SavedViewItem>> ViewsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<SavedViewItem>>("/" + ShipmentsApiRoutes.Views + "?list=" + SavedViewLists.Shipments))!;
}

using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel.Identity;

namespace Logistico.Web.IntegrationTests;

/// <summary>Staff comments on an order: kept in order with who wrote them, readable by the office only.</summary>
[Collection("Api")]
public sealed class CommentTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Staff_leave_comments_on_an_order_for_each_other()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient("ops.desk@logistico.test", company);
        var care = factory.CreateUserClient("care.desk@logistico.test", company, StaffRoles.CustomerSupport);
        var viewer = factory.CreateUserClient("viewer.desk@logistico.test", company, StaffRoles.Viewer);
        var shop = await admin.CreateMerchantAsync("Comment Shop", "comment.shop");
        var order = await admin.BookAsync(ApiTestHelpers.DomesticOrder(shop, "زبون التعليق"));

        Assert.Equal(HttpStatusCode.Created, (await care.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(order.Id), new AddCommentRequest { Text = "  الزبون يطلب التوصيل بعد العصر  " })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(order.Id), new AddCommentRequest { Text = "Noted, on the evening run." })).StatusCode);

        var comments = (await viewer.GetFromJsonAsync<List<ShipmentCommentItem>>("/" + ShipmentsApiRoutes.Comments(order.Id)))!;
        Assert.Equal(["care.desk@logistico.test", "ops.desk@logistico.test"], comments.Select(c => c.Author));
        Assert.Equal("الزبون يطلب التوصيل بعد العصر", comments[0].Text);

        // Read-only staff read but do not write; empty notes, unknown orders and other companies are refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(order.Id), new AddCommentRequest { Text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await care.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(order.Id), new AddCommentRequest { Text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await care.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(Guid.CreateVersion7()), new AddCommentRequest { Text = "x" })).StatusCode);
        var stranger = factory.CreateUserClient(tenant: Guid.CreateVersion7());
        Assert.Empty((await stranger.GetFromJsonAsync<List<ShipmentCommentItem>>("/" + ShipmentsApiRoutes.Comments(order.Id)))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync("/" + ShipmentsApiRoutes.Comments(order.Id), new AddCommentRequest { Text = "x" })).StatusCode);
    }
}

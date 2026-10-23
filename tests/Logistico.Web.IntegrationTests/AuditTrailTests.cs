using System.Net;
using System.Net.Http.Json;
using Logistico.Modules.Audit.Contracts;
using Logistico.Modules.Shipments.Contracts;
using Logistico.Modules.Shipments.Contracts.Rules;
using Logistico.SharedKernel;
using Logistico.SharedKernel.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistico.Web.IntegrationTests;

/// <summary>Who changed what: every saved change to orders, prices and accounts is recorded, and the record cannot be altered.</summary>
[Collection("Api")]
public sealed class AuditTrailTests(LogisticoApiFactory factory)
{
    [Fact]
    public async Task Order_edits_and_price_changes_are_recorded_with_who_and_what()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient("auditor@logistico.test", company);
        var shop = await admin.CreateMerchantAsync("متجر السجل", "audit.trail.shop");
        var order = ApiTestHelpers.DomesticOrder(shop, "زبون السجل");
        order.CodAmount = 40_000;
        var booked = await admin.BookAsync(order);

        var edit = UpdateOrderRequest.From(await admin.GetShipmentAsync(booked.Id));
        edit.CodAmount = 45_000;
        edit.ConsigneeName = "زبون السجل المعدّل";
        Assert.True((await admin.PutWithETagAsync(ShipmentsApiRoutes.ById(booked.Id), edit, await admin.ETagAsync(booked.Id))).IsSuccessStatusCode);

        var history = await TrailAsync(admin, $"?entity=Shipment&entityId={booked.Id}");
        Assert.Equal([AuditActions.Update, AuditActions.Create], history.Items.Select(e => e.Action));
        var change = history.Items[0];
        Assert.Equal(("auditor@logistico.test", booked.TrackingNumber), (change.Actor, change.Label));
        Assert.Contains(new AuditChange("CodAmount", "40000", "45000"), change.Changes);
        Assert.Contains(new AuditChange("ConsigneeName", "زبون السجل", "زبون السجل المعدّل"), change.Changes);
        Assert.DoesNotContain(change.Changes, c => c.Field is "UpdatedAt" or "Version" or "TenantId");

        // The price list.
        var pricing = await admin.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        var card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        card.DefaultFee += 1_000;
        Assert.True((await admin.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!)).IsSuccessStatusCode);
        var price = Assert.Single((await TrailAsync(admin, "?entity=RateCardSettings")).Items);
        Assert.Contains(price.Changes, c => c.Field == "DefaultFee" && c.New == (card.DefaultFee).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(AuditActions.Create, price.Action); // the company's first price list

        // One governorate's price: just that one is recorded (the others come back with PostgreSQL's JSON spacing).
        pricing = await admin.GetAsync("/" + ShipmentsApiRoutes.Pricing);
        card = (await pricing.Content.ReadFromJsonAsync<RateCard>())!;
        var basra = card.GovernorateFees.GetValueOrDefault("IQ-BA", card.DefaultFee);
        card.GovernorateFees["IQ-BA"] = basra + 500;
        Assert.True((await admin.PutWithETagAsync(ShipmentsApiRoutes.Pricing, card, pricing.Headers.ETag!)).IsSuccessStatusCode);
        var regional = (await TrailAsync(admin, "?entity=RateCardSettings")).Items[0];
        Assert.Equal("GovernorateFees.IQ-BA", Assert.Single(regional.Changes).Field);

        // Searching by the person or the label; another company sees none of it.
        Assert.NotEmpty((await TrailAsync(admin, "?q=auditor@")).Items);
        Assert.NotEmpty((await TrailAsync(admin, "?q=" + booked.TrackingNumber)).Items);
        Assert.Empty((await TrailAsync(factory.CreateUserClient(tenant: Guid.CreateVersion7()), "?q=" + booked.TrackingNumber)).Items);
    }

    [Fact]
    public async Task Account_changes_are_recorded_without_secrets()
    {
        var company = Guid.CreateVersion7();
        var admin = factory.CreateUserClient("boss@logistico.test", company);
        var email = $"clerk.{Guid.NewGuid():N}@logistico.test";
        var created = await admin.PostAsJsonAsync("/" + TeamApiRoutes.Team, new CreateTeamMemberRequest { Email = email, DisplayName = "موظف", Role = StaffRoles.Operations, Password = "Basra-Hub-2026!" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = (await created.Content.ReadFromJsonAsync<TeamMember>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/" + TeamApiRoutes.Role(member.Id), new ChangeTeamRoleRequest { Role = StaffRoles.Finance })).StatusCode);

        var trail = (await TrailAsync(admin, "?q=" + Uri.EscapeDataString(email))).Items;
        var account = Assert.Single(trail, e => e.Entity == "ApplicationUser" && e.Action == AuditActions.Create);
        Assert.Contains(new AuditChange("PasswordHash", null, "•••"), account.Changes);
        Assert.DoesNotContain(account.Changes, c => c.New?.StartsWith("AQAAAA", StringComparison.Ordinal) == true); // no password hash

        // The role change: Operations taken away, Finance given, both named.
        Assert.Contains(trail, e => e.Entity == "IdentityUserRole" && e.Action == AuditActions.Delete && e.Label == $"{email} · {StaffRoles.Operations}");
        Assert.Contains(trail, e => e.Entity == "IdentityUserRole" && e.Action == AuditActions.Create && e.Label == $"{email} · {StaffRoles.Finance}");
    }

    [Fact]
    public async Task Only_administrators_read_the_trail_and_nobody_can_alter_it()
    {
        var company = Guid.CreateVersion7();
        foreach (var role in new[] { StaffRoles.Operations, StaffRoles.Finance, StaffRoles.Viewer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateUserClient(tenant: company, role: role).GetAsync("/" + AuditApiRoutes.Audit)).StatusCode);
        }

        var admin = factory.CreateUserClient(tenant: company);
        await admin.CreateMerchantAsync("متجر لا يمحى", "audit.append.only");
        Assert.NotEmpty((await TrailAsync(admin, "?entity=Merchant")).Items);

        await using var connection = new NpgsqlConnection(factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Logistico"));
        await connection.OpenAsync();
        foreach (var sql in new[] { "UPDATE audit.entries SET actor = 'someone else'", "DELETE FROM audit.entries" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("append-only", refused.MessageText, StringComparison.Ordinal);
        }
    }

    private static async Task<PagedResult<AuditEntryItem>> TrailAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<AuditEntryItem>>("/" + AuditApiRoutes.Audit + query))!;
}

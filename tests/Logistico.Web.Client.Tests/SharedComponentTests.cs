using Logistico.Modules.Shipments.Contracts;
using Logistico.SharedKernel.Identity;
using Logistico.Web.Client.Ui;
using Microsoft.AspNetCore.Components;

namespace Logistico.Web.Client.Tests;

/// <summary>The building blocks every page uses, rendered in Arabic (the default) and English.</summary>
public sealed class SharedComponentTests : ClientTestContext
{
    [Theory]
    [InlineData(StaffRoles.Admin, true)]
    [InlineData(StaffRoles.Operations, true)]
    [InlineData(StaffRoles.Viewer, false)]
    [InlineData(StaffRoles.Finance, false)]
    public void Actions_show_only_to_roles_that_may_use_them(string role, bool shown)
    {
        AddAuthorization().SetAuthorized("someone@company.iq").SetRoles(role);

        var allowed = Render<Allowed>(p => p
            .Add(a => a.Policy, StaffPolicies.Operate)
            .AddChildContent("<button id=\"scan\">scan</button>")
            .Add(a => a.Otherwise, (RenderFragment)(b => b.AddMarkupContent(0, "<span id=\"read-only\">read only</span>"))));

        Assert.Equal(shown, allowed.FindAll("#scan").Count == 1);
        Assert.Equal(!shown, allowed.FindAll("#read-only").Count == 1);
    }

    [Fact]
    public void Tables_say_they_are_empty_and_spin_while_loading()
    {
        var loading = Render<DataTable<string>>(p => p
            .Add(t => t.Items, (IReadOnlyList<string>?)null)
            .Add(t => t.Header, (RenderFragment)(b => b.AddMarkupContent(0, "<th>x</th>")))
            .Add(t => t.Row, s => b => b.AddMarkupContent(0, $"<td>{s}</td>")));
        Assert.Single(loading.FindAll(".bp6-spinner"));

        var empty = Render<DataTable<string>>(p => p
            .Add(t => t.Items, [])
            .Add(t => t.EmptyTitle, "لا توجد طلبات")
            .Add(t => t.Header, (RenderFragment)(b => b.AddMarkupContent(0, "<th>x</th>")))
            .Add(t => t.Row, s => b => b.AddMarkupContent(0, $"<td>{s}</td>")));
        Assert.Contains("لا توجد طلبات", empty.Markup, StringComparison.Ordinal);

        var rows = Render<DataTable<string>>(p => p
            .Add(t => t.Items, ["LX1", "LX2"])
            .Add(t => t.Header, (RenderFragment)(b => b.AddMarkupContent(0, "<th>x</th>")))
            .Add(t => t.Row, s => b => b.AddMarkupContent(0, $"<td>{s}</td>")));
        Assert.Equal(2, rows.FindAll("tbody tr").Count);
    }

    [Fact]
    public void The_pager_shows_the_range_and_stops_at_the_ends()
    {
        UseCulture("en-US");
        var pages = new List<int>();
        var pager = Render<Pager>(p => p
            .Add(x => x.Page, 1)
            .Add(x => x.PageSize, 25)
            .Add(x => x.Total, 60)
            .Add(x => x.OnPage, (int page) => pages.Add(page)));

        Assert.Contains("1–25", pager.Find(".lx-pager-info").TextContent, StringComparison.Ordinal);
        var buttons = pager.FindAll(".lx-pager-nav button");
        Assert.True(buttons[0].HasAttribute("disabled"));
        buttons[1].Click();
        Assert.Equal([2], pages);

        var last = Render<Pager>(p => p.Add(x => x.Page, 3).Add(x => x.PageSize, 25).Add(x => x.Total, 60));
        Assert.Contains("51–60", last.Find(".lx-pager-info").TextContent, StringComparison.Ordinal);
        Assert.True(last.FindAll(".lx-pager-nav button")[1].HasAttribute("disabled"));
    }

    [Fact]
    public void Latin_runs_read_left_to_right_inside_Arabic_text()
    {
        var mixed = Render<Mixed>(p => p.Add(m => m.Text, "علي (Baghdad-03) وصل"));

        var ltr = Assert.Single(mixed.FindAll("bdi[dir=ltr]"));
        Assert.Contains("Baghdad-03", ltr.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered, "bp6-intent-success")]
    [InlineData(ShipmentStatus.CustomsHold, "bp6-intent-danger")]
    [InlineData(ShipmentStatus.ReturnAtHub, "bp6-intent-warning")]
    public void Statuses_carry_their_colour_and_name(ShipmentStatus status, string intent)
    {
        var tag = Render<StatusTag>(p => p.Add(s => s.Status, status));

        Assert.Contains(intent, tag.Find(".bp6-tag").ClassName, StringComparison.Ordinal);
        Assert.Equal(ShipmentTexts.Of(status), tag.Find(".bp6-tag").TextContent.Trim());
    }
}

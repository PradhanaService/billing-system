using System.Text.RegularExpressions;
using Bunit;
using WaterBilling.Components;
using WaterBilling.Components.Layout;

namespace WaterBilling.Tests.Design;

// Icons come from Phosphor Icons (phosphoricons.com): every icon name the app uses must exist,
// in both styles, so no icon is ever drawn empty.
public class IconTests : TestContext
{
    private static string SrcFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Components"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src");
    }

    // Every icon name written in the screens and the menu, e.g. Icon Name="edit", IconName="truck", "receipt-in".
    private static IEnumerable<string> NamesUsedInCode()
    {
        var names = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(SrcFolder(), "*.razor", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?:Icon Name|IconName)=""([a-z-]+)""")) names.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Icon Name=""@\((.*?)\)""")) foreach (Match q in Regex.Matches(m.Groups[1].Value, @"""([a-z-]+)""")) names.Add(q.Groups[1].Value);
        }
        foreach (var item in MenuItems.All) names.Add(item.Icon);
        return names;
    }

    [Fact]
    public void UI_10_Every_Icon_Used_Exists_In_Both_Phosphor_Styles()
    {
        var used = NamesUsedInCode().ToList();
        Assert.True(used.Count > 30, $"only {used.Count} names found");
        var missing = used.Where(n => !PhosphorIcons.Regular.ContainsKey(n) || !PhosphorIcons.Duotone.ContainsKey(n)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void UI_11_Icon_Draws_Phosphor_Shapes_And_Duotone_Has_The_Soft_Layer()
    {
        var regular = RenderComponent<Icon>(p => p.Add(i => i.Name, "truck"));
        var duotone = RenderComponent<Icon>(p => p.Add(i => i.Name, "truck").Add(i => i.Duotone, true));

        Assert.Equal("0 0 256 256", regular.Find("svg").GetAttribute("viewBox"));
        Assert.Equal("currentColor", regular.Find("svg").GetAttribute("fill"));
        Assert.NotEmpty(regular.FindAll("path"));
        Assert.DoesNotContain("opacity", regular.Markup);
        Assert.Contains("opacity=\"0.2\"", duotone.Markup);   // the soft fill of the duotone style
    }

    [Fact]
    public void UI_12_Menu_Uses_Money_Icons_For_Receipt_And_Payment()
    {
        Assert.Equal("receipt-in", MenuItems.All.Single(i => i.Title == "Receipt").Icon);
        Assert.Equal("payment-out", MenuItems.All.Single(i => i.Title == "Payment").Icon);
    }
}

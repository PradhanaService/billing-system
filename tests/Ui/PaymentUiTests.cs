using Bunit;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Accounts;

namespace WaterBilling.Tests.Ui;

// Module 8 of the final design: Payment (no Reference No., plain payment numbers).
public class PaymentUiTests : UiTest
{
    private const long FuelExpense = 10;   // seeded ledger
    public PaymentUiTests() : base(UserRole.SalesPerson) { }

    private IRenderedComponent<Payments> Page()
    {
        var page = RenderComponent<Payments>();
        page.WaitForAssertion(() => Assert.Contains("Fuel Expense", page.Markup));
        return page;
    }

    [Fact]
    public void PAY_10_Title_Payment_No_And_No_Reference_Box()
    {
        var page = Page();
        Assert.Equal("Payment", page.Find("h1").TextContent.Trim());
        Assert.Contains("Payment No.", page.Markup);
        Assert.DoesNotContain("Voucher No.", page.Markup);
        Assert.Equal("1", page.Find("#pno").GetAttribute("value"));
        Assert.Empty(page.FindAll("#ref"));
        Assert.DoesNotContain("Reference", page.Markup);
    }

    [Fact]
    public void PAY_11_Bank_Payment_Saves_Without_A_Reference()
    {
        var page = Page();
        page.Find("#ledger").Change(FuelExpense.ToString());
        page.Find("#mode").Change(PaymentMode.Bank.ToString());
        page.Find("#amount").Input("750");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save payment").Click();

        page.WaitForAssertion(() => Assert.Contains("Payment 1 saved.", page.Markup));
        Assert.Equal("2", page.Find("#pno").GetAttribute("value"));
        using var db = Db.NewContext();
        Assert.Equal(75000, db.LedgerEntries.Where(e => e.AccountId == FuelExpense).Sum(e => e.DebitPaise));
    }

    [Fact]
    public void PAY_12_History_Has_No_Reference_Column_And_Plain_Numbers()
    {
        var page = Page();
        page.Find("#ledger").Change(FuelExpense.ToString());
        page.Find("#mode").Change(PaymentMode.Upi.ToString());
        page.Find("#amount").Input("100");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save payment").Click();
        page.WaitForAssertion(() => Assert.Contains("saved", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Contains("Payment History")).Click();
        page.WaitForAssertion(() => Assert.Contains("Fuel Expense", page.Find(".modal tbody").TextContent));
        var heads = page.FindAll(".modal thead th").Select(h => h.TextContent.Trim()).ToList();
        Assert.DoesNotContain("Reference", heads);
        Assert.Contains("Payment", heads);
        var row = page.Find(".modal tbody tr");
        Assert.Equal("1", row.Children[0].TextContent.Trim());
        Assert.DoesNotContain("PV-", row.TextContent);
    }
}

using Bunit;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Accounts;
using WaterBilling.Services.Billing;

namespace WaterBilling.Tests.Ui;

// Module 7 of the final design: Receipt with the pending-invoices multi-select.
public class ReceiptUiTests : UiTest
{
    private long _supplier, _vehicle, _ravi;

    public ReceiptUiTests() : base(UserRole.SalesPerson) { }

    private async Task<long> CreditInvoiceAsync()
    {
        if (_vehicle == 0)
        {
            (_supplier, _vehicle) = await SupplierWithVehicleAsync(rate: 150m);
            _ravi = Db.AddSalesPerson("Ravi");
        }
        return (await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _vehicle, Rate = 150m, PaymentMode = InvoicePaymentMode.Credit, SalesPersonId = _ravi })).Value;
    }

    private IRenderedComponent<Receipts> PickSupplier()
    {
        var page = RenderComponent<Receipts>();
        page.WaitForAssertion(() => Assert.Contains("Kaveri Tankers", page.Markup));
        page.Find("#rsup").Change(_supplier.ToString());
        return page;
    }

    [Fact]
    public async Task RCP_10_Screen_Has_No_Reference_Box_And_Plain_Receipt_Number()
    {
        await CreditInvoiceAsync();
        var page = RenderComponent<Receipts>();
        page.WaitForAssertion(() => Assert.Equal("1", page.Find("#rno").GetAttribute("value")));
        Assert.DoesNotContain("Reference No", page.Markup);
        Assert.Equal(3, page.FindAll("#mode option").Count);
    }

    [Fact]
    public async Task RCP_11_Ticking_Invoices_Fills_The_Amount_With_Their_Total()
    {
        await CreditInvoiceAsync();
        await CreditInvoiceAsync();
        var page = PickSupplier();

        page.WaitForAssertion(() => Assert.Contains("0 of 2 selected", page.Markup));
        Assert.False(page.Find("#amount").HasAttribute("readonly"));   // filled in from the ticks, and can be changed
        page.Find(".ms-box").Click();
        page.Find("input[aria-label='Invoice 1']").Change(true);
        Assert.Equal("150.00", page.Find("#amount").GetAttribute("value"));
        page.Find(".ms-all input").Change(true);
        Assert.Equal("300.00", page.Find("#amount").GetAttribute("value"));
        Assert.Contains("2 of 2 selected", page.Markup);
    }

    [Fact]
    public async Task RCP_12_Saving_Pays_Exactly_The_Chosen_Invoice()
    {
        var first = await CreditInvoiceAsync();
        var second = await CreditInvoiceAsync();
        var page = PickSupplier();
        page.WaitForAssertion(() => page.Find(".ms-box"));
        page.Find(".ms-box").Click();
        page.Find("input[aria-label='Invoice 2']").Change(true);   // the newer one, not oldest-first
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").Click();

        page.WaitForAssertion(() => Assert.Contains("Receipt 1 saved for ₹150.00", page.Markup));
        using var db = Db.NewContext();
        Assert.Equal(0, db.Invoices.Single(i => i.Id == first).AllocatedPaise);
        Assert.Equal(15000, db.Invoices.Single(i => i.Id == second).AllocatedPaise);
        page.WaitForAssertion(() => Assert.Contains("0 of 1 selected", page.Markup));   // list refreshed
    }

    [Fact]
    public async Task RCP_13_Supplier_With_Nothing_Pending_Can_Pay_An_Advance()
    {
        (_supplier, _vehicle) = await SupplierWithVehicleAsync();
        var page = PickSupplier();
        page.WaitForAssertion(() => Assert.Contains("No pending invoices", page.Markup));
        Assert.False(page.Find("#amount").HasAttribute("readonly"));
        page.Find("#amount").Input("500");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").Click();

        page.WaitForAssertion(() => Assert.Contains("saved for ₹500.00", page.Markup));
    }

    [Fact]
    public async Task RCP_14_History_Shows_Invoices_Paid_And_Cancel_Needs_A_Reason()
    {
        await CreditInvoiceAsync();
        var page = PickSupplier();
        page.WaitForAssertion(() => page.Find(".ms-box"));
        page.Find(".ms-box").Click();
        page.Find("input[aria-label='Invoice 1']").Change(true);
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").Click();
        page.WaitForAssertion(() => Assert.Contains("saved", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Contains("Receipt History")).Click();
        page.WaitForAssertion(() => Assert.Contains("Invoices paid", page.Markup));
        var row = page.Find(".modal tbody tr");
        Assert.Contains("1", row.Children[0].TextContent);
        Assert.DoesNotContain("RV-", row.TextContent);

        row.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Confirm cancel").Click();
        page.WaitForAssertion(() => Assert.Contains("reason", page.Find(".modal").TextContent, StringComparison.OrdinalIgnoreCase));
        using var db = Db.NewContext();
        Assert.Equal(15000, db.Invoices.Single().AllocatedPaise);   // still paid: nothing cancelled
    }

    [Fact]
    public async Task ADV_10_Paying_More_Than_The_Ticked_Invoices_Shows_The_Advance_And_It_Pays_The_Next_Trip()
    {
        await CreditInvoiceAsync();
        await CreditInvoiceAsync();                                        // two trips, ₹300 owed
        var page = PickSupplier();
        page.WaitForAssertion(() => page.Find(".ms-box"));
        page.Find(".ms-box").Click();
        page.Find(".ms-all input").Change(true);
        Assert.Equal("300.00", page.Find("#amount").GetAttribute("value"));

        page.Find("#amount").Input("500");
        Assert.Contains("₹300.00 pays the 2 selected invoices · ₹200.00 kept as advance", page.Find("#split").TextContent);
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").Click();

        page.WaitForAssertion(() => Assert.Contains("₹200.00 is kept as advance and pays the next invoices automatically", page.Markup));
        page.WaitForAssertion(() => Assert.Contains("Advance with us: ₹200.00", page.Find("#advnote").TextContent));

        var next = await CreditInvoiceAsync();                             // the next trip is paid from the advance
        using var db = Db.NewContext();
        Assert.Equal(InvoiceStatus.Paid, db.Invoices.Single(i => i.Id == next).Status);
    }

    [Fact]
    public async Task ADV_11_Paying_Less_Than_The_Ticked_Invoices_Is_Not_Allowed()
    {
        await CreditInvoiceAsync();
        var page = PickSupplier();
        page.WaitForAssertion(() => page.Find(".ms-box"));
        page.Find(".ms-box").Click();
        page.Find("input[aria-label='Invoice 1']").Change(true);
        page.Find("#amount").Input("100");

        Assert.Contains("Part payments are not accepted: receive at least ₹150.00", page.Find("#split").TextContent);
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").HasAttribute("disabled"));
        page.Find("#amount").Input("150");
        Assert.False(page.FindAll("button").Single(b => b.TextContent.Trim() == "Save receipt").HasAttribute("disabled"));
    }
}

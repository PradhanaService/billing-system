using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Microsoft.AspNetCore.Components;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Billing;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;

namespace WaterBilling.Tests.Ui;

// Module 6 of the final design: Invoice Display and the Edit flow, on the Sales login.
public class InvoiceDisplayUiTests : UiTest
{
    private long _vehicle, _supplier, _ravi;
    private static readonly DateOnly Day = new(2026, 11, 5);

    public InvoiceDisplayUiTests() : base(UserRole.SalesPerson) { }

    private async Task<long> InvoiceAsync(InvoicePaymentMode mode, string inTime)
    {
        if (_vehicle == 0)
        {
            (_supplier, _vehicle) = await SupplierWithVehicleAsync();
            _ravi = Db.AddSalesPerson("Ravi");
        }
        return (await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _vehicle, Rate = 129.68m, PaymentMode = mode, SalesPersonId = _ravi,
                                                               InDate = Day, InTime = inTime })).Value;
    }

    [Fact]
    public async Task ID_10_List_Shows_Date_And_Time_Vehicle_And_Status_With_Edit_View_Print()
    {
        await InvoiceAsync(InvoicePaymentMode.Cash, "11:40 AM");
        await InvoiceAsync(InvoicePaymentMode.Credit, "10:20 AM");

        var page = RenderComponent<InvoiceDisplay>();

        page.WaitForAssertion(() => Assert.Contains("11:40 AM", page.Markup));
        Assert.Contains("Paid · cash at billing", page.Markup);
        Assert.Contains("Not paid", page.Markup);
        Assert.Equal(2, page.FindAll("button").Count(b => b.TextContent.Trim() == "Edit"));
        Assert.Equal(2, page.FindAll("a[title=Print]").Count);
        Assert.Contains("Time from", page.Markup);
        Assert.DoesNotContain("Receipts</div>", page.Markup);   // receipts and payments are not listed here
    }

    [Fact]
    public async Task ID_11_Time_Filter_Keeps_Only_Trips_In_The_Range()
    {
        await InvoiceAsync(InvoicePaymentMode.Cash, "08:15 AM");
        await InvoiceAsync(InvoicePaymentMode.Cash, "02:10 PM");
        var page = RenderComponent<InvoiceDisplay>();
        page.WaitForAssertion(() => Assert.Contains("02:10 PM", page.Markup));

        page.Find("#itf").Change("09:00 AM");
        page.Find("#itt").Change("06:00 PM");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

        page.WaitForAssertion(() => Assert.DoesNotContain("08:15 AM", page.Markup));
        Assert.Contains("02:10 PM", page.Markup);
    }

    [Fact]
    public async Task ID_12_Bad_Time_In_The_Filter_Is_Explained()
    {
        await InvoiceAsync(InvoicePaymentMode.Cash, "08:15 AM");
        var page = RenderComponent<InvoiceDisplay>();

        page.Find("#itf").Change("nine");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

        page.WaitForAssertion(() => Assert.Contains("Type the times like 09:00 AM", page.Markup));
    }

    [Fact]
    public async Task ID_13_Tapping_Paid_Shows_The_Receipt()
    {
        await InvoiceAsync(InvoicePaymentMode.Credit, "10:20 AM");
        await S.Receipts.CreateAsync(new ReceiptForm { PartyId = _supplier, Amount = 129.68m });
        var page = RenderComponent<InvoiceDisplay>();
        page.WaitForAssertion(() => Assert.Contains("Paid · Receipt", page.Markup));

        page.Find("button.pill-ok").Click();

        Assert.Contains("Paid by receipt", page.Markup);
        Assert.Contains("Paid to this invoice", page.Markup);
        Assert.Contains("₹129.68", page.Find(".popover").TextContent);
    }

    [Fact]
    public async Task ID_14_Edit_Asks_Which_Salesperson_Then_Opens_The_Edit_Screen()
    {
        var id = await InvoiceAsync(InvoicePaymentMode.Credit, "10:20 AM");
        var page = RenderComponent<InvoiceDisplay>();
        page.WaitForAssertion(() => Assert.Contains("Edit", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Trim() == "Edit").Click();
        Assert.Contains("Which salesperson is editing?", page.Markup);
        Assert.True(page.FindAll("button").First(b => b.TextContent == "Continue").HasAttribute("disabled"));
        page.Find("#ewho").Change(_ravi.ToString());
        page.FindAll("button").First(b => b.TextContent == "Continue").Click();

        Assert.EndsWith($"/invoices/{id}/edit?by={_ravi}", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task ID_15_Edit_Screen_Changes_Only_Capacity_Rate_And_Times_And_Issues_A_Copy()
    {
        var id = await InvoiceAsync(InvoicePaymentMode.Credit, "10:05 AM");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/invoices/{id}/edit?by={_ravi}");
        var page = RenderComponent<InvoiceEdit>(p => p.Add(x => x.Id, id));

        page.WaitForAssertion(() => Assert.Contains("Editing as <b>Ravi</b>", page.Markup));
        foreach (var fixedBox in page.FindAll(".edit-sec:not(#emeters) input").Where(i => !i.ClassList.Contains("time-input") && i.Id is not ("ecap" or "erate" or "eindate" or "eoutdate")))
            Assert.True(fixedBox.HasAttribute("readonly"), $"{fixedBox.OuterHtml} should be read-only");
        page.Find("#erate").Input("150");
        page.Find("#eouttime").Change("10:30 AM");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        page.WaitForAssertion(() => Assert.Contains("corrected copy is invoice", page.Markup));
        Assert.Contains("₹150.00", page.Markup);
        using var db = Db.NewContext();
        Assert.Equal(InvoiceStatus.Cancelled, db.Invoices.Single(i => i.Id == id).Status);
        Assert.Equal(id, db.Invoices.Single(i => i.Id != id).ReplacesInvoiceId);
    }

    [Fact]
    public async Task ID_16_Edit_Screen_Opened_Directly_Asks_Who_Is_Editing()
    {
        var id = await InvoiceAsync(InvoicePaymentMode.Credit, "10:05 AM");

        var page = RenderComponent<InvoiceEdit>(p => p.Add(x => x.Id, id));

        page.WaitForAssertion(() => Assert.Contains("Which salesperson is editing?", page.Markup));
        Assert.True(page.FindAll("button").First(b => b.TextContent.Trim() == "Save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task MTR_13_Edit_Screen_Changes_Meter_End_Readings_On_The_Same_Invoice_With_Start_Locked()
    {
        if (_vehicle == 0) { (_supplier, _vehicle) = await SupplierWithVehicleAsync(); _ravi = Db.AddSalesPerson("Ravi"); }
        var first = (await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _vehicle, Rate = 129.68m, SalesPersonId = _ravi, Meter1Start = 100m, Meter1End = 110m })).Value;
        var second = (await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _vehicle, Rate = 129.68m, SalesPersonId = _ravi, Meter1End = 120m })).Value;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/invoices/{first}/edit?by={_ravi}");
        var page = RenderComponent<InvoiceEdit>(p => p.Add(x => x.Id, first));

        page.WaitForAssertion(() => Assert.Equal("110.0", page.Find("#emeters input[aria-label='Meter 1 end']").GetAttribute("value")));
        Assert.True(page.Find("#emeters input[aria-label='Meter 1 start']").HasAttribute("readonly"));
        Assert.False(page.Find("#emeters input[aria-label='Meter 1 end']").HasAttribute("readonly"));
        Assert.Empty(page.FindAll("#ereplaced"));                                  // the meter-replaced option is Admin only

        page.Find("#emeters input[aria-label='Meter 1 end']").Change("108.5");
        page.Find("#emreason").Input("Misread the dial");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        page.WaitForAssertion(() => Assert.Contains("Meter readings of invoice 1 saved. Meter readings also updated on: invoice 2, Meter 1 start: 110.0 → 108.5", page.Markup));
        using var db = Db.NewContext();
        Assert.Equal(2, db.Invoices.Count());                                       // no corrected copy for a meter-only change
        Assert.Equal(1085L, db.Invoices.Single(i => i.Id == second).Meter1StartTenths);
    }
}

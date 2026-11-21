using Bunit;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Billing;

namespace WaterBilling.Tests.Ui;

// Module 5 of the final design: the Sales Invoice screen.
public class SalesInvoiceUiTests : UiTest
{
    [Fact]
    public async Task SI_10_Typing_The_Vehicle_Code_Fills_Name_Supplier_Capacity_Rate_And_Amount()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();

        page.Find("#vcode").Change("a2");   // any letter case

        page.WaitForAssertion(() => Assert.Contains("Found in Vehicle Master", page.Markup));
        Assert.Contains("value=\"Kaveri Tankers\"", page.Markup);
        Assert.Contains("value=\"24,000\"", page.Markup);
        Assert.Contains("value=\"129.68\"", page.Markup);
        Assert.Equal("129.68", page.Find("input[aria-label=Amount]").GetAttribute("value"));
        Assert.DoesNotContain("Trip*", page.Markup);   // Capacity replaces Trip
    }

    [Fact]
    public async Task SI_11_Unknown_Code_Says_So_And_Save_Stays_Off()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();

        page.Find("#vcode").Change("ZZ9");

        Assert.Contains("No active vehicle with code ZZ9", page.Markup);
        Assert.True(page.FindAll("button").First(b => b.TextContent.Trim() == "Save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SI_12_Save_Stores_The_Invoice_With_Its_Trip_Times()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();
        page.Find("#vcode").Change("A2");
        page.Find("#intime").Change("11:05 AM");
        page.Find("#outtime").Change("11:15 AM");

        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();

        page.WaitForAssertion(() => Assert.Contains("saved for ₹129.68", page.Markup));
        using var db = Db.NewContext();
        var inv = db.Invoices.Single();
        Assert.NotNull(inv.InTimeUtc);
        Assert.Equal((InvoicePaymentMode.Credit, 0L), (inv.PaymentMode, inv.AllocatedPaise));   // saved as Not paid
        Assert.Contains("Not paid until a receipt is recorded", page.Markup);
        Assert.Equal(24000, inv.CapacityLitres);
    }

    [Fact]
    public async Task SI_21_New_Invoice_Starts_Each_Meter_From_The_Previous_End_Reading()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();
        Assert.Equal("", page.Find("input[aria-label='Meter 1 start']").GetAttribute("value") ?? "");
        page.Find("#vcode").Change("A2");
        page.Find("input[aria-label='Meter 1 start']").Change("28451.2");
        page.Find("input[aria-label='Meter 1 end']").Change("28463.2");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();
        page.WaitForAssertion(() => Assert.Contains("saved", page.Markup));

        // The next invoice: Meter 1 starts at 28463.2 by itself; meters 2 and 3 stay empty.
        page.WaitForAssertion(() => Assert.Equal("28463.2", page.Find("input[aria-label='Meter 1 start']").GetAttribute("value")));
        Assert.Equal("", page.Find("input[aria-label='Meter 2 start']").GetAttribute("value") ?? "");
        Assert.Contains("Meter 1: invoice 1", page.Find("#meternote").TextContent);

        // A trip that does not use meter 1 still saves (its carried-on start is not sent alone).
        page.Find("#vcode").Change("A2");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();
        page.WaitForAssertion(() => Assert.Contains("Invoice 2 saved", page.Markup));
        Assert.Equal("28463.2", page.Find("input[aria-label='Meter 1 start']").GetAttribute("value"));

        // Using it again: type only the end reading.
        page.Find("#vcode").Change("A2");
        page.Find("input[aria-label='Meter 1 end']").Change("28475.0");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();
        page.WaitForAssertion(() => Assert.Contains("Invoice 3 saved", page.Markup));
        using var db = Db.NewContext();
        var third = db.Invoices.Single(i => i.DisplayNo == "3");
        Assert.Equal((284632L, 284750L), (third.Meter1StartTenths!.Value, third.Meter1EndTenths!.Value));
        Assert.Null(db.Invoices.Single(i => i.DisplayNo == "2").Meter1StartTenths);
    }

    [Fact]
    public async Task SI_16_No_Payment_Choice_On_A_New_Invoice()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();
        Assert.Empty(page.FindAll("#mode"));
        Assert.DoesNotContain("Payment Mode", page.Markup);
        Assert.Empty(page.FindAll("#paystatus"));
    }

    [Fact]
    public async Task SI_17_Saved_Invoice_Shows_Not_Paid_Then_Paid_After_A_Receipt()
    {
        var (supplier, _) = await SupplierWithVehicleAsync();
        var page = RenderComponent<SalesInvoice>();
        page.Find("#vcode").Change("A2");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click();
        page.WaitForAssertion(() => Assert.Contains("saved", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Contains("Previous")).Click();
        page.WaitForAssertion(() => Assert.Equal("Not paid", page.Find("#paystatus").TextContent.Trim()));

        var id = Db.NewContext().Invoices.Single().Id;
        Assert.True((await S.Receipts.CreateAsync(new WaterBilling.Services.Accounts.ReceiptForm { PartyId = supplier, Amount = 129.68m, InvoiceIds = [id] })).Ok);
        page.FindAll("button").First(b => b.TextContent.Contains("New invoice")).Click();
        page.FindAll("button").First(b => b.TextContent.Contains("Previous")).Click();
        page.WaitForAssertion(() => Assert.Equal("Paid", page.Find("#paystatus").TextContent.Trim()));
        Assert.Contains("pill-ok", page.Find("#paystatus").ClassName);
    }

    [Fact]
    public async Task SI_13_Previous_Shows_The_Last_Invoice_Read_Only_With_View_And_Edit()
    {
        await SupplierWithVehicleAsync();
        var id = (await S.Invoices.CreateAsync(new() { VehicleId = Db.NewContext().Vehicles.Single().Id, Rate = 129.68m })).Value;
        var no = (await S.Invoices.GetAsync(id))!.DisplayNo;
        var page = RenderComponent<SalesInvoice>();

        page.FindAll("button").First(b => b.TextContent.Contains("Previous")).Click();

        page.WaitForAssertion(() => Assert.Contains($"Showing saved invoice <b>{no}</b>", page.Markup));
        Assert.Contains($"href=\"/invoices/{id}\"", page.Markup);
        Assert.Contains($"href=\"/invoices/{id}/edit\"", page.Markup);
        Assert.True(page.Find("#vcode").HasAttribute("readonly"));
        Assert.DoesNotContain(">Save<", page.Markup.Replace(" ", ""));
    }

    [Fact]
    public async Task SI_14_Search_Opens_An_Invoice_And_New_Invoice_Goes_Back()
    {
        await SupplierWithVehicleAsync();
        var vehicle = Db.NewContext().Vehicles.Single().Id;
        var first = (await S.Invoices.CreateAsync(new() { VehicleId = vehicle, Rate = 129.68m })).Value;
        await S.Invoices.CreateAsync(new() { VehicleId = vehicle, Rate = 129.68m });
        var no = (await S.Invoices.GetAsync(first))!.DisplayNo;
        var page = RenderComponent<SalesInvoice>();

        page.Find("input[aria-label='Find invoice']").Change(no);
        page.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();
        page.WaitForAssertion(() => Assert.Contains($"Showing saved invoice <b>{no}</b>", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Contains("New invoice")).Click();
        page.WaitForAssertion(() => Assert.DoesNotContain("Showing saved invoice", page.Markup));
        Assert.False(page.Find("#vcode").HasAttribute("readonly"));
    }

    [Fact]
    public void SI_15_Search_For_A_Missing_Number_Says_So()
    {
        var page = RenderComponent<SalesInvoice>();

        page.Find("input[aria-label='Find invoice']").Change("99999");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Search").Click();

        page.WaitForAssertion(() => Assert.Contains("No invoice numbered 99999", page.Markup));
    }
}

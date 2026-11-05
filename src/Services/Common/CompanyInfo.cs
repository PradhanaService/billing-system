namespace WaterBilling.Services.Common;

// Printed on invoices and reports. Set in appsettings.json under "Company".
public sealed class CompanyInfo
{
    public string Name { get; set; } = "Shree Karunambika Traders";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
}

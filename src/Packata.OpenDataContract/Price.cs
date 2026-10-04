using Packata.Core;

namespace Packata.OpenDataContract;

public class Price
{
    [Label("Price Amount")]
    public decimal? PriceAmount { get; set; }

    [Label("Price Currency")]
    public string? PriceCurrency { get; set; }

    [Label("Price Unit")]
    public string? PriceUnit { get; set; }
}

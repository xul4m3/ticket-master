namespace TicketMaster.Api.Contracts;

public sealed class AreaRequest
{
    public string AreaId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int RowCount { get; set; }
    public int ColCount { get; set; }
}

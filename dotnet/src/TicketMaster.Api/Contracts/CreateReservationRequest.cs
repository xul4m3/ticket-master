namespace TicketMaster.Api.Contracts;

public sealed class CreateReservationRequest
{
    public string UserId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string AreaId { get; set; } = string.Empty;
    public int NumOfSeats { get; set; }
    public List<SeatRequest>? Seats { get; set; }
    public string Type { get; set; } = string.Empty;
}

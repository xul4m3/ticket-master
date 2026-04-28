namespace TicketMaster.Api.Contracts;

public sealed class ReservationResponse
{
    public string ReservationId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string EventId { get; init; } = string.Empty;
    public string AreaId { get; init; } = string.Empty;
    public int NumOfSeats { get; init; }
    public int NumOfSeat { get; init; }
    public string Type { get; init; } = string.Empty;
    public List<SeatRequest> Seats { get; init; } = [];
    public string State { get; init; } = string.Empty;
    public string FailedReason { get; init; } = string.Empty;
}

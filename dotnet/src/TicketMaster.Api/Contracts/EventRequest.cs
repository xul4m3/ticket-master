namespace TicketMaster.Api.Contracts;

public sealed class EventRequest
{
    public string EventName { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public List<AreaRequest> Areas { get; set; } = [];
    public DateTimeOffset ReservationOpeningTime { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReservationClosingTime { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EventStartTime { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EventEndTime { get; set; } = DateTimeOffset.UtcNow;
}

using TicketMaster.Api.Contracts;

namespace TicketMaster.Api.Domain;

internal sealed class EventState
{
    public EventState(EventRequest request)
    {
        EventName = request.EventName;
        Artist = request.Artist;
        ReservationOpeningTime = request.ReservationOpeningTime;
        ReservationClosingTime = request.ReservationClosingTime;
        EventStartTime = request.EventStartTime;
        EventEndTime = request.EventEndTime;
        Areas = request.Areas.ToDictionary(area => area.AreaId, area => new AreaState(area), StringComparer.Ordinal);
    }

    public string EventName { get; }
    public string Artist { get; }
    public DateTimeOffset ReservationOpeningTime { get; }
    public DateTimeOffset ReservationClosingTime { get; }
    public DateTimeOffset EventStartTime { get; }
    public DateTimeOffset EventEndTime { get; }
    public Dictionary<string, AreaState> Areas { get; }
}

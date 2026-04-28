using System.Collections.Concurrent;
using TicketMaster.Api.Contracts;
using TicketMaster.Api.Domain;

namespace TicketMaster.Api.Services;

public sealed class TicketMasterStore
{
    private const string RandomType = "RANDOM";
    private const string SelfPickType = "SELF_PICK";
    private const string ReservedState = "RESERVED";
    private const string FailedState = "FAILED";

    private readonly ConcurrentDictionary<string, EventState> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ReservationResponse> _reservations = new(StringComparer.Ordinal);

    public EventRequest UpsertEvent(EventRequest request)
    {
        ValidateEvent(request);
        _events[request.EventName] = new EventState(request);
        return request;
    }

    public string CreateReservation(string routeEventId, CreateReservationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeEventId);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.EventId) &&
            !string.Equals(request.EventId, routeEventId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Route event id must match request body eventId.");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("userId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AreaId))
        {
            throw new ArgumentException("areaId is required.");
        }

        var eventId = string.IsNullOrWhiteSpace(request.EventId) ? routeEventId : request.EventId;
        var reservationId = Guid.NewGuid().ToString();

        if (!_events.TryGetValue(eventId, out var eventState))
        {
            return StoreFailure(reservationId, request, eventId, "Event does not exist.");
        }

        if (!eventState.Areas.TryGetValue(request.AreaId, out var areaState))
        {
            return StoreFailure(reservationId, request, eventId, "Area does not exist.");
        }

        if (request.NumOfSeats <= 0)
        {
            return StoreFailure(reservationId, request, eventId, "numOfSeats must be greater than zero.");
        }

        ReservationResponse reservation;
        lock (areaState.SyncRoot)
        {
            reservation = BuildReservation(reservationId, request, eventId, areaState);
        }

        _reservations[reservationId] = reservation;
        return reservationId;
    }

    public bool TryGetReservation(string reservationId, out ReservationResponse? reservation) =>
        _reservations.TryGetValue(reservationId, out reservation);

    private static ReservationResponse BuildReservation(string reservationId, CreateReservationRequest request, string eventId, AreaState areaState)
    {
        var normalizedType = request.Type.Trim().ToUpperInvariant();
        var requestedSeats = request.Seats ?? [];
        var numOfSeat = 0;
        List<SeatRequest> reservedSeats;
        string failureReason;
        var success = normalizedType switch
        {
            RandomType => areaState.TryReserveFirstAvailable(request.NumOfSeats, out reservedSeats, out failureReason),
            SelfPickType when requestedSeats.Count == request.NumOfSeats =>
                areaState.TryReserveSpecific(requestedSeats, out reservedSeats, out failureReason),
            SelfPickType => FailSelfPick(out reservedSeats, out failureReason),
            _ => FailInvalidType(out reservedSeats, out failureReason)
        };

        if (success)
        {
            numOfSeat = reservedSeats.Count;
        }

        return new ReservationResponse
        {
            ReservationId = reservationId,
            UserId = request.UserId,
            EventId = eventId,
            AreaId = request.AreaId,
            NumOfSeats = request.NumOfSeats,
            NumOfSeat = numOfSeat,
            Type = normalizedType,
            Seats = reservedSeats,
            State = success ? ReservedState : FailedState,
            FailedReason = success ? string.Empty : failureReason
        };
    }

    private static bool FailSelfPick(out List<SeatRequest> reservedSeats, out string failureReason)
    {
        reservedSeats = [];
        failureReason = "SELF_PICK reservations require the requested seat list to match numOfSeats.";
        return false;
    }

    private static bool FailInvalidType(out List<SeatRequest> reservedSeats, out string failureReason)
    {
        reservedSeats = [];
        failureReason = "Reservation type must be RANDOM or SELF_PICK.";
        return false;
    }

    private string StoreFailure(string reservationId, CreateReservationRequest request, string eventId, string reason)
    {
        _reservations[reservationId] = new ReservationResponse
        {
            ReservationId = reservationId,
            UserId = request.UserId,
            EventId = eventId,
            AreaId = request.AreaId,
            NumOfSeats = request.NumOfSeats,
            NumOfSeat = 0,
            Type = request.Type.Trim().ToUpperInvariant(),
            Seats = [],
            State = FailedState,
            FailedReason = reason
        };

        return reservationId;
    }

    private static void ValidateEvent(EventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.EventName))
        {
            throw new ArgumentException("eventName is required.");
        }

        if (request.Areas.Count == 0)
        {
            throw new ArgumentException("At least one area is required.");
        }

        var seenAreaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in request.Areas)
        {
            if (string.IsNullOrWhiteSpace(area.AreaId))
            {
                throw new ArgumentException("areaId is required.");
            }

            if (!seenAreaIds.Add(area.AreaId))
            {
                throw new ArgumentException($"Duplicate areaId '{area.AreaId}' is not allowed.");
            }

            if (area.RowCount <= 0 || area.ColCount <= 0)
            {
                throw new ArgumentException($"Area '{area.AreaId}' must have positive rowCount and colCount.");
            }
        }
    }
}

using System.Text.Json;
using Npgsql;
using StackExchange.Redis;
using TicketMaster.Api.Contracts;
using TicketMaster.Api.Domain;

namespace TicketMaster.Api.Services;

public sealed class TicketMasterStore
{
    private const string RandomType = "RANDOM";
    private const string SelfPickType = "SELF_PICK";
    private const string ReservedState = "RESERVED";
    private const string FailedState = "FAILED";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly IDatabase _redis;

    public TicketMasterStore(NpgsqlDataSource dataSource, IConnectionMultiplexer connectionMultiplexer)
    {
        _dataSource = dataSource;
        _redis = connectionMultiplexer.GetDatabase();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
                           CREATE TABLE IF NOT EXISTS events (
                               event_id TEXT PRIMARY KEY,
                               payload JSONB NOT NULL
                           );

                           CREATE TABLE IF NOT EXISTS reservations (
                               reservation_id TEXT PRIMARY KEY,
                               event_id TEXT NOT NULL,
                               area_id TEXT NOT NULL,
                               state TEXT NOT NULL,
                               payload JSONB NOT NULL
                           );

                           CREATE INDEX IF NOT EXISTS ix_reservations_event_area_state
                               ON reservations (event_id, area_id, state);
                           """;

        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EventRequest> UpsertEventAsync(EventRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEvent(request);

        const string sql = """
                           INSERT INTO events (event_id, payload)
                           VALUES (@event_id, @payload::jsonb)
                           ON CONFLICT (event_id)
                           DO UPDATE SET payload = EXCLUDED.payload;
                           """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("event_id", request.EventName);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(request, JsonSerializerOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var area in request.Areas)
        {
            await _redis.KeyDeleteAsync(GetAvailabilityKey(request.EventName, area.AreaId));
        }

        return request;
    }

    public async Task<string> CreateReservationAsync(string routeEventId, CreateReservationRequest request, CancellationToken cancellationToken = default)
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
        var normalizedType = NormalizeType(request.Type);
        var eventState = await LoadEventAsync(eventId, cancellationToken);

        if (eventState is null)
        {
            return await StoreFailureAsync(reservationId, request, eventId, normalizedType, "Event does not exist.", cancellationToken);
        }

        if (!eventState.Areas.TryGetValue(request.AreaId, out var areaState))
        {
            return await StoreFailureAsync(reservationId, request, eventId, normalizedType, "Area does not exist.", cancellationToken);
        }

        if (request.NumOfSeats <= 0)
        {
            return await StoreFailureAsync(reservationId, request, eventId, normalizedType, "numOfSeats must be greater than zero.", cancellationToken);
        }

        var lockKey = GetLockKey(eventId, request.AreaId);
        await using var reservationLock = await RedisReservationLock.AcquireAsync(_redis, lockKey, cancellationToken);
        var availability = await LoadAvailabilityAsync(eventId, areaState, cancellationToken);
        var requestedSeats = request.Seats ?? [];

        List<SeatRequest> reservedSeats;
        string failureReason;
        var success = normalizedType switch
        {
            RandomType => areaState.TryReserveFirstAvailable(request.NumOfSeats, availability.ReservedSeatKeys, out reservedSeats, out failureReason),
            SelfPickType when requestedSeats.Count == request.NumOfSeats =>
                areaState.TryReserveSpecific(availability.ReservedSeatKeys, requestedSeats, out reservedSeats, out failureReason),
            SelfPickType => FailSelfPick(out reservedSeats, out failureReason),
            _ => FailInvalidType(out reservedSeats, out failureReason)
        };

        if (success)
        {
            await SaveAvailabilityAsync(eventId, areaState.AreaId, availability, cancellationToken);
        }

        var reservation = new ReservationResponse
        {
            ReservationId = reservationId,
            UserId = request.UserId,
            EventId = eventId,
            AreaId = request.AreaId,
            NumOfSeats = request.NumOfSeats,
            NumOfSeat = success ? reservedSeats.Count : 0,
            Type = normalizedType,
            Seats = reservedSeats,
            State = success ? ReservedState : FailedState,
            FailedReason = success ? string.Empty : failureReason
        };

        await PersistReservationAsync(reservation, cancellationToken);
        return reservationId;
    }

    public async Task<ReservationResponse?> GetReservationAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT payload
                           FROM reservations
                           WHERE reservation_id = @reservation_id;
                           """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("reservation_id", reservationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return Deserialize<ReservationResponse>(reader.GetString(0));
    }

    private async Task<EventState?> LoadEventAsync(string eventId, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT payload
                           FROM events
                           WHERE event_id = @event_id;
                           """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("event_id", eventId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var request = Deserialize<EventRequest>(reader.GetString(0));
        return new EventState(request);
    }

    private async Task<AreaAvailabilityState> LoadAvailabilityAsync(string eventId, AreaState areaState, CancellationToken cancellationToken)
    {
        var cacheKey = GetAvailabilityKey(eventId, areaState.AreaId);
        var cachedAvailability = await _redis.StringGetAsync(cacheKey);
        if (cachedAvailability.HasValue)
        {
            return Deserialize<AreaAvailabilityState>(cachedAvailability!);
        }

        const string sql = """
                           SELECT payload
                           FROM reservations
                           WHERE event_id = @event_id
                             AND area_id = @area_id
                             AND state = @state;
                           """;

        var availability = new AreaAvailabilityState();

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("area_id", areaState.AreaId);
        command.Parameters.AddWithValue("state", ReservedState);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reservation = Deserialize<ReservationResponse>(reader.GetString(0));
            foreach (var seat in reservation.Seats)
            {
                availability.ReservedSeatKeys.Add(ToSeatKey(seat.Row, seat.Col));
            }
        }

        await SaveAvailabilityAsync(eventId, areaState.AreaId, availability, cancellationToken);
        return availability;
    }

    private async Task SaveAvailabilityAsync(string eventId, string areaId, AreaAvailabilityState availability, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _redis.StringSetAsync(GetAvailabilityKey(eventId, areaId), JsonSerializer.Serialize(availability, JsonSerializerOptions));
    }

    private async Task PersistReservationAsync(ReservationResponse reservation, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO reservations (reservation_id, event_id, area_id, state, payload)
                           VALUES (@reservation_id, @event_id, @area_id, @state, @payload::jsonb);
                           """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("reservation_id", reservation.ReservationId);
        command.Parameters.AddWithValue("event_id", reservation.EventId);
        command.Parameters.AddWithValue("area_id", reservation.AreaId);
        command.Parameters.AddWithValue("state", reservation.State);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(reservation, JsonSerializerOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string> StoreFailureAsync(string reservationId, CreateReservationRequest request, string eventId, string normalizedType, string reason, CancellationToken cancellationToken)
    {
        var reservation = new ReservationResponse
        {
            ReservationId = reservationId,
            UserId = request.UserId,
            EventId = eventId,
            AreaId = request.AreaId,
            NumOfSeats = request.NumOfSeats,
            NumOfSeat = 0,
            Type = normalizedType,
            Seats = [],
            State = FailedState,
            FailedReason = reason
        };

        await PersistReservationAsync(reservation, cancellationToken);
        return reservationId;
    }

    private static string NormalizeType(string? reservationType) => reservationType?.Trim().ToUpperInvariant() ?? string.Empty;

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

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonSerializerOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name}.");

    private static string GetAvailabilityKey(string eventId, string areaId) => $"event:{eventId}:area:{areaId}:availability";

    private static string GetLockKey(string eventId, string areaId) => $"lock:event:{eventId}:area:{areaId}";

    private static string ToSeatKey(int row, int col) => $"{row}:{col}";

    private sealed class AreaAvailabilityState
    {
        public HashSet<string> ReservedSeatKeys { get; init; } = new(StringComparer.Ordinal);
    }
}

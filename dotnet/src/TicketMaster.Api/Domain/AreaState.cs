using TicketMaster.Api.Contracts;

namespace TicketMaster.Api.Domain;

internal sealed class AreaState
{
    private readonly bool[,] _availableSeats;

    public AreaState(AreaRequest request)
    {
        AreaId = request.AreaId;
        Price = request.Price;
        RowCount = request.RowCount;
        ColCount = request.ColCount;
        _availableSeats = new bool[RowCount, ColCount];

        for (var row = 0; row < RowCount; row++)
        {
            for (var col = 0; col < ColCount; col++)
            {
                _availableSeats[row, col] = true;
            }
        }
    }

    public object SyncRoot { get; } = new();
    public string AreaId { get; }
    public decimal Price { get; }
    public int RowCount { get; }
    public int ColCount { get; }

    public bool TryReserveSpecific(IReadOnlyList<SeatRequest> requestedSeats, out List<SeatRequest> reservedSeats, out string failureReason)
    {
        reservedSeats = [];
        failureReason = string.Empty;

        foreach (var seat in requestedSeats)
        {
            if (!IsInBounds(seat))
            {
                failureReason = $"Seat ({seat.Row}, {seat.Col}) is out of bounds.";
                return false;
            }

            if (!_availableSeats[seat.Row, seat.Col])
            {
                failureReason = $"Seat ({seat.Row}, {seat.Col}) is already reserved.";
                return false;
            }
        }

        reservedSeats = requestedSeats.Select(seat => new SeatRequest { Row = seat.Row, Col = seat.Col }).ToList();
        foreach (var seat in reservedSeats)
        {
            _availableSeats[seat.Row, seat.Col] = false;
        }

        return true;
    }

    public bool TryReserveFirstAvailable(int seatCount, out List<SeatRequest> reservedSeats, out string failureReason)
    {
        reservedSeats = [];
        failureReason = string.Empty;

        for (var row = 0; row < RowCount && reservedSeats.Count < seatCount; row++)
        {
            for (var col = 0; col < ColCount && reservedSeats.Count < seatCount; col++)
            {
                if (!_availableSeats[row, col])
                {
                    continue;
                }

                reservedSeats.Add(new SeatRequest { Row = row, Col = col });
            }
        }

        if (reservedSeats.Count != seatCount)
        {
            failureReason = "Not enough seats are available.";
            return false;
        }

        foreach (var seat in reservedSeats)
        {
            _availableSeats[seat.Row, seat.Col] = false;
        }

        return true;
    }

    private bool IsInBounds(SeatRequest seat) =>
        seat.Row >= 0 &&
        seat.Col >= 0 &&
        seat.Row < RowCount &&
        seat.Col < ColCount;
}

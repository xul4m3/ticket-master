using TicketMaster.Api.Contracts;

namespace TicketMaster.Api.Domain;

internal sealed class AreaState
{
    public AreaState(AreaRequest request)
    {
        AreaId = request.AreaId;
        Price = request.Price;
        RowCount = request.RowCount;
        ColCount = request.ColCount;
    }

    public string AreaId { get; }
    public decimal Price { get; }
    public int RowCount { get; }
    public int ColCount { get; }

    public bool TryReserveSpecific(HashSet<string> reservedSeatKeys, IReadOnlyList<SeatRequest> requestedSeats, out List<SeatRequest> reservedSeats, out string failureReason)
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

            if (reservedSeatKeys.Contains(ToSeatKey(seat.Row, seat.Col)))
            {
                failureReason = $"Seat ({seat.Row}, {seat.Col}) is already reserved.";
                return false;
            }
        }

        reservedSeats = requestedSeats.Select(seat => new SeatRequest { Row = seat.Row, Col = seat.Col }).ToList();
        foreach (var seat in reservedSeats)
        {
            reservedSeatKeys.Add(ToSeatKey(seat.Row, seat.Col));
        }

        return true;
    }

    public bool TryReserveFirstAvailable(int seatCount, HashSet<string> reservedSeatKeys, out List<SeatRequest> reservedSeats, out string failureReason)
    {
        reservedSeats = [];
        failureReason = string.Empty;

        for (var row = 0; row < RowCount && reservedSeats.Count < seatCount; row++)
        {
            for (var col = 0; col < ColCount && reservedSeats.Count < seatCount; col++)
            {
                if (reservedSeatKeys.Contains(ToSeatKey(row, col)))
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
            reservedSeatKeys.Add(ToSeatKey(seat.Row, seat.Col));
        }

        return true;
    }

    private bool IsInBounds(SeatRequest seat) =>
        seat.Row >= 0 &&
        seat.Col >= 0 &&
        seat.Row < RowCount &&
        seat.Col < ColCount;

    private static string ToSeatKey(int row, int col) => $"{row}:{col}";
}

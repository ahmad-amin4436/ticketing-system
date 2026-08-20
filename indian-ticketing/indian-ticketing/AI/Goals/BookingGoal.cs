using indian_ticketing;

namespace indian_ticketing.AI.Goals;

/// <summary>Business context handed to the AI — the agent never invents booking requirements, it only decides how to move the current page toward this goal.</summary>
public sealed class BookingGoalPassenger
{
    public string Name { get; init; } = "";
    public int Age { get; init; }
    public string Gender { get; init; } = "M";
}

public sealed class BookingGoal
{
    public string GoalId { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Destination { get; init; } = "";
    public string JourneyDate { get; init; } = "";
    public string? TrainNumber { get; init; }
    public string TravelClass { get; init; } = "";
    public string Quota { get; init; } = "";
    public IReadOnlyList<BookingGoalPassenger> Passengers { get; init; } = Array.Empty<BookingGoalPassenger>();

    public static BookingGoal FromSavedBooking(SavedBooking booking) => new()
    {
        GoalId = booking.Id,
        Origin = booking.FromCode,
        Destination = booking.ToCode,
        JourneyDate = booking.JourneyDate,
        TrainNumber = booking.TrainNo,
        TravelClass = booking.TravelClass,
        Quota = booking.Quota,
        Passengers = booking.Passengers
            .Select(p => new BookingGoalPassenger { Name = p.Name, Age = p.Age, Gender = p.Gender })
            .ToList(),
    };
}

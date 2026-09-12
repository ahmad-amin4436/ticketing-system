using System.Text.Json;
using System.Text.Json.Serialization;

namespace indian_ticketing;

public class SavedBooking
{
    public string   Id          { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public DateTime SavedAt     { get; set; } = DateTime.Now;
    public string   TrainNo     { get; set; } = "";
    public string   TrainName   { get; set; } = "";
    public string   FromCode    { get; set; } = "";
    public string   FromName    { get; set; } = "";
    public string   ToCode      { get; set; } = "";
    public string   ToName      { get; set; } = "";
    public string   DepTime     { get; set; } = "";
    public string   ArrTime     { get; set; } = "";
    public string   Duration    { get; set; } = "";
    public string   JourneyDate { get; set; } = "";
    public string   TravelClass { get; set; } = "SL";
    public string   Quota       { get; set; } = "GN";
    public List<Passenger> Passengers { get; set; } = new();
    // Who saved this booking — Booking Manager uses it to show each
    // non-admin user only their own bookings (VIEW_ALL_BOOKINGS is the
    // permission that lifts that filter). Blank for bookings saved before
    // this field existed; those are visible only to VIEW_ALL_BOOKINGS
    // holders rather than guessed at.
    public string   CreatedByUsername { get; set; } = "";

    [JsonIgnore]
    public static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "IndianTicketing", "saved_bookings.json");

    public static List<SavedBooking> LoadAll()
    {
        if (!File.Exists(StorePath)) return new();
        try   { return JsonSerializer.Deserialize<List<SavedBooking>>(File.ReadAllText(StorePath)) ?? new(); }
        catch { return new(); }
    }

    public static void SaveAll(List<SavedBooking> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath,
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public class Passenger
{
    public string Name   { get; set; } = "";
    public int    Age    { get; set; } = 25;
    public string Gender { get; set; } = "M";   // M / F / T
    public string Berth  { get; set; } = "NP";  // NP / LB / MB / UB / SL / SU
}

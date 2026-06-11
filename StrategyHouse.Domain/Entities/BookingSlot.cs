namespace StrategyHouse.Domain.Entities;

/// <summary>
/// A time slot that department heads can book to attend a strategic session.
/// Admin creates these in advance. Each slot has a max capacity (default 2);
/// once full, the slot displays as fully booked to everyone in real time.
/// </summary>
public class BookingSlot
{
    public int Id { get; set; }

    /// <summary>Admin-facing label, e.g. "الجلسة الافتتاحية" — optional.</summary>
    public string? TitleAr { get; set; }

    /// <summary>The day this slot occurs on (date portion is used).</summary>
    public DateTime SlotDate { get; set; }

    /// <summary>Start time on the slot date — stored as a TimeSpan from midnight.</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>Duration in minutes (default 90).</summary>
    public int DurationMinutes { get; set; } = 90;

    /// <summary>Optional venue/room name.</summary>
    public string? VenueAr { get; set; }

    /// <summary>Optional facilitator name shown to bookers.</summary>
    public string? FacilitatorAr { get; set; }

    /// <summary>Max number of departments that can book this slot.</summary>
    public int Capacity { get; set; } = 2;

    /// <summary>If false, slot is hidden from the public booking page.</summary>
    public bool IsOpen { get; set; } = true;

    public string? NotesAr { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SlotBooking> Bookings { get; set; } = new List<SlotBooking>();
}

/// <summary>
/// A single department's booking on a slot. (slotId, departmentId) is unique:
/// a department can only book a given slot once.
/// </summary>
public class SlotBooking
{
    public int Id { get; set; }

    public int BookingSlotId { get; set; }
    public BookingSlot? BookingSlot { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Name of the person who booked (department head). Optional.</summary>
    public string? BookedByName { get; set; }

    /// <summary>Contact email/phone for confirmations. Optional.</summary>
    public string? BookedByContact { get; set; }

    public DateTime BookedAt { get; set; } = DateTime.UtcNow;
}

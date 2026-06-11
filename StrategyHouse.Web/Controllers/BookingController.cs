using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Hubs;

namespace StrategyHouse.Web.Controllers;

/// <summary>
/// Public slot booking — department heads pick an open slot from a dropdown +
/// grid. Admin endpoints (CRUD on BookingSlots, bookings overview) are gated
/// by [Authorize(Roles = "Admin,Facilitator")]. Public endpoints are anonymous.
/// All mutations broadcast a "SlotsChanged" event so connected clients update
/// in real time.
/// </summary>
public class BookingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<BookingHub> _hub;

    public BookingController(ApplicationDbContext db, IHubContext<BookingHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ---------- Public ----------

    /// <summary>Public booking page (no login). Dept head selects their dept + slot.</summary>
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var slots = await LoadOpenSlotsAsync();
        var departments = await _db.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.NameAr)
            .ToListAsync();

        ViewBag.Departments = departments;
        return View(slots);
    }

    /// <summary>Public booking submission.</summary>
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(int slotId, int departmentId, string? bookedByName, string? bookedByContact)
    {
        if (departmentId <= 0)
        {
            TempData["BookingError"] = "يرجى اختيار الإدارة.";
            return RedirectToAction(nameof(Index));
        }

        var slot = await _db.BookingSlots
            .Include(s => s.Bookings)
            .FirstOrDefaultAsync(s => s.Id == slotId);
        if (slot == null || !slot.IsOpen)
        {
            TempData["BookingError"] = "هذا الموعد لم يعد متاحاً.";
            return RedirectToAction(nameof(Index));
        }

        if (slot.Bookings.Count >= slot.Capacity)
        {
            TempData["BookingError"] = "هذا الموعد مكتمل بالفعل. يرجى اختيار موعد آخر.";
            return RedirectToAction(nameof(Index));
        }

        if (slot.Bookings.Any(b => b.DepartmentId == departmentId))
        {
            TempData["BookingError"] = "هذه الإدارة قامت بحجز هذا الموعد مسبقاً.";
            return RedirectToAction(nameof(Index));
        }

        var dept = await _db.Departments.FindAsync(departmentId);
        if (dept == null)
        {
            TempData["BookingError"] = "الإدارة غير معروفة.";
            return RedirectToAction(nameof(Index));
        }

        _db.SlotBookings.Add(new SlotBooking
        {
            BookingSlotId = slotId,
            DepartmentId = departmentId,
            BookedByName = bookedByName,
            BookedByContact = bookedByContact,
            BookedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // unique-index race: another request booked the same (slot, dept) simultaneously
            TempData["BookingError"] = "تعذر الحجز بسبب تعارض متزامن. حدّث الصفحة وحاول مرة أخرى.";
            return RedirectToAction(nameof(Index));
        }

        await _hub.Clients.Group(BookingHub.GroupName).SendAsync("SlotsChanged");
        TempData["BookingSuccess"] = $"تم تأكيد حجز {dept.NameAr} لـ {slot.TitleAr ?? "الموعد المحدد"}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Admin ----------

    [Authorize(Roles = "Admin,Facilitator")]
    public async Task<IActionResult> Manage()
    {
        var slots = (await _db.BookingSlots
            .Include(s => s.Bookings).ThenInclude(b => b.Department)
            .ToListAsync())
            .OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime)
            .ToList();
        return View(slots);
    }

    [Authorize(Roles = "Admin,Facilitator"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSlot(string? titleAr, DateTime slotDate, string startTime,
        int durationMinutes, string? venueAr, string? facilitatorAr, int capacity, string? notesAr)
    {
        if (!TimeSpan.TryParse(startTime, out var ts))
        {
            TempData["ManageError"] = "صيغة الوقت غير صحيحة. استخدم HH:MM.";
            return RedirectToAction(nameof(Manage));
        }

        _db.BookingSlots.Add(new BookingSlot
        {
            TitleAr = string.IsNullOrWhiteSpace(titleAr) ? null : titleAr.Trim(),
            SlotDate = slotDate.Date,
            StartTime = ts,
            DurationMinutes = durationMinutes <= 0 ? 90 : durationMinutes,
            VenueAr = venueAr,
            FacilitatorAr = facilitatorAr,
            Capacity = capacity <= 0 ? 2 : capacity,
            IsOpen = true,
            NotesAr = notesAr,
        });
        await _db.SaveChangesAsync();
        await _hub.Clients.Group(BookingHub.GroupName).SendAsync("SlotsChanged");
        TempData["ManageSuccess"] = "تم إنشاء الموعد.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize(Roles = "Admin,Facilitator"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleOpen(int id)
    {
        var slot = await _db.BookingSlots.FindAsync(id);
        if (slot == null) return NotFound();
        slot.IsOpen = !slot.IsOpen;
        await _db.SaveChangesAsync();
        await _hub.Clients.Group(BookingHub.GroupName).SendAsync("SlotsChanged");
        return RedirectToAction(nameof(Manage));
    }

    [Authorize(Roles = "Admin,Facilitator"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSlot(int id)
    {
        var slot = await _db.BookingSlots.Include(s => s.Bookings).FirstOrDefaultAsync(s => s.Id == id);
        if (slot == null) return NotFound();
        _db.BookingSlots.Remove(slot);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group(BookingHub.GroupName).SendAsync("SlotsChanged");
        TempData["ManageSuccess"] = "تم حذف الموعد وكافة الحجوزات المرتبطة به.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize(Roles = "Admin,Facilitator"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking(int bookingId)
    {
        var booking = await _db.SlotBookings.FindAsync(bookingId);
        if (booking == null) return NotFound();
        _db.SlotBookings.Remove(booking);
        await _db.SaveChangesAsync();
        await _hub.Clients.Group(BookingHub.GroupName).SendAsync("SlotsChanged");
        TempData["ManageSuccess"] = "تم إلغاء الحجز.";
        return RedirectToAction(nameof(Manage));
    }

    /// <summary>JSON endpoint used by SignalR clients to refresh the slot grid.</summary>
    [AllowAnonymous]
    public async Task<IActionResult> Status()
    {
        var slots = await LoadOpenSlotsAsync();
        return Json(slots.Select(s => new
        {
            id = s.Id,
            titleAr = s.TitleAr,
            slotDate = s.SlotDate.ToString("yyyy-MM-dd"),
            startTime = s.StartTime.ToString(@"hh\:mm"),
            durationMinutes = s.DurationMinutes,
            venueAr = s.VenueAr,
            facilitatorAr = s.FacilitatorAr,
            capacity = s.Capacity,
            bookedCount = s.Bookings.Count,
            isFull = s.Bookings.Count >= s.Capacity,
            bookedDepartments = s.Bookings
                .Select(b => b.Department != null ? b.Department.NameAr : "")
                .ToArray(),
        }));
    }

    private async Task<List<BookingSlot>> LoadOpenSlotsAsync()
    {
        // ORDER BY TimeSpan is not supported by SQLite, so sort in memory after fetching.
        var rows = await _db.BookingSlots
            .Include(s => s.Bookings).ThenInclude(b => b.Department)
            .Where(s => s.IsOpen)
            .ToListAsync();
        return rows.OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime).ToList();
    }
}

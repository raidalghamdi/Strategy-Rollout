using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 6 — seeds a predefined roster (5-8 members) for each of the 17 departments,
// surfaced as default-checked checkboxes in journey stage 1. Idempotent: skips a
// department if it already has any roster rows so admin edits are never overwritten.
public static class RosterSeeder
{
    // Phase 19.8 — the shuffle seed is configurable (StrategyContent:RandomSeed).
    // Null (default) → Random.Shared (non-deterministic). A fixed int reproduces the
    // same roster across runs when determinism is explicitly required.
    public static async Task RunAsync(ApplicationDbContext db, int? randomSeed = null)
    {
        var depts = await db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptCode)
            .Select(d => d.DeptCode).ToListAsync();
        if (depts.Count == 0) return;

        var roles = new[] { "مدير الإدارة", "نائب المدير", "رئيس قسم", "أخصائي أول", "أخصائي", "محلل", "منسق" };
        var firstNames = new[]
        {
            "أحمد", "محمد", "عبدالله", "خالد", "فهد", "سلطان", "نايف", "ماجد", "بدر", "تركي",
            "نورة", "سارة", "ريم", "هند", "لمياء", "منيرة", "أمل", "غادة", "دانة", "وجدان",
        };
        var lastNames = new[]
        {
            "العتيبي", "القحطاني", "الدوسري", "المطيري", "الشهري", "الغامدي", "الحربي",
            "السبيعي", "العنزي", "الزهراني", "البقمي", "العمري", "الرشيدي", "الجهني", "الشمري",
        };

        var rnd = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        var toAdd = new List<DepartmentRoster>();

        foreach (var dept in depts)
        {
            if (await db.DepartmentRoster.AnyAsync(r => r.DeptCode == dept)) continue;

            int count = 5 + rnd.Next(0, 4); // 5-8 members
            var used = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                string name;
                do { name = firstNames[rnd.Next(firstNames.Length)] + " " + lastNames[rnd.Next(lastNames.Length)]; }
                while (!used.Add(name));

                toAdd.Add(new DepartmentRoster
                {
                    DeptCode = dept,
                    NameAr = name,
                    Role = i == 0 ? roles[0] : roles[rnd.Next(1, roles.Length)],
                    IsDefaultAttending = true,
                    IsActive = true,
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.DepartmentRoster.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }
}

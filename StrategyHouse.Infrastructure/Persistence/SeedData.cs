using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Infrastructure.Persistence;

/// <summary>
/// Seeds the strict strategy schema for the General Authority for Competition (GAC):
/// 5 pillars, 13 objectives, 17 departments, 23 initiatives, 220 projects
/// (110 strategic / 110 operational) and 134 KPIs (14 strategic / 120 operational).
/// Idempotent: guarded by Pillars.AnyAsync(). Identity admin is seeded separately.
/// </summary>
public static class SeedData
{
    public static async Task RunAsync(
        ApplicationDbContext db,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole<int>> roleManager)
    {
        await db.Database.MigrateAsync();
        await SeedRolesAndAdminAsync(userManager, roleManager);
        await SeedStrategyDataAsync(db);
        await SeedSampleAccessCodesAsync(db);
    }

    // Seeds three deterministic sample access codes (one per demo department)
    // so the journey flow is testable on a fresh install. Idempotent per code.
    private static async Task SeedSampleAccessCodesAsync(ApplicationDbContext db)
    {
        var samples = new (string Code, string Dept)[]
        {
            ("GAC202", "DEPT-02"),
            ("GAC206", "DEPT-06"),
            ("GAC208", "DEPT-08"),
        };
        var added = false;
        foreach (var (code, dept) in samples)
        {
            if (await db.DepartmentAccessCodes.AnyAsync(c => c.Code == code)) continue;
            db.DepartmentAccessCodes.Add(new DepartmentAccessCode { Code = code, DeptCode = dept, IsActive = true });
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAndAdminAsync(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole<int>> roleManager)
    {
        foreach (var role in new[] { "Admin", "Facilitator", "Viewer", "CX" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<int>(role));
        }

        // Phase 20.8 — prefer the SEED_ADMIN_PASSWORD env var (production). If it's
        // missing, fall back to a strong default that satisfies the new policy so the
        // app still boots; operators are expected to rotate the admin password right
        // after the first sign-in.
        // Phase 20.19 — also USED as a forced reset target on every boot, so an admin
        // who forgot the password can recover by redeploying. Set SEED_ADMIN_RESET=false
        // in production to disable the unconditional reset once the password is known.
        var seedPassword = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(seedPassword))
        {
            Console.WriteLine("[SeedData] WARNING: SEED_ADMIN_PASSWORD not set; using default strong password. Rotate immediately.");
            seedPassword = "Admin@2026Strong";
        }

        var adminUser = await userManager.FindByEmailAsync("admin@gac.gov.sa");
        if (adminUser == null)
        {
            adminUser = new AppUser
            {
                UserName = "admin@gac.gov.sa",
                Email = "admin@gac.gov.sa",
                EmailConfirmed = true,
                FullNameAr = "مدير المنصة",
                AppRole = UserRole.Admin,
                IsActive = true,
            };
            var createAdmin = await userManager.CreateAsync(adminUser, seedPassword);
            if (!createAdmin.Succeeded)
            {
                Console.WriteLine($"[SeedData] Failed to create admin: {string.Join("; ", createAdmin.Errors.Select(e => e.Description))}");
                return;
            }
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
        else
        {
            // Phase 20.19 — forced password reset on boot (opt-out via SEED_ADMIN_RESET=false).
            var resetFlag = Environment.GetEnvironmentVariable("SEED_ADMIN_RESET");
            var shouldReset = string.IsNullOrWhiteSpace(resetFlag) ||
                              !string.Equals(resetFlag, "false", StringComparison.OrdinalIgnoreCase);
            if (shouldReset)
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(adminUser);
                var resetResult = await userManager.ResetPasswordAsync(adminUser, token, seedPassword);
                if (resetResult.Succeeded)
                {
                    Console.WriteLine("[SeedData] Admin password reset to SEED_ADMIN_PASSWORD on boot.");
                    // Make sure the account is unlocked + active + email-confirmed so logins succeed.
                    if (await userManager.IsLockedOutAsync(adminUser))
                        await userManager.SetLockoutEndDateAsync(adminUser, null);
                    if (!adminUser.EmailConfirmed || !adminUser.IsActive)
                    {
                        adminUser.EmailConfirmed = true;
                        adminUser.IsActive = true;
                        await userManager.UpdateAsync(adminUser);
                    }
                }
                else
                {
                    Console.WriteLine($"[SeedData] Failed to reset admin password: {string.Join("; ", resetResult.Errors.Select(e => e.Description))}");
                }
            }
            // Ensure the Admin role assignment exists even on legacy DBs.
            if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
                await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        // Phase 20.33 (Comment 8) — seed default CX user (idempotent)
        var cxUser = await userManager.FindByEmailAsync("cx@gac.gov.sa");
        if (cxUser == null)
        {
            cxUser = new AppUser
            {
                UserName = "cx@gac.gov.sa",
                Email = "cx@gac.gov.sa",
                EmailConfirmed = true,
                FullNameAr = "مستخدم CX",
                AppRole = UserRole.CX,
                IsActive = true,
            };
            var createCx = await userManager.CreateAsync(cxUser, "CX@2026Strong");
            if (createCx.Succeeded)
            {
                await userManager.AddToRoleAsync(cxUser, "CX");
                Console.WriteLine("[SeedData] CX user cx@gac.gov.sa created.");
            }
            else
            {
                Console.WriteLine($"[SeedData] Failed to create CX user: {string.Join("; ", createCx.Errors.Select(e => e.Description))}");
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(cxUser, "CX"))
                await userManager.AddToRoleAsync(cxUser, "CX");
        }

        // Phase 20.34 (Comment B) — seed a JOURNEY TEST user (idempotent). This account
        // is intended for manual end-to-end testing of the employee journey flow.
        // Email:    journey.test@gac.gov.sa
        // Password: Journey@2026Strong
        // Scope:    TEST (→ SEC_ALL synthetic dept, walks the unified all-sectors journey)
        // Role:     Facilitator (حتى تظهر له روابط الرحلة دون أدوات إدارية)
        var journeyTester = await userManager.FindByEmailAsync("journey.test@gac.gov.sa");
        if (journeyTester == null)
        {
            journeyTester = new AppUser
            {
                UserName = "journey.test@gac.gov.sa",
                Email = "journey.test@gac.gov.sa",
                EmailConfirmed = true,
                FullNameAr = "حساب تجربة الرحلة",
                AppRole = UserRole.Facilitator,
                JourneyScopeKey = "TEST",
                IsActive = true,
            };
            var createTester = await userManager.CreateAsync(journeyTester, "Journey@2026Strong");
            if (createTester.Succeeded)
            {
                await userManager.AddToRoleAsync(journeyTester, "Facilitator");
                Console.WriteLine("[SeedData] Journey test user journey.test@gac.gov.sa created (password: Journey@2026Strong).");
            }
            else
            {
                Console.WriteLine($"[SeedData] Failed to create journey test user: {string.Join("; ", createTester.Errors.Select(e => e.Description))}");
            }
        }
        else
        {
            // Ensure scope + role stay correct on existing installs.
            bool dirty = false;
            if (journeyTester.JourneyScopeKey != "TEST")
            {
                journeyTester.JourneyScopeKey = "TEST";
                dirty = true;
            }
            if (journeyTester.AppRole != UserRole.Facilitator)
            {
                journeyTester.AppRole = UserRole.Facilitator;
                dirty = true;
            }
            if (dirty) await userManager.UpdateAsync(journeyTester);
            if (!await userManager.IsInRoleAsync(journeyTester, "Facilitator"))
                await userManager.AddToRoleAsync(journeyTester, "Facilitator");
        }
    }

    // Phase 20 — canonical 20-department list (DEPT-01..DEPT-20) with the three real
    // sectors. Idempotent per code: inserts only DeptCodes that don't already exist, so it
    // is safe to call on an empty DB, a legacy 17-row DB, or the production 20-row DB.
    public static readonly (string Code, string Ar, string Parent)[] CanonicalDepartments =
    {
        ("DEPT-01", "الإدارة التنفيذية للأمن السيبراني ومكتب البيانات", ""),
        ("DEPT-02", "الإدارة التنفيذية للمخاطر والحوكمة والالتزام", ""),
        ("DEPT-03", "الإدارة التنفيذية للاستراتيجية وتميّز الأعمال", ""),
        ("DEPT-04", "الأمانة العامة لمجلس الادارة", ""),
        ("DEPT-05", "المراجعة الداخلية", ""),
        ("DEPT-06", "أمانة لجنة الفصل", ""),
        ("DEPT-07", "الإدارة التنفيذية لتقنية المعلومات والتحول الرقمي", "قطاع الدعم المؤسسي"),
        ("DEPT-08", "الإدارة التنفيذية للتواصل المؤسسي", "قطاع الدعم المؤسسي"),
        ("DEPT-09", "الإدارة التنفيذية للخدمات المساندة", "قطاع الدعم المؤسسي"),
        ("DEPT-10", "الإدارة التنفيذية للشؤون المالية", "قطاع الدعم المؤسسي"),
        ("DEPT-11", "الإدارة التنفيذية للموارد البشرية", "قطاع الدعم المؤسسي"),
        ("DEPT-12", "أكاديمية المنافسة", "قطاع الدعم المؤسسي"),
        ("DEPT-13", "الإدارة التنفيذية لرقابة الاسواق والتحليل الاقتصادي", "قطاع الشؤون الاقتصادية"),
        ("DEPT-14", "الإدارة التنفيذية لدعم السياسات", "قطاع الشؤون الاقتصادية"),
        ("DEPT-15", "الإدارة التنفيذية للدراسات الاقتصادية", "قطاع الشؤون الاقتصادية"),
        ("DEPT-16", "الإدارة التنفيذية للاندماجات والاستحواذات", "قطاع الشؤون الاقتصادية"),
        ("DEPT-17", "الإدارة التنفيذية للامتثال والتسوية", "قطاع الشؤون القانونية"),
        ("DEPT-18", "الإدارة التنفيذية للتحريات والتحقيق", "قطاع الشؤون القانونية"),
        ("DEPT-19", "الإدارة التنفيذية للدراسات القانونية والتقاضي", "قطاع الشؤون القانونية"),
        ("DEPT-20", "مكتب الرئيس التنفيذي", ""),
    };

    public static async Task SeedCanonicalDepartmentsAsync(ApplicationDbContext db)
    {
        var existing = await db.Departments.Select(d => d.DeptCode).ToListAsync();
        var existingSet = existing.ToHashSet();
        var added = false;
        foreach (var (code, ar, parent) in CanonicalDepartments)
        {
            if (existingSet.Contains(code)) continue;
            db.Departments.Add(new Department
            {
                DeptCode = code,
                NameAr = ar,
                NameEn = code,
                ParentSector = string.IsNullOrEmpty(parent) ? null : parent,
                Level = 2,
                IsActive = true,
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    public static async Task SeedStrategyDataAsync(ApplicationDbContext db)
    {
        if (await db.Pillars.AnyAsync()) return;

        var rnd = new Random(20260614);
        var start = new DateTime(2026, 1, 1);
        var end = new DateTime(2030, 12, 31);

        // ---- 5.1 Pillars ----
        var pillarDefs = new (string Code, string Name, decimal Budget, decimal Liquidity)[]
        {
            ("PLR-01", "تمكين المنافسة", 50000000m, 45000000m),
            ("PLR-02", "حماية المنافسة", 35000000m, 32000000m),
            ("PLR-03", "الشراكة والتعاون", 20000000m, 18000000m),
            ("PLR-04", "الكفاءة المؤسسية", 40000000m, 36000000m),
            ("PLR-05", "الابتكار والتقنيات الرقمية", 55000000m, 50000000m),
        };
        var pillars = pillarDefs.Select(p => new Pillar
        {
            PlrCode = p.Code,
            PillarName = p.Name,
            Budget = p.Budget,
            Liquidity = p.Liquidity,
            StartDates = start,
            EndDates = end,
            PlrPeriods = "6Y",
        }).ToList();
        db.Pillars.AddRange(pillars);
        await db.SaveChangesAsync();

        // ---- 5.2 Objectives ----
        var objectiveDefs = new (string Code, string Name, string Plr)[]
        {
            ("OBJ-01-01", "تطوير منظومة المنافسة التشريعية والتنظيمية", "PLR-01"),
            ("OBJ-01-02", "رفع مستوى الوعي بثقافة المنافسة", "PLR-01"),
            ("OBJ-01-03", "تحسين بيئة الأعمال للأسواق التنافسية", "PLR-01"),
            ("OBJ-01-04", "دعم المنافسة العادلة في الأسواق الناشئة", "PLR-01"),
            ("OBJ-02-01", "كشف ومعالجة الممارسات الاحتكارية", "PLR-02"),
            ("OBJ-02-02", "تعزيز الامتثال لأحكام نظام المنافسة", "PLR-02"),
            ("OBJ-03-01", "بناء شراكات استراتيجية محلية ودولية", "PLR-03"),
            ("OBJ-03-02", "تعزيز التعاون مع الجهات الرقابية", "PLR-03"),
            ("OBJ-04-01", "تطوير الكوادر البشرية وبناء القدرات", "PLR-04"),
            ("OBJ-04-02", "تحسين كفاءة العمليات والخدمات المساندة", "PLR-04"),
            ("OBJ-04-03", "ترسيخ الحوكمة وإدارة المخاطر", "PLR-04"),
            ("OBJ-05-01", "تبني التقنيات الرقمية والذكاء الاصطناعي", "PLR-05"),
            ("OBJ-05-02", "تطوير منصات البيانات والتحليلات", "PLR-05"),
        };
        var objCountPerPillar = objectiveDefs.GroupBy(o => o.Plr).ToDictionary(g => g.Key, g => g.Count());
        var objectives = objectiveDefs.Select(o =>
        {
            var pillar = pillars.First(p => p.PlrCode == o.Plr);
            var n = objCountPerPillar[o.Plr];
            return new Objective
            {
                ObjectiveCode = o.Code,
                ObjectiveName = o.Name,
                PlrCode = o.Plr,
                Budget = Math.Round((pillar.Budget ?? 0) / n, 2),
                Liquidity = Math.Round((pillar.Liquidity ?? 0) / n, 2),
                StartDates = start,
                EndDates = end,
                ObjPeriod = "6Y",
            };
        }).ToList();
        db.Objectives.AddRange(objectives);
        await db.SaveChangesAsync();

        // ---- 5.3 Departments (canonical 20, DEPT-01..DEPT-20) ----
        // Phase 20 — the canonical list now mirrors the production DB (20 rows with the
        // three real sectors). Seeding is idempotent PER CODE: a row is inserted only if
        // its DeptCode does not already exist. When the production DB is swapped in (which
        // already has 20 rows — or a legacy DB with the old 17), nothing is touched here
        // (the whole strategy seed is also short-circuited by Pillars.AnyAsync above).
        await SeedCanonicalDepartmentsAsync(db);

        var departments = await db.Departments.ToListAsync();

        // Pillar → preferred department codes (for weighted assignment). Falls back to any
        // existing dept code if a preferred one is absent (e.g. legacy 17-dept DB).
        var allDeptCodes = departments.Select(d => d.DeptCode).ToHashSet();
        string[] Pref(params string[] codes)
        {
            var present = codes.Where(allDeptCodes.Contains).ToArray();
            return present.Length > 0 ? present : departments.Select(d => d.DeptCode).ToArray();
        }
        var pillarDepts = new Dictionary<string, string[]>
        {
            ["PLR-01"] = Pref("DEPT-13", "DEPT-14", "DEPT-15", "DEPT-03"),
            ["PLR-02"] = Pref("DEPT-17", "DEPT-18", "DEPT-19", "DEPT-13"),
            ["PLR-03"] = Pref("DEPT-03", "DEPT-08", "DEPT-04", "DEPT-20"),
            ["PLR-04"] = Pref("DEPT-09", "DEPT-10", "DEPT-11", "DEPT-12", "DEPT-05", "DEPT-02"),
            ["PLR-05"] = Pref("DEPT-01", "DEPT-07", "DEPT-03"),
        };
        string DeptName(string code) => departments.First(d => d.DeptCode == code).NameAr!;
        string PickDept(string plr) { var arr = pillarDepts[plr]; return arr[rnd.Next(arr.Length)]; }

        var personNames = new[]
        {
            "أحمد العتيبي", "نورة القحطاني", "خالد الدوسري", "سارة المطيري", "فهد الشهري",
            "ريم الغامدي", "عبدالله الحربي", "لمياء السبيعي", "ماجد العنزي", "هند الزهراني",
            "سلطان البقمي", "منيرة العمري", "يوسف الرشيدي", "أمل الجهني", "بدر الشمري",
            "وجدان المالكي", "تركي العسيري", "غادة الفيفي", "نايف القرني", "دانة الخالدي",
        };
        string PickPerson() => personNames[rnd.Next(personNames.Length)];

        // ---- 5.4 Initiatives (23) ----
        // 2 per objective = 26, drop 1 each from OBJ-04-02, OBJ-04-03, OBJ-05-02 → 23
        var dropSecond = new HashSet<string> { "OBJ-04-02", "OBJ-04-03", "OBJ-05-02" };
        var initiativeNameBank = new[]
        {
            "تحديث لائحة الاندماجات والاستحواذات", "إطلاق منصة الشكاوى الإلكترونية",
            "برنامج تدريب المحققين الاقتصاديين", "مرصد بيانات الأسواق", "حملة الوعي بثقافة المنافسة",
            "تطوير دليل الامتثال للمنشآت", "برنامج الشراكات الدولية", "أتمتة إجراءات البت في البلاغات",
            "مركز التميز في تحليل الأسواق", "منصة الإفصاح والشفافية", "برنامج بناء القدرات القيادية",
            "إطار حوكمة المخاطر المؤسسية", "بوابة الخدمات الرقمية الموحدة", "نظام الإنذار المبكر للاحتكار",
            "مؤشر تنافسية القطاعات", "برنامج التعاون مع الجهات الرقابية", "مختبر الابتكار التنظيمي",
            "محرك التحليلات التنبؤية", "تطوير السياسات التنافسية", "حاضنة المواهب التحليلية",
            "منصة إدارة القضايا", "برنامج تمكين الأسواق الناشئة", "نظام قياس أثر التدخلات التنظيمية",
        };

        var initiatives = new List<Initiative>();
        int initSeq = 1;
        foreach (var obj in objectives)
        {
            int count = dropSecond.Contains(obj.ObjectiveCode) ? 1 : 2;
            for (int j = 0; j < count; j++)
            {
                var budget = Math.Round((decimal)(1_500_000 + rnd.Next(0, 3_500_001)), 2);
                var sMonth = rnd.Next(0, 9); // 2026 Q1-Q3
                var sDate = new DateTime(2026, 1, 1).AddMonths(sMonth);
                var eDate = sDate.AddMonths(18 + rnd.Next(0, 19));
                var name = initiativeNameBank[(initSeq - 1) % initiativeNameBank.Length];
                initiatives.Add(new Initiative
                {
                    InitiativeCode = $"INIT-{initSeq:D3}",
                    InitiativeName = name,
                    ObjectiveCode = obj.ObjectiveCode,
                    ObjectiveName = obj.ObjectiveName,
                    Owners = DeptName(PickDept(obj.PlrCode!)),
                    Budget = budget,
                    Liquidity = Math.Round(budget * 0.9m, 2),
                    StartDates = sDate,
                    EndDates = eDate,
                });
                initSeq++;
            }
        }
        db.Initiatives.AddRange(initiatives);
        await db.SaveChangesAsync();

        // Initiative → its pillar (via objective)
        var objToPlr = objectives.ToDictionary(o => o.ObjectiveCode, o => o.PlrCode!);

        // ---- 5.5 Projects (220: 110 strategic / 110 operational) ----
        var statuses = new[] { "مخطط", "قيد التنفيذ", "قيد التنفيذ", "قيد التنفيذ", "متأخر", "مكتمل", "متوقف" };
        var phases = new[] { "تخطيط", "تصميم", "تنفيذ", "اختبار", "إغلاق" };
        var projectFlavors = new[]
        {
            "نظام إدارة الشكاوى", "ترقية قاعدة بيانات الأسواق", "برنامج تدريب", "لوحة مؤشرات تنفيذية",
            "أتمتة سير العمل", "بوابة المستفيدين", "دراسة قطاعية", "تطوير لائحة تنفيذية",
            "حملة توعوية", "ورشة عمل تخصصية", "تكامل أنظمة", "مراجعة إجراءات", "نموذج تحليلي",
            "منصة تعاون", "تقرير رصد سوق", "برنامج امتثال", "تحديث سياسة", "مبادرة تحول رقمي",
        };

        var projects = new List<Project>();
        int prjSeq = 1;
        int strategicLeft = 110, operationalLeft = 110;
        // Round-robin across initiatives until 220 created
        int total = 220;
        for (int i = 0; i < total; i++)
        {
            var init = initiatives[i % initiatives.Count];
            var plr = objToPlr[init.ObjectiveCode!];

            // alternate type but respect remaining quotas
            bool strategic;
            if (strategicLeft == 0) strategic = false;
            else if (operationalLeft == 0) strategic = true;
            else strategic = (i % 2 == 0);
            if (strategic) strategicLeft--; else operationalLeft--;

            var budget = Math.Round((decimal)(200_000 + rnd.Next(0, 2_800_001)), 2);
            var liquidity = Math.Round(budget * (decimal)(0.80 + rnd.NextDouble() * 0.15), 2);
            var gac = Math.Round(budget * (decimal)(0.70 + rnd.NextDouble() * 0.30), 2);
            var deptCode = PickDept(plr);

            // 7-year liquidity spread over the project window (pick 3-5 consecutive years from 2026)
            var spread = new decimal[7]; // 2026..2032
            int firstYear = rnd.Next(0, 3);          // 2026-2028 start
            int span = 3 + rnd.Next(0, 3);            // 3-5 years
            int lastYear = Math.Min(6, firstYear + span - 1);
            int activeYears = lastYear - firstYear + 1;
            decimal per = Math.Round(liquidity / activeYears, 2);
            decimal accumulated = 0;
            for (int y = firstYear; y <= lastYear; y++)
            {
                if (y == lastYear) spread[y] = Math.Round(liquidity - accumulated, 2);
                else { spread[y] = per; accumulated += per; }
            }

            projects.Add(new Project
            {
                ProjectCode = $"PRJ-{prjSeq:D3}",
                ProjectName = $"مشروع {prjSeq} — {projectFlavors[rnd.Next(projectFlavors.Length)]}",
                InitiativeCode = init.InitiativeCode,
                PlrCode = plr,
                ProjectType = strategic ? "استراتيجي" : "تشغيلي",
                ProjectStatus = statuses[rnd.Next(statuses.Length)],
                Budget = budget,
                Liquidity = liquidity,
                Liquidity2025 = spread[0],
                Liquidity2026 = spread[1],
                Liquidity2027 = spread[2],
                Liquidity2028 = spread[3],
                Liquidity2029 = spread[4],
                Liquidity2030 = spread[5],
                Liquidity2031 = spread[6],
                GacBudget = gac,
                ProjectSponsor = PickPerson(),
                ProjectManager = PickPerson(),
                DepartmentCode = deptCode,
                Division = DeptName(deptCode),
                ProjectPhase = phases[rnd.Next(phases.Length)],
            });
            prjSeq++;
        }
        db.Projects.AddRange(projects);
        await db.SaveChangesAsync();

        // ---- 5.6 KPIs (134: 14 strategic / 120 operational) ----
        var kpiNameBank = new[]
        {
            "نسبة الشكاوى المغلقة خلال 30 يوم", "عدد القضايا المحالة للقضاء", "معدل رضا الجهات الشريكة",
            "زمن البت في طلبات الاندماج", "نسبة أتمتة العمليات", "عدد المنشآت الملتزمة بالنظام",
            "نسبة تغطية رصد الأسواق", "معدل الوعي بثقافة المنافسة", "عدد الدراسات الاقتصادية المنجزة",
            "نسبة إنجاز المشاريع في وقتها", "معدل دوران الموظفين", "نسبة تنفيذ خطة المخاطر",
            "عدد الاتفاقيات الدولية المبرمة", "زمن الاستجابة للبلاغات", "نسبة رضا المستفيدين",
            "عدد البلاغات المعالجة", "نسبة الامتثال للحوكمة", "معدل توافر الأنظمة الرقمية",
            "نسبة البيانات المؤتمتة", "عدد ورش التوعية المنفذة",
        };
        var freqs = new[] { "شهري", "ربع سنوي", "نصف سنوي", "سنوي" };
        var units = new[] { "%", "عدد", "يوم", "ريال", "ساعة" };
        var directions = new[] { "تصاعدي", "تنازلي" };
        var automation = new[] { "مؤتمت بالكامل", "جزئي", "يدوي" };

        var kpis = new List<Kpi>();
        int kpiSeq = 1;

        // Strategic: PLR-01:3, PLR-02:3, PLR-03:3, PLR-04:3, PLR-05:2
        var strategicPerPillar = new (string Plr, int Count)[]
        {
            ("PLR-01", 3), ("PLR-02", 3), ("PLR-03", 3), ("PLR-04", 3), ("PLR-05", 2),
        };
        foreach (var (plr, cnt) in strategicPerPillar)
        {
            var pillarObjs = objectives.Where(o => o.PlrCode == plr).ToList();
            for (int k = 0; k < cnt; k++)
            {
                var obj = pillarObjs[k % pillarObjs.Count];
                var deptCode = PickDept(plr);
                kpis.Add(BuildKpi(kpiSeq++, "استراتيجي", obj.ObjectiveCode, plr, deptCode,
                    DeptName(deptCode), kpiNameBank, freqs, units, directions, automation, rnd));
            }
        }

        // Operational: 120 scattered across 17 departments (~7 each)
        for (int k = 0; k < 120; k++)
        {
            var dept = departments[k % departments.Count];
            // pick an objective whose pillar tends to match the department's work; fallback random
            var obj = objectives[rnd.Next(objectives.Count)];
            kpis.Add(BuildKpi(kpiSeq++, "تشغيلي", obj.ObjectiveCode, obj.PlrCode, dept.DeptCode,
                dept.NameAr!, kpiNameBank, freqs, units, directions, automation, rnd));
        }

        db.Kpis.AddRange(kpis);
        await db.SaveChangesAsync();
    }

    private static Kpi BuildKpi(
        int seq, string type, string? objCode, string? plr, string deptCode, string deptName,
        string[] names, string[] freqs, string[] units, string[] dirs, string[] autos, Random rnd)
    {
        decimal min = rnd.Next(0, 51);
        decimal max = rnd.Next(80, 101);
        // 6 increasing targets between min and max
        var targets = new string[6];
        decimal step = (max - min) / 6m;
        for (int t = 0; t < 6; t++)
            targets[t] = Math.Round(min + step * (t + 1), 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new Kpi
        {
            KpiCode = $"KPI-{seq:D3}",
            KpiName = names[rnd.Next(names.Length)],
            ActivationStatus = rnd.Next(0, 10) == 0 ? "Inactive" : "Active",
            KpiType = type,
            ObjectiveCode = objCode,
            PlrCode = plr,
            DepartmentCode = deptCode,
            Division = deptName,
            Frequency = freqs[rnd.Next(freqs.Length)],
            Unit = units[rnd.Next(units.Length)],
            Direction = dirs[rnd.Next(dirs.Length)],
            IndexWeight = (rnd.Next(1, 6)).ToString(),
            Minimum = min,
            Maximum = max,
            Target2025 = targets[0],
            Target2026 = targets[1],
            Target2027 = targets[2],
            Target2028 = targets[3],
            Target2029 = targets[4],
            Target2030 = targets[5],
            AutomationStatus = autos[rnd.Next(autos.Length)],
        };
    }
}

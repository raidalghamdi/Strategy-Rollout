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
        foreach (var role in new[] { "Admin", "Facilitator", "Viewer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<int>(role));
        }

        if (await userManager.FindByEmailAsync("admin@gac.gov.sa") == null)
        {
            var admin = new AppUser
            {
                UserName = "admin@gac.gov.sa",
                Email = "admin@gac.gov.sa",
                EmailConfirmed = true,
                FullNameAr = "مدير المنصة",
                AppRole = UserRole.Admin,
            };
            await userManager.CreateAsync(admin, "Demo@123");
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }

    public static async Task SeedStrategyDataAsync(ApplicationDbContext db)
    {
        if (await db.Pillars.AnyAsync()) return;

        var rnd = new Random(20260614);
        var start = new DateTime(2025, 1, 1);
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

        // ---- 5.3 Departments (17, GAC Level-2) ----
        var deptDefs = new (string Code, string Ar, string En, string Parent)[]
        {
            ("DEPT-01", "مكتب الرئيس التنفيذي", "CEO Office", "CEO"),
            ("DEPT-02", "الاستراتيجية وتميز الأعمال", "Strategy & Business Excellence", "CEO"),
            ("DEPT-03", "المراجعة الداخلية", "Internal Audit", "CEO"),
            ("DEPT-04", "المخاطر والحوكمة والالتزام", "Risk Governance & Compliance", "CEO"),
            ("DEPT-05", "الأمن السيبراني ومكتب البيانات", "Cybersecurity & Data Office", "CEO"),
            ("DEPT-06", "الشؤون القانونية", "Legal Affairs", "CEO"),
            ("DEPT-07", "الشؤون الاقتصادية", "Economic Affairs", "CEO"),
            ("DEPT-08", "الدعم المؤسسي", "Corporate Support", "CEO"),
            ("DEPT-09", "التحريات والتحقيق", "Investigation", "الشؤون القانونية"),
            ("DEPT-10", "الامتثال والتسوية", "Compliance & Settlement", "الشؤون القانونية"),
            ("DEPT-11", "رقابة الأسواق والتحليل الاقتصادي", "Market Monitoring", "الشؤون الاقتصادية"),
            ("DEPT-12", "دعم السياسات", "Policy Support", "الشؤون الاقتصادية"),
            ("DEPT-13", "الاندماجات والاستحواذات", "M&A", "الشؤون الاقتصادية"),
            ("DEPT-14", "الخدمات المساندة", "Support Services", "الدعم المؤسسي"),
            ("DEPT-15", "الموارد البشرية", "HR", "الدعم المؤسسي"),
            ("DEPT-16", "الشؤون المالية", "Financial Affairs", "الدعم المؤسسي"),
            ("DEPT-17", "التواصل المؤسسي", "Corporate Communications", "الدعم المؤسسي"),
        };
        var departments = deptDefs.Select(d => new Department
        {
            DeptCode = d.Code,
            NameAr = d.Ar,
            NameEn = d.En,
            ParentSector = d.Parent,
            Level = 2,
            IsActive = true,
        }).ToList();
        db.Departments.AddRange(departments);
        await db.SaveChangesAsync();

        // Pillar → preferred department codes (for weighted assignment)
        var pillarDepts = new Dictionary<string, string[]>
        {
            ["PLR-01"] = new[] { "DEPT-07", "DEPT-12", "DEPT-11", "DEPT-02" },
            ["PLR-02"] = new[] { "DEPT-06", "DEPT-09", "DEPT-10", "DEPT-11" },
            ["PLR-03"] = new[] { "DEPT-02", "DEPT-07", "DEPT-17", "DEPT-01" },
            ["PLR-04"] = new[] { "DEPT-08", "DEPT-14", "DEPT-15", "DEPT-16", "DEPT-03", "DEPT-04" },
            ["PLR-05"] = new[] { "DEPT-05", "DEPT-02", "DEPT-08" },
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
                var sMonth = rnd.Next(0, 9); // 2025 Q1-Q3
                var sDate = new DateTime(2025, 1, 1).AddMonths(sMonth);
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

            // 7-year liquidity spread over the project window (pick 3-5 consecutive years from 2025)
            var spread = new decimal[7]; // 2025..2031
            int firstYear = rnd.Next(0, 3);          // 2025-2027 start
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

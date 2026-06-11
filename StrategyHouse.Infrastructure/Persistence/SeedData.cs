using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Infrastructure.Persistence;

/// <summary>
/// Seeds the GAC Strategy House (vision, mission, 5 values, 5 pillars, 13 objectives)
/// as placeholder data, plus 18 placeholder departments and supporting catalog content.
/// All seed data is editable through the admin UI and intended to be replaced with
/// the strategy office's real content.
/// </summary>
public static class SeedData
{
    public static async Task RunAsync(
        ApplicationDbContext db,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole<int>> roleManager)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureBookingTablesAsync(db);
        await SeedRolesAndAdminAsync(userManager, roleManager);
        await SeedFrameworkAsync(db);
        await SeedDepartmentsAsync(db);
        await SeedCommitmentsAsync(db);
        await SeedSurveyAsync(db);
        await SeedQuizAsync(db);
        await SeedSampleSessionAsync(db);
        await SeedBookingSlotsAsync(db);
    }

    private static async Task SeedBookingSlotsAsync(ApplicationDbContext db)
    {
        if (await db.BookingSlots.AnyAsync()) return;
        // Seed 8 slots over 4 consecutive working days, two per day at 09:00 and 13:00.
        var firstDay = DateTime.UtcNow.Date.AddDays(7);
        var titles = new[]
        {
            ("الجلسة الأولى — تعريف", "قاعة الاجتماعات الرئيسية"),
            ("الجلسة الثانية — بناء", "قاعة الاجتماعات الرئيسية"),
            ("الجلسة الثالثة — تعريف", "قاعة الورش"),
            ("الجلسة الرابعة — بناء", "قاعة الورش"),
            ("الجلسة الخامسة — تعريف", "قاعة الاجتماعات الرئيسية"),
            ("الجلسة السادسة — بناء", "قاعة الاجتماعات الرئيسية"),
            ("الجلسة السابعة — تعريف", "قاعة الورش"),
            ("الجلسة الثامنة — التزام", "قاعة الورش"),
        };
        for (var i = 0; i < titles.Length; i++)
        {
            var day = firstDay.AddDays(i / 2);
            var start = (i % 2 == 0) ? new TimeSpan(9, 0, 0) : new TimeSpan(13, 0, 0);
            db.BookingSlots.Add(new BookingSlot
            {
                TitleAr = titles[i].Item1,
                VenueAr = titles[i].Item2,
                FacilitatorAr = "مكتب الاستراتيجية",
                SlotDate = day,
                StartTime = start,
                DurationMinutes = 90,
                Capacity = 2,
                IsOpen = true,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates the BookingSlots / SlotBookings tables if they do not yet exist.
    /// EnsureCreated does not add new tables to an existing database, so this
    /// idempotent CREATE TABLE IF NOT EXISTS step handles the upgrade for
    /// already-deployed installations. Works for SQLite and MySQL.
    /// </summary>
    private static async Task EnsureBookingTablesAsync(ApplicationDbContext db)
    {
        var providerName = db.Database.ProviderName ?? "";
        var isMySql = providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                   || providerName.Contains("Pomelo", StringComparison.OrdinalIgnoreCase);

        string slotsSql;
        string bookingsSql;
        string bookingsIdxSql;

        if (isMySql)
        {
            slotsSql = @"CREATE TABLE IF NOT EXISTS BookingSlots (
                Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                TitleAr VARCHAR(255) NULL,
                SlotDate DATETIME NOT NULL,
                StartTime TIME(6) NOT NULL,
                DurationMinutes INT NOT NULL,
                VenueAr VARCHAR(255) NULL,
                FacilitatorAr VARCHAR(255) NULL,
                Capacity INT NOT NULL,
                IsOpen TINYINT(1) NOT NULL,
                NotesAr LONGTEXT NULL,
                CreatedAt DATETIME NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            bookingsSql = @"CREATE TABLE IF NOT EXISTS SlotBookings (
                Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                BookingSlotId INT NOT NULL,
                DepartmentId INT NOT NULL,
                BookedByName VARCHAR(255) NULL,
                BookedByContact VARCHAR(255) NULL,
                BookedAt DATETIME NOT NULL,
                CONSTRAINT FK_SlotBookings_BookingSlots FOREIGN KEY (BookingSlotId) REFERENCES BookingSlots(Id) ON DELETE CASCADE,
                CONSTRAINT FK_SlotBookings_Departments FOREIGN KEY (DepartmentId) REFERENCES Departments(Id) ON DELETE RESTRICT
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            bookingsIdxSql = @"CREATE UNIQUE INDEX IX_SlotBookings_Slot_Department ON SlotBookings (BookingSlotId, DepartmentId);";
        }
        else
        {
            slotsSql = @"CREATE TABLE IF NOT EXISTS BookingSlots (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                TitleAr TEXT NULL,
                SlotDate TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                DurationMinutes INTEGER NOT NULL,
                VenueAr TEXT NULL,
                FacilitatorAr TEXT NULL,
                Capacity INTEGER NOT NULL,
                IsOpen INTEGER NOT NULL,
                NotesAr TEXT NULL,
                CreatedAt TEXT NOT NULL
            );";

            bookingsSql = @"CREATE TABLE IF NOT EXISTS SlotBookings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                BookingSlotId INTEGER NOT NULL,
                DepartmentId INTEGER NOT NULL,
                BookedByName TEXT NULL,
                BookedByContact TEXT NULL,
                BookedAt TEXT NOT NULL,
                FOREIGN KEY (BookingSlotId) REFERENCES BookingSlots(Id) ON DELETE CASCADE,
                FOREIGN KEY (DepartmentId) REFERENCES Departments(Id) ON DELETE RESTRICT
            );";

            bookingsIdxSql = @"CREATE UNIQUE INDEX IF NOT EXISTS IX_SlotBookings_Slot_Department ON SlotBookings (BookingSlotId, DepartmentId);";
        }

        await db.Database.ExecuteSqlRawAsync(slotsSql);
        await db.Database.ExecuteSqlRawAsync(bookingsSql);
        try { await db.Database.ExecuteSqlRawAsync(bookingsIdxSql); }
        catch { /* index already exists on MySQL — ignore */ }
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

    private static async Task SeedFrameworkAsync(ApplicationDbContext db)
    {
        if (await db.Frameworks.AnyAsync()) return;

        var framework = new Framework
        {
            NameAr = "بيت الاستراتيجية - الهيئة العامة للمنافسة",
            NameEn = "GAC Strategy House",
            DescriptionAr = "بيئة منافسة رائدة عالميًا تسهم في الازدهار الاقتصادي",
            Shape = "house",
            IsActive = true,
        };
        db.Frameworks.Add(framework);
        await db.SaveChangesAsync();

        // Layer: Vision
        var visionLayer = new FrameworkLayer { FrameworkId = framework.Id, Type = LayerType.Vision, NameAr = "الرؤية", NameEn = "Vision", Order = 1, VisualKey = "roof" };
        db.FrameworkLayers.Add(visionLayer);
        await db.SaveChangesAsync();
        db.FrameworkElements.Add(new FrameworkElement
        {
            LayerId = visionLayer.Id, Order = 1, IconKey = "vision",
            NameAr = "بيئة منافسة رائدة عالميًا تسهم في الازدهار الاقتصادي",
            NameEn = "A world-leading competitive environment contributing to economic prosperity",
        });

        // Layer: Mission
        var missionLayer = new FrameworkLayer { FrameworkId = framework.Id, Type = LayerType.Mission, NameAr = "الرسالة", NameEn = "Mission", Order = 2, VisualKey = "banner" };
        db.FrameworkLayers.Add(missionLayer);
        await db.SaveChangesAsync();
        db.FrameworkElements.Add(new FrameworkElement
        {
            LayerId = missionLayer.Id, Order = 1, IconKey = "mission",
            NameAr = "تمكين المنافسة العادلة من خلال تطبيق احكام النظام بفعالية ودعم السياسات ورفع مستويات الوعي والامتثال بما يسهم في تحسين كفاءة الأسواق وتعزيز مصلحة المستهلك",
            NameEn = "Enable fair competition through effective enforcement, policy support, and raising awareness and compliance",
        });

        // Layer: Values (5)
        var valuesLayer = new FrameworkLayer { FrameworkId = framework.Id, Type = LayerType.Values, NameAr = "قيم الهيئة", NameEn = "Values", Order = 3, VisualKey = "values-row" };
        db.FrameworkLayers.Add(valuesLayer);
        await db.SaveChangesAsync();
        var values = new[]
        {
            ("الشفافية", "Transparency", "transparency", "#2D9CDB"),
            ("التعاون", "Collaboration", "collaboration", "#27AE60"),
            ("التميز", "Excellence", "excellence", "#F2994A"),
            ("العدالة", "Fairness", "fairness", "#9B51E0"),
            ("الابتكار", "Innovation", "innovation", "#EB5757"),
        };
        for (int i = 0; i < values.Length; i++)
        {
            db.FrameworkElements.Add(new FrameworkElement
            {
                LayerId = valuesLayer.Id, Order = i + 1,
                NameAr = values[i].Item1, NameEn = values[i].Item2,
                IconKey = values[i].Item3, ColorHex = values[i].Item4,
            });
        }

        // Layer: Pillars (5)
        var pillarsLayer = new FrameworkLayer { FrameworkId = framework.Id, Type = LayerType.Pillars, NameAr = "الركائز", NameEn = "Pillars", Order = 4, VisualKey = "pillars-row" };
        db.FrameworkLayers.Add(pillarsLayer);
        await db.SaveChangesAsync();
        var pillars = new[]
        {
            ("الركيزة الأولى: تمكين المنافسة", "Pillar 1: Enabling Competition"),
            ("الركيزة الثانية: حماية المنافسة", "Pillar 2: Protecting Competition"),
            ("الركيزة الثالثة: الشراكة والتعاون", "Pillar 3: Partnership and Cooperation"),
            ("الركيزة الرابعة: الكفاءة المؤسسية", "Pillar 4: Institutional Efficiency"),
            ("الركيزة الخامسة: الابتكار والتقنيات الرقمية", "Pillar 5: Innovation and Digital Technologies"),
        };
        var pillarIds = new List<int>();
        for (int i = 0; i < pillars.Length; i++)
        {
            var el = new FrameworkElement
            {
                LayerId = pillarsLayer.Id, Order = i + 1,
                NameAr = pillars[i].Item1, NameEn = pillars[i].Item2,
                ColorHex = "#1B5E7F",
            };
            db.FrameworkElements.Add(el);
            await db.SaveChangesAsync();
            pillarIds.Add(el.Id);
        }

        // Layer: Objectives (13) — grouped under their pillars
        var objLayer = new FrameworkLayer { FrameworkId = framework.Id, Type = LayerType.Objectives, NameAr = "الأهداف", NameEn = "Objectives", Order = 5, VisualKey = "objectives-grid" };
        db.FrameworkLayers.Add(objLayer);
        await db.SaveChangesAsync();
        var objectives = new (string ar, string en, int pillarIdx)[]
        {
            // Pillar 1 (4)
            ("تحسين بيئة تنظيمية داعمة للمنافسة العادلة", "Improve regulatory environment for fair competition", 0),
            ("تعزيز السياسات والدراسات المحفزة للمنافسة لرفع كفاءة الأسواق", "Strengthen pro-competition policies and studies", 0),
            ("رفع مستويات الامتثال للمنشآت", "Raise compliance levels among establishments", 0),
            ("تعزيز الصورة الذهنية والوعي المعرفي بدور المنافسة", "Enhance brand and public awareness", 0),
            // Pillar 2 (2)
            ("تطوير منظومة الرقابة", "Develop the monitoring system", 1),
            ("تعزيز مكافحة الممارسات المخلة بالمنافسة", "Strengthen action against anti-competitive practices", 1),
            // Pillar 3 (2)
            ("بناء تعاون فعال محليًا ودوليًا", "Build effective local and international cooperation", 2),
            ("تعزيز الحضور الدولي للهيئة في المنظمات والمنتديات الدولية", "Strengthen international presence", 2),
            // Pillar 4 (3)
            ("تحسين العمليات التشغيلية والمالية وضمان استدامتها", "Improve operational and financial processes", 3),
            ("الاستثمار بتطوير القدرات البشرية وتعزيز الثقافة المؤسسية", "Invest in human capital and culture", 3),
            ("تعزيز ممارسات الحوكمة والمخاطر والالتزام", "Strengthen governance, risk, and compliance", 3),
            // Pillar 5 (2)
            ("تمكين الاستفادة من التقنيات الناشئة والبيانات", "Leverage emerging tech and data", 4),
            ("تمكين الابتكار لتعزيز كفاءة الأسواق وتحقيق المنافسة العادلة", "Enable innovation for market efficiency", 4),
        };
        int order = 1;
        foreach (var o in objectives)
        {
            db.FrameworkElements.Add(new FrameworkElement
            {
                LayerId = objLayer.Id, Order = order++,
                NameAr = o.ar, NameEn = o.en,
                ParentElementId = pillarIds[o.pillarIdx],
                ColorHex = "#E8F4F8",
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDepartmentsAsync(ApplicationDbContext db)
    {
        if (await db.Departments.AnyAsync()) return;

        // 18 placeholder departments — strategy office will rename
        var deptNames = new[]
        {
            ("الشؤون القانونية", "Legal Affairs"),
            ("التحقيق والتفتيش", "Investigation & Inspection"),
            ("الدراسات والسياسات", "Studies & Policies"),
            ("التوعية والتثقيف", "Awareness & Education"),
            ("العلاقات الدولية", "International Relations"),
            ("الشراكات المحلية", "Local Partnerships"),
            ("الحوكمة والالتزام", "Governance & Compliance"),
            ("إدارة المخاطر", "Risk Management"),
            ("الموارد البشرية", "Human Resources"),
            ("التطوير المؤسسي", "Organizational Development"),
            ("الشؤون المالية", "Finance"),
            ("المشتريات والعقود", "Procurement & Contracts"),
            ("تقنية المعلومات", "Information Technology"),
            ("البيانات والتحليلات", "Data & Analytics"),
            ("الابتكار الرقمي", "Digital Innovation"),
            ("الاتصال المؤسسي", "Corporate Communication"),
            ("مكتب الاستراتيجية", "Strategy Office"),
            ("التدقيق الداخلي", "Internal Audit"),
        };

        foreach (var (ar, en) in deptNames)
        {
            var dept = new Department { NameAr = ar, NameEn = en, IsActive = true };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();

            // 2 placeholder projects per department
            db.DepartmentProjects.AddRange(
                new DepartmentProject { DepartmentId = dept.Id, NameAr = $"مشروع استراتيجي - {ar}", NameEn = "Strategic Project", Kind = "Strategic" },
                new DepartmentProject { DepartmentId = dept.Id, NameAr = $"مشروع تشغيلي - {ar}", NameEn = "Operational Project", Kind = "Operational" }
            );
            // 2 placeholder KPIs per department
            db.DepartmentKpis.AddRange(
                new DepartmentKpi { DepartmentId = dept.Id, NameAr = $"مؤشر الأداء الأول - {ar}", Unit = "%", Target = "90" },
                new DepartmentKpi { DepartmentId = dept.Id, NameAr = $"مؤشر الأداء الثاني - {ar}", Unit = "عدد", Target = "12" }
            );
            // 3 placeholder roles per department
            db.DepartmentRoles.AddRange(
                new DepartmentRole { DepartmentId = dept.Id, TitleAr = "مدير الإدارة", TitleEn = "Department Head" },
                new DepartmentRole { DepartmentId = dept.Id, TitleAr = "أخصائي أول", TitleEn = "Senior Specialist" },
                new DepartmentRole { DepartmentId = dept.Id, TitleAr = "أخصائي", TitleEn = "Specialist" }
            );
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedCommitmentsAsync(ApplicationDbContext db)
    {
        if (await db.CommitmentTemplates.AnyAsync()) return;

        // Universal commitments — visible to every department, linkable to any element.
        // The strategy office can add department-specific commitments later.
        var commitments = new[]
        {
            ("نلتزم بأن نطبق قيمة الشفافية في كل ما نقوم به من أعمال", CommitmentLinkType.Value),
            ("نلتزم بتعزيز التعاون داخل فريقنا ومع الإدارات الأخرى", CommitmentLinkType.Value),
            ("نلتزم بتحقيق التميز في مخرجاتنا", CommitmentLinkType.Value),
            ("نلتزم بالعدالة في التعامل مع كل الأطراف", CommitmentLinkType.Value),
            ("نلتزم بتبني الابتكار في حلولنا اليومية", CommitmentLinkType.Value),
            ("نلتزم بإنجاز مشاريعنا الاستراتيجية ضمن الإطار الزمني المحدد", CommitmentLinkType.Project),
            ("نلتزم بتحقيق مؤشرات الأداء المستهدفة لإدارتنا", CommitmentLinkType.Objective),
            ("نلتزم بدعم الركيزة الاستراتيجية الأكثر ارتباطًا بعملنا", CommitmentLinkType.Pillar),
        };
        int order = 1;
        foreach (var (text, link) in commitments)
        {
            db.CommitmentTemplates.Add(new CommitmentTemplate
            {
                TextAr = text,
                SuggestedLinkType = link,
                Order = order++,
                IsActive = true,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedSurveyAsync(ApplicationDbContext db)
    {
        if (await db.Surveys.AnyAsync()) return;

        var survey = new Survey
        {
            NameAr = "استبيان نهاية الجلسة",
            IntroAr = "نشكرك على مشاركتك. رأيك يساعدنا على تطوير الجلسات القادمة. الاستبيان يستغرق ٥ دقائق.",
            IsActive = true,
        };
        db.Surveys.Add(survey);
        await db.SaveChangesAsync();

        var questions = new (string text, SurveyQuestionType type, string? options)[]
        {
            ("كيف تقيّم وضوح الاستراتيجية بعد الجلسة؟", SurveyQuestionType.Rating, null),
            ("كيف تقيّم تنظيم الجلسة وإيقاعها؟", SurveyQuestionType.Rating, null),
            ("كيف تقيّم التفاعل مع المنصة (iPads)؟", SurveyQuestionType.Rating, null),
            ("هل تشعر أن دور إدارتك في الاستراتيجية أصبح أوضح؟", SurveyQuestionType.SingleChoice, "[\"نعم تمامًا\",\"إلى حد ما\",\"لا أشعر بفرق\"]"),
            ("ما الذي كان الأكثر فائدة لك في هذه الجلسة؟", SurveyQuestionType.OpenText, null),
            ("ما الذي يمكن تحسينه في الجلسات القادمة؟", SurveyQuestionType.OpenText, null),
        };
        int order = 1;
        foreach (var (text, type, options) in questions)
        {
            db.SurveyQuestions.Add(new SurveyQuestion
            {
                SurveyId = survey.Id,
                TextAr = text,
                Type = type,
                Order = order++,
                IsRequired = type != SurveyQuestionType.OpenText,
                MinValue = type == SurveyQuestionType.Rating ? 1 : null,
                MaxValue = type == SurveyQuestionType.Rating ? 5 : null,
                OptionsJson = options,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedQuizAsync(ApplicationDbContext db)
    {
        if (await db.Quizzes.AnyAsync()) return;

        var quiz = new Quiz
        {
            NameAr = "اختبار ما بعد الجلسة",
            IntroAr = "اختبار قصير اختياري لتعزيز ما تعلمته في الجلسة. يأخذ ٣ دقائق.",
            IsActive = true,
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync();

        db.QuizQuestions.AddRange(
            new QuizQuestion
            {
                QuizId = quiz.Id,
                TextAr = "كم عدد ركائز الاستراتيجية؟",
                Type = QuizQuestionType.SingleChoice,
                Order = 1,
                OptionsJson = "[\"٣\",\"٤\",\"٥\",\"٦\"]",
                CorrectOptionsJson = "[2]",
                FeedbackAr = "الاستراتيجية تتكون من ٥ ركائز رئيسية تشكل الهيكل الاستراتيجي للهيئة.",
            },
            new QuizQuestion
            {
                QuizId = quiz.Id,
                TextAr = "أي من التالي يعتبر قيمة من قيم الهيئة؟",
                Type = QuizQuestionType.SingleChoice,
                Order = 2,
                OptionsJson = "[\"الشفافية\",\"النمو\",\"السرعة\",\"التنافس\"]",
                CorrectOptionsJson = "[0]",
                FeedbackAr = "الشفافية إحدى قيم الهيئة الخمس: الشفافية، التعاون، التميز، العدالة، الابتكار.",
            },
            new QuizQuestion
            {
                QuizId = quiz.Id,
                TextAr = "ما هو هدف الركيزة الخامسة؟",
                Type = QuizQuestionType.SingleChoice,
                Order = 3,
                OptionsJson = "[\"الكفاءة المؤسسية\",\"الشراكة والتعاون\",\"الابتكار والتقنيات الرقمية\",\"حماية المنافسة\"]",
                CorrectOptionsJson = "[2]",
                FeedbackAr = "الركيزة الخامسة هي الابتكار والتقنيات الرقمية - تركز على الاستفادة من التقنيات الناشئة والبيانات.",
            },
            new QuizQuestion
            {
                QuizId = quiz.Id,
                TextAr = "ما الركيزة المسؤولة عن مكافحة الممارسات المخلة بالمنافسة؟",
                Type = QuizQuestionType.SingleChoice,
                Order = 4,
                OptionsJson = "[\"تمكين المنافسة\",\"حماية المنافسة\",\"الشراكة والتعاون\",\"الكفاءة المؤسسية\"]",
                CorrectOptionsJson = "[1]",
                FeedbackAr = "الركيزة الثانية - حماية المنافسة - مسؤولة عن تطوير منظومة الرقابة ومكافحة الممارسات المخلة.",
            },
            new QuizQuestion
            {
                QuizId = quiz.Id,
                TextAr = "ما الهدف الرئيسي للرؤية الاستراتيجية؟",
                Type = QuizQuestionType.SingleChoice,
                Order = 5,
                OptionsJson = "[\"زيادة الإيرادات\",\"الازدهار الاقتصادي\",\"التوسع الدولي\",\"تقليل التكاليف\"]",
                CorrectOptionsJson = "[1]",
                FeedbackAr = "رؤية الهيئة: بيئة منافسة رائدة عالميًا تسهم في الازدهار الاقتصادي.",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedSampleSessionAsync(ApplicationDbContext db)
    {
        if (await db.Sessions.AnyAsync()) return;
        var framework = await db.Frameworks.FirstAsync(f => f.IsActive);
        var depts = await db.Departments.Take(3).ToListAsync();

        var session = new Session
        {
            FrameworkId = framework.Id,
            TitleAr = "الجلسة التجريبية - اليوم الأول",
            ScheduledAt = DateTime.UtcNow.AddDays(1),
            VenueAr = "قاعة الاجتماعات الرئيسية",
            LeadFacilitator = "مكتب الاستراتيجية",
            Status = SessionStatus.Scheduled,
            AccessCode = "DEMO-001",
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        foreach (var d in depts)
        {
            db.SessionDepartments.Add(new SessionDepartment { SessionId = session.Id, DepartmentId = d.Id });
            for (int i = 1; i <= 3; i++)
            {
                db.SessionAttendees.Add(new SessionAttendee
                {
                    SessionId = session.Id,
                    DepartmentId = d.Id,
                    FullNameAr = $"عضو الفريق {i} - {d.NameAr}",
                    Email = $"attendee{i}_{d.Id}@gac.gov.sa",
                    IsDepartmentHead = i == 1,
                });
            }
        }
        await db.SaveChangesAsync();
    }
}

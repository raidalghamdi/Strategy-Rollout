using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

/// <summary>
/// Produces the Department Head Journey Map briefing artifact — a per-head
/// visual timeline of the 90-minute session with their two speaking moments
/// marked. Strategy office uses this in the individual pre-session briefing.
/// </summary>
public class JourneyMapService
{
    private readonly ApplicationDbContext _db;
    public JourneyMapService(ApplicationDbContext db) { _db = db; }

    public async Task<JourneyMapBrief> BuildAsync(int sessionId, int departmentId)
    {
        var session = await _db.Sessions
            .Include(s => s.Framework)
            .FirstAsync(s => s.Id == sessionId);
        var dept = await _db.Departments
            .Include(d => d.Projects)
            .Include(d => d.Kpis)
            .FirstAsync(d => d.Id == departmentId);

        var blocks = new List<JourneyBlock>
        {
            new(0, 2, "فحص أولي مجهول", "يصل الحضور ويُجيبون على ٣ أسئلة مجهولة عبر هواتفهم. لا تتدخل.", false),
            new(2, 7, "فيديو الافتتاح", "يُعرض فيديو الافتتاح من الراعي التنفيذي. لا تتدخل.", false),
            new(7, 17, "شرح بيت الاستراتيجية", "مكتب الاستراتيجية يشرح الرؤية والرسالة والقيم والركائز والأهداف. لا تتدخل.", false),
            new(17, 20, "تمهيد للحركة الثانية", "ميسر مكتب الاستراتيجية يقدم تمرين إدارتك في الاستراتيجية.", false),
            new(20, 22, "كلمتك الافتتاحية (دقيقتان)", $"اشرح لفريقك لماذا هذه الاستراتيجية مهمة لإدارة {dept.NameAr}. تكلم من القلب، لا تقرأ.", true),
            new(22, 60, "بناء خريطة الإدارة", "النقاش الجماعي يدير من قبل مكتب الاستراتيجية. شارك كعضو من الفريق - لا تهيمن على النقاش. اترك المساحة لأعضاء الفريق.", false),
            new(60, 75, "اختيار الالتزامات وربطها", "الفريق يختار التزامات من القائمة ويربطها بعناصر من الخريطة. شارك في الاختيار.", false),
            new(75, 83, "التوقيع الاختياري", "التوقيع تطوعي. وقّع إن أردت، ولا تضغط على أحد ليوقع.", false),
            new(83, 85, "كلمتك الختامية (دقيقتان)", "أعد التأكيد على ما التزم به الفريق. قُل: سمعت ما التزمنا به، وسأدعمه.", true),
            new(85, 90, "الاستبيان", "يمسح الحضور رمز QR ويملؤون استبيانًا قصيرًا. لا تتدخل.", false),
        };

        var notes = new List<string>
        {
            "تجنب الإجابة على الأسئلة بدلًا من الفريق. اترك النقاش ينضج.",
            "تجنب التعليق على من شارك ومن لم يشارك.",
            "إذا اعترض أحدهم على عنصر في الاستراتيجية، اشكره على المداخلة ودع الميسر يدير الحوار.",
            "احضر مبكرًا بـ ١٥ دقيقة - سيكون هناك إعداد قصير مع الميسر.",
        };

        return new JourneyMapBrief(session.TitleAr, dept.NameAr, session.ScheduledAt, blocks, notes);
    }
}

public record JourneyBlock(int FromMin, int ToMin, string Title, string Guidance, bool IsHeadSpeakingMoment);
public record JourneyMapBrief(
    string SessionTitle,
    string DepartmentName,
    DateTime ScheduledAt,
    List<JourneyBlock> Blocks,
    List<string> Notes);

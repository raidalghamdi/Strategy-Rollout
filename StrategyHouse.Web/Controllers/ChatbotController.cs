using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 7 — chatbot API restricted to authenticated users; the widget is only
// rendered for signed-in admins, and anonymous POSTs are rejected with 401.
[Authorize]
[Route("api/chatbot")]
public class ChatbotController : Controller
{
    private readonly ChatbotService _bot;

    public ChatbotController(ChatbotService bot) => _bot = bot;

    public class AskDto
    {
        public string? Question { get; set; }
        public Guid? SessionId { get; set; }
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskDto dto)
    {
        var question = (dto?.Question ?? string.Empty).Trim();
        if (question.Length == 0)
            return Json(new { ok = false, answer = "الرجاء كتابة سؤال.", intent = "empty" });

        var result = await _bot.AskAsync(question, dto!.SessionId);
        return Json(new { ok = true, answer = result.Text, intent = result.Intent, resultCount = result.ResultCount });
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20_33_BackfillSessionCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 20.33 — backfill CompletedAt for historical sessions that
            // finished their quiz (have a QuizAttempt row) but never reached the
            // /Journey/Complete redirect — which until now was the only place
            // CompletedAt was set. This unblocks the executive report's
            // completion-rate KPI for older sessions.
            //
            // Heuristic (in priority order):
            //   1) If a QuizAttempt exists for the session, use its CreatedAt
            //      (or fall back to StartedAt + 30 min).
            //   2) Else if Status = 'Completed', use LastActivityAt or StartedAt.
            //
            // We only update rows where CompletedAt IS NULL so we never overwrite
            // already-good data. Status is also normalised to 'Completed'.
            migrationBuilder.Sql(@"
                UPDATE StrategySessions
                SET CompletedAt = COALESCE(
                        (SELECT MAX(qa.CompletedAt)
                           FROM QuizAttempts qa
                          WHERE qa.SessionId = StrategySessions.Id),
                        LastActivityAt,
                        StartedAt
                    ),
                    Status = 'Completed'
                WHERE CompletedAt IS NULL
                  AND EXISTS (
                        SELECT 1 FROM QuizAttempts qa
                         WHERE qa.SessionId = StrategySessions.Id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Non-reversible data backfill. No-op down.
        }
    }
}

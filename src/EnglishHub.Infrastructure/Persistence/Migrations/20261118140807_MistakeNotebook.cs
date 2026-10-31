using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MistakeNotebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MistakeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttemptItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WrongCount = table.Column<int>(type: "int", nullable: false),
                    LastWrongAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastEventAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrectReviewDays = table.Column<int>(type: "int", nullable: false),
                    LastCorrectDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CycleTimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MistakeItems", x => x.Id);
                    table.UniqueConstraint("AK_MistakeItems_Id_UserId_QuestionId", x => new { x.Id, x.UserId, x.QuestionId });
                    table.CheckConstraint("CK_Mistake_Values", "[WrongCount] > 0 AND [CorrectReviewDays] >= 0 AND [CorrectReviewDays] <= 2 AND (([Status] = 'learning' AND [NextReviewAt] IS NOT NULL) OR ([Status] = 'mastered' AND [NextReviewAt] IS NULL AND [CorrectReviewDays] = 2))");
                    table.ForeignKey(
                        name: "FK_MistakeItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MistakeItems_AttemptItems_SourceAttemptItemId_QuestionId",
                        columns: x => new { x.SourceAttemptItemId, x.QuestionId },
                        principalTable: "AttemptItems",
                        principalColumns: new[] { "Id", "QuestionId" });
                });

            migrationBuilder.CreateTable(
                name: "MistakeProcessedAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MistakeProcessedAttempts", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_MistakeProcessedAttempts_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MistakeReviewTargets",
                columns: table => new
                {
                    AttemptItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MistakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MistakeReviewTargets", x => x.AttemptItemId);
                    table.ForeignKey(
                        name: "FK_MistakeReviewTargets_AttemptItems_AttemptItemId_QuestionId",
                        columns: x => new { x.AttemptItemId, x.QuestionId },
                        principalTable: "AttemptItems",
                        principalColumns: new[] { "Id", "QuestionId" });
                    table.ForeignKey(
                        name: "FK_MistakeReviewTargets_MistakeItems_MistakeId_UserId_QuestionId",
                        columns: x => new { x.MistakeId, x.UserId, x.QuestionId },
                        principalTable: "MistakeItems",
                        principalColumns: new[] { "Id", "UserId", "QuestionId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeItems_SourceAttemptItemId_QuestionId",
                table: "MistakeItems",
                columns: new[] { "SourceAttemptItemId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeItems_UserId_QuestionId",
                table: "MistakeItems",
                columns: new[] { "UserId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MistakeItems_UserId_Status_NextReviewAt_Id",
                table: "MistakeItems",
                columns: new[] { "UserId", "Status", "NextReviewAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeReviewTargets_AttemptItemId_QuestionId",
                table: "MistakeReviewTargets",
                columns: new[] { "AttemptItemId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeReviewTargets_MistakeId_UserId_QuestionId",
                table: "MistakeReviewTargets",
                columns: new[] { "MistakeId", "UserId", "QuestionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MistakeProcessedAttempts");

            migrationBuilder.DropTable(
                name: "MistakeReviewTargets");

            migrationBuilder.DropTable(
                name: "MistakeItems");
        }
    }
}

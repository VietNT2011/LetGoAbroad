using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionOptions_QuestionId",
                table: "QuestionOptions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_QuestionOptions_QuestionId_Id",
                table: "QuestionOptions",
                columns: new[] { "QuestionId", "Id" });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Attempts_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CommandReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttemptItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Stimulus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttemptItems_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttemptItems_QuestionOptions_QuestionId_SelectedOptionId",
                        columns: x => new { x.QuestionId, x.SelectedOptionId },
                        principalTable: "QuestionOptions",
                        principalColumns: new[] { "QuestionId", "Id" });
                    table.ForeignKey(
                        name: "FK_AttemptItems_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AttemptResults",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    BlankCount = table.Column<int>(type: "int", nullable: false),
                    ListeningRaw = table.Column<int>(type: "int", nullable: false),
                    ReadingRaw = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScoreCalculationVersion = table.Column<int>(type: "int", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptResults", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_AttemptResults_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GradingKey",
                columns: table => new
                {
                    AttemptItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Skill = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Part = table.Column<int>(type: "int", nullable: false),
                    GradingPolicyVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradingKey", x => x.AttemptItemId);
                    table.ForeignKey(
                        name: "FK_GradingKey_AttemptItems_AttemptItemId",
                        column: x => x.AttemptItemId,
                        principalTable: "AttemptItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptItems_AttemptId_Order",
                table: "AttemptItems",
                columns: new[] { "AttemptId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptItems_AttemptId_QuestionId",
                table: "AttemptItems",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttemptItems_QuestionId_SelectedOptionId",
                table: "AttemptItems",
                columns: new[] { "QuestionId", "SelectedOptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ContentVersionId",
                table: "Attempts",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_StartedAt_Id",
                table: "Attempts",
                columns: new[] { "UserId", "StartedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandReceipts_UserId_Scope_CommandId",
                table: "CommandReceipts",
                columns: new[] { "UserId", "Scope", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_ProcessedAt_OccurredAt",
                table: "OutboxEvents",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttemptResults");

            migrationBuilder.DropTable(
                name: "CommandReceipts");

            migrationBuilder.DropTable(
                name: "GradingKey");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "AttemptItems");

            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_QuestionOptions_QuestionId_Id",
                table: "QuestionOptions");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionOptions_QuestionId",
                table: "QuestionOptions",
                column: "QuestionId");
        }
    }
}

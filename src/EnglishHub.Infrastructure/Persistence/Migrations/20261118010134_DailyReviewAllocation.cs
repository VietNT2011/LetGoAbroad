using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyReviewAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_UserCards_Id_UserId",
                table: "UserCards",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CardReviews_Id_UserCardId",
                table: "CardReviews",
                columns: new[] { "Id", "UserCardId" });

            migrationBuilder.CreateTable(
                name: "DailyReviewBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GoalRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: false),
                    PlanningPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReviewBatches", x => x.Id);
                    table.UniqueConstraint("AK_DailyReviewBatches_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_ReviewBatch_Values", "[Number] > 0 AND [EstimatedMinutes] > 0 AND [Status] IN ('pending','completed','missed','superseded')");
                    table.ForeignKey(
                        name: "FK_DailyReviewBatches_Enrollments_EnrollmentId_UserId",
                        columns: x => new { x.EnrollmentId, x.UserId },
                        principalTable: "Enrollments",
                        principalColumns: new[] { "Id", "UserId" });
                    table.ForeignKey(
                        name: "FK_DailyReviewBatches_PlanGoalRevisions_GoalRevisionId_EnrollmentId",
                        columns: x => new { x.GoalRevisionId, x.EnrollmentId },
                        principalTable: "PlanGoalRevisions",
                        principalColumns: new[] { "Id", "EnrollmentId" });
                });

            migrationBuilder.CreateTable(
                name: "DailyReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserCardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    DueAtWhenAssigned = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CardReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReviewItems_CardReviews_CardReviewId_UserCardId",
                        columns: x => new { x.CardReviewId, x.UserCardId },
                        principalTable: "CardReviews",
                        principalColumns: new[] { "Id", "UserCardId" });
                    table.ForeignKey(
                        name: "FK_DailyReviewItems_DailyReviewBatches_BatchId_UserId",
                        columns: x => new { x.BatchId, x.UserId },
                        principalTable: "DailyReviewBatches",
                        principalColumns: new[] { "Id", "UserId" });
                    table.ForeignKey(
                        name: "FK_DailyReviewItems_UserCards_UserCardId_UserId",
                        columns: x => new { x.UserCardId, x.UserId },
                        principalTable: "UserCards",
                        principalColumns: new[] { "Id", "UserId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewBatches_EnrollmentId",
                table: "DailyReviewBatches",
                column: "EnrollmentId",
                unique: true,
                filter: "[Status] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewBatches_EnrollmentId_LocalDate_Number",
                table: "DailyReviewBatches",
                columns: new[] { "EnrollmentId", "LocalDate", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewBatches_EnrollmentId_UserId",
                table: "DailyReviewBatches",
                columns: new[] { "EnrollmentId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewBatches_GoalRevisionId_EnrollmentId",
                table: "DailyReviewBatches",
                columns: new[] { "GoalRevisionId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewBatches_UserId_Status_LocalDate",
                table: "DailyReviewBatches",
                columns: new[] { "UserId", "Status", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewItems_BatchId_UserCardId",
                table: "DailyReviewItems",
                columns: new[] { "BatchId", "UserCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewItems_BatchId_UserId",
                table: "DailyReviewItems",
                columns: new[] { "BatchId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewItems_CardReviewId",
                table: "DailyReviewItems",
                column: "CardReviewId",
                unique: true,
                filter: "[CardReviewId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewItems_CardReviewId_UserCardId",
                table: "DailyReviewItems",
                columns: new[] { "CardReviewId", "UserCardId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReviewItems_UserCardId_UserId",
                table: "DailyReviewItems",
                columns: new[] { "UserCardId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyReviewItems");

            migrationBuilder.DropTable(
                name: "DailyReviewBatches");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UserCards_Id_UserId",
                table: "UserCards");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CardReviews_Id_UserCardId",
                table: "CardReviews");
        }
    }
}

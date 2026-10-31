using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EffectiveGoalRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalRevisionId",
                table: "DailyTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanGoalRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    TargetScore = table.Column<int>(type: "int", nullable: false),
                    MinutesPerDay = table.Column<int>(type: "int", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SchedulingTimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanGoalRevisions", x => x.Id);
                    table.UniqueConstraint("AK_PlanGoalRevisions_Id_EnrollmentId", x => new { x.Id, x.EnrollmentId });
                    table.CheckConstraint("CK_PlanGoalRevision_Values", "[Number] >= 1 AND [TargetScore] IN (450,650,800) AND [MinutesPerDay] IN (20,30,45) AND [EffectiveAt] >= [RecordedAt]");
                    table.ForeignKey(
                        name: "FK_PlanGoalRevisions_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_GoalRevisionId_EnrollmentId",
                table: "DailyTasks",
                columns: new[] { "GoalRevisionId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanGoalRevisions_EnrollmentId_Number",
                table: "PlanGoalRevisions",
                columns: new[] { "EnrollmentId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_PlanGoalRevisions_GoalRevisionId_EnrollmentId",
                table: "DailyTasks",
                columns: new[] { "GoalRevisionId", "EnrollmentId" },
                principalTable: "PlanGoalRevisions",
                principalColumns: new[] { "Id", "EnrollmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_PlanGoalRevisions_GoalRevisionId_EnrollmentId",
                table: "DailyTasks");

            migrationBuilder.DropTable(
                name: "PlanGoalRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_GoalRevisionId_EnrollmentId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "GoalRevisionId",
                table: "DailyTasks");
        }
    }
}

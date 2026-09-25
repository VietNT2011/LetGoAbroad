using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LearningPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurriculumOrder",
                table: "ContentVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetScore = table.Column<int>(type: "int", nullable: false),
                    MinutesPerDay = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    PlanRevision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.UniqueConstraint("AK_Enrollments_Id_UserId", x => new { x.Id, x.UserId });
                    table.ForeignKey(
                        name: "FK_Enrollments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LessonProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonProgress_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DailyTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SlotKey = table.Column<int>(type: "int", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyTasks_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyTasks_Enrollments_EnrollmentId_UserId",
                        columns: x => new { x.EnrollmentId, x.UserId },
                        principalTable: "Enrollments",
                        principalColumns: new[] { "Id", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "TaskCompletions",
                columns: table => new
                {
                    DailyTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCompletions", x => x.DailyTaskId);
                    table.CheckConstraint("CK_TaskCompletion_OneSource", "([LessonProgressId] IS NOT NULL AND [AttemptId] IS NULL) OR ([LessonProgressId] IS NULL AND [AttemptId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TaskCompletions_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskCompletions_DailyTasks_DailyTaskId",
                        column: x => x.DailyTaskId,
                        principalTable: "DailyTasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskCompletions_LessonProgress_LessonProgressId",
                        column: x => x.LessonProgressId,
                        principalTable: "LessonProgress",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_ContentVersionId",
                table: "DailyTasks",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_EnrollmentId_LocalDate_SlotKey",
                table: "DailyTasks",
                columns: new[] { "EnrollmentId", "LocalDate", "SlotKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_EnrollmentId_UserId",
                table: "DailyTasks",
                columns: new[] { "EnrollmentId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_UserId_LocalDate_Status",
                table: "DailyTasks",
                columns: new[] { "UserId", "LocalDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_UserId",
                table: "Enrollments",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgress_ContentVersionId",
                table: "LessonProgress",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgress_UserId_ContentVersionId",
                table: "LessonProgress",
                columns: new[] { "UserId", "ContentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_AttemptId",
                table: "TaskCompletions",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_LessonProgressId",
                table: "TaskCompletions",
                column: "LessonProgressId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskCompletions");

            migrationBuilder.DropTable(
                name: "DailyTasks");

            migrationBuilder.DropTable(
                name: "LessonProgress");

            migrationBuilder.DropTable(
                name: "Enrollments");

            migrationBuilder.DropColumn(
                name: "CurriculumOrder",
                table: "ContentVersions");
        }
    }
}

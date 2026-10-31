using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledPathChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PathRevisionNumber",
                table: "Enrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PathChoiceRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    PathVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlacementAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PathStartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StudyDays = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SchedulingTimeZoneId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SelectionReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathChoiceRevisions", x => x.Id);
                    table.CheckConstraint("CK_PathChoice_Values", "[Number] >= 1 AND [EffectiveAt] >= [RecordedAt] AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_PathChoiceRevisions_Attempts_PlacementAttemptId",
                        column: x => x.PlacementAttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PathChoiceRevisions_Enrollments_EnrollmentId_UserId",
                        columns: x => new { x.EnrollmentId, x.UserId },
                        principalTable: "Enrollments",
                        principalColumns: new[] { "Id", "UserId" });
                    table.ForeignKey(
                        name: "FK_PathChoiceRevisions_PathVersions_PathVersionId",
                        column: x => x.PathVersionId,
                        principalTable: "PathVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PathChoiceRevisions_EnrollmentId_Number",
                table: "PathChoiceRevisions",
                columns: new[] { "EnrollmentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathChoiceRevisions_EnrollmentId_UserId",
                table: "PathChoiceRevisions",
                columns: new[] { "EnrollmentId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathChoiceRevisions_PathVersionId",
                table: "PathChoiceRevisions",
                column: "PathVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PathChoiceRevisions_PlacementAttemptId",
                table: "PathChoiceRevisions",
                column: "PlacementAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PathChoiceRevisions");

            migrationBuilder.DropColumn(
                name: "PathRevisionNumber",
                table: "Enrollments");
        }
    }
}

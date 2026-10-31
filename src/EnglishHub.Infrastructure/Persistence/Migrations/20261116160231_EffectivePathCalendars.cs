using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EffectivePathCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CalendarAnchorOn",
                table: "PathChoiceRevisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CalendarElapsedDays",
                table: "PathChoiceRevisions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UsesPathCalendar",
                table: "PathChoiceRevisions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PathCalendarAnchorOn",
                table: "Enrollments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PathCalendarElapsedDays",
                table: "Enrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UsesPathCalendar",
                table: "Enrollments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PathChoiceRevisionId",
                table: "DailyTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PathChoiceRevisions_EnrollmentId_Id",
                table: "PathChoiceRevisions",
                columns: new[] { "EnrollmentId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions",
                sql: "[Number] >= 1 AND [CalendarElapsedDays] >= 0 AND [EffectiveAt] >= [RecordedAt] AND ([ActivatedAt] IS NULL OR [ActivatedAt] >= [EffectiveAt]) AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Enrollment_Calendar",
                table: "Enrollments",
                sql: "[PathCalendarElapsedDays] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_EnrollmentId_PathChoiceRevisionId",
                table: "DailyTasks",
                columns: new[] { "EnrollmentId", "PathChoiceRevisionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_PathChoiceRevisions_EnrollmentId_PathChoiceRevisionId",
                table: "DailyTasks",
                columns: new[] { "EnrollmentId", "PathChoiceRevisionId" },
                principalTable: "PathChoiceRevisions",
                principalColumns: new[] { "EnrollmentId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_PathChoiceRevisions_EnrollmentId_PathChoiceRevisionId",
                table: "DailyTasks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PathChoiceRevisions_EnrollmentId_Id",
                table: "PathChoiceRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Enrollment_Calendar",
                table: "Enrollments");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_EnrollmentId_PathChoiceRevisionId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "CalendarAnchorOn",
                table: "PathChoiceRevisions");

            migrationBuilder.DropColumn(
                name: "CalendarElapsedDays",
                table: "PathChoiceRevisions");

            migrationBuilder.DropColumn(
                name: "UsesPathCalendar",
                table: "PathChoiceRevisions");

            migrationBuilder.DropColumn(
                name: "PathCalendarAnchorOn",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathCalendarElapsedDays",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "UsesPathCalendar",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathChoiceRevisionId",
                table: "DailyTasks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PathChoice_Values",
                table: "PathChoiceRevisions",
                sql: "[Number] >= 1 AND [EffectiveAt] >= [RecordedAt] AND ([ActivatedAt] IS NULL OR [ActivatedAt] >= [EffectiveAt]) AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))");
        }
    }
}

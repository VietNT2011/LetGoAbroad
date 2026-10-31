using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionedLearningPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskCompletions_AttemptId",
                table: "TaskCompletions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PathStartedOn",
                table: "Enrollments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathStudyDays",
                table: "Enrollments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathTimeZoneId",
                table: "Enrollments",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PathVersionId",
                table: "Enrollments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlacementAttemptId",
                table: "Enrollments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionReason",
                table: "Enrollments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<Guid>(
                name: "PathNodeId",
                table: "DailyTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PathVersionId",
                table: "DailyTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskId",
                table: "DailyTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "DailyTasks",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DailyTasks_Id_EnrollmentId",
                table: "DailyTasks",
                columns: new[] { "Id", "EnrollmentId" });

            migrationBuilder.CreateTable(
                name: "PathNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PathVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    StudyDay = table.Column<int>(type: "int", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletionRule = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MinimumCorrectPercent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathNodes", x => x.Id);
                    table.UniqueConstraint("AK_PathNodes_Id_ContentVersionId", x => new { x.Id, x.ContentVersionId });
                    table.UniqueConstraint("AK_PathNodes_PathVersionId_Id", x => new { x.PathVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_PathNodes_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PathPrerequisites",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrerequisiteNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PathVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathPrerequisites", x => new { x.NodeId, x.PrerequisiteNodeId });
                    table.ForeignKey(
                        name: "FK_PathPrerequisites_PathNodes_PathVersionId_NodeId",
                        columns: x => new { x.PathVersionId, x.NodeId },
                        principalTable: "PathNodes",
                        principalColumns: new[] { "PathVersionId", "Id" });
                    table.ForeignKey(
                        name: "FK_PathPrerequisites_PathNodes_PathVersionId_PrerequisiteNodeId",
                        columns: x => new { x.PathVersionId, x.PrerequisiteNodeId },
                        principalTable: "PathNodes",
                        principalColumns: new[] { "PathVersionId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "PathStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PathVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathStages", x => x.Id);
                    table.UniqueConstraint("AK_PathStages_Id_PathVersionId", x => new { x.Id, x.PathVersionId });
                });

            migrationBuilder.CreateTable(
                name: "PathTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CurrentPublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PathVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Attribution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathVersions", x => x.Id);
                    table.UniqueConstraint("AK_PathVersions_TemplateId_Id", x => new { x.TemplateId, x.Id });
                    table.ForeignKey(
                        name: "FK_PathVersions_PathTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PathTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_AttemptId",
                table: "TaskCompletions",
                column: "AttemptId",
                unique: true,
                filter: "[AttemptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_PathVersionId",
                table: "Enrollments",
                column: "PathVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_PlacementAttemptId",
                table: "Enrollments",
                column: "PlacementAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_EnrollmentId_PathNodeId",
                table: "DailyTasks",
                columns: new[] { "EnrollmentId", "PathNodeId" },
                unique: true,
                filter: "[PathNodeId] IS NOT NULL AND [Status] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_PathNodeId_ContentVersionId",
                table: "DailyTasks",
                columns: new[] { "PathNodeId", "ContentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_PathVersionId_PathNodeId",
                table: "DailyTasks",
                columns: new[] { "PathVersionId", "PathNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_SourceTaskId_EnrollmentId",
                table: "DailyTasks",
                columns: new[] { "SourceTaskId", "EnrollmentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DailyTask_PathPair",
                table: "DailyTasks",
                sql: "([PathNodeId] IS NULL AND [PathVersionId] IS NULL) OR ([PathNodeId] IS NOT NULL AND [PathVersionId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PathNodes_ContentVersionId",
                table: "PathNodes",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PathNodes_PathVersionId_Key",
                table: "PathNodes",
                columns: new[] { "PathVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathNodes_StageId_Order",
                table: "PathNodes",
                columns: new[] { "StageId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathNodes_StageId_PathVersionId",
                table: "PathNodes",
                columns: new[] { "StageId", "PathVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathPrerequisites_PathVersionId_NodeId",
                table: "PathPrerequisites",
                columns: new[] { "PathVersionId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathPrerequisites_PathVersionId_PrerequisiteNodeId",
                table: "PathPrerequisites",
                columns: new[] { "PathVersionId", "PrerequisiteNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathStages_PathVersionId_Key",
                table: "PathStages",
                columns: new[] { "PathVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathStages_PathVersionId_Order",
                table: "PathStages",
                columns: new[] { "PathVersionId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathTemplates_Id_CurrentPublishedVersionId",
                table: "PathTemplates",
                columns: new[] { "Id", "CurrentPublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathTemplates_Slug",
                table: "PathTemplates",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PathVersions_TemplateId_Version",
                table: "PathVersions",
                columns: new[] { "TemplateId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_DailyTasks_SourceTaskId_EnrollmentId",
                table: "DailyTasks",
                columns: new[] { "SourceTaskId", "EnrollmentId" },
                principalTable: "DailyTasks",
                principalColumns: new[] { "Id", "EnrollmentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_PathNodes_PathNodeId_ContentVersionId",
                table: "DailyTasks",
                columns: new[] { "PathNodeId", "ContentVersionId" },
                principalTable: "PathNodes",
                principalColumns: new[] { "Id", "ContentVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_PathNodes_PathVersionId_PathNodeId",
                table: "DailyTasks",
                columns: new[] { "PathVersionId", "PathNodeId" },
                principalTable: "PathNodes",
                principalColumns: new[] { "PathVersionId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_Attempts_PlacementAttemptId",
                table: "Enrollments",
                column: "PlacementAttemptId",
                principalTable: "Attempts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Enrollments_PathVersions_PathVersionId",
                table: "Enrollments",
                column: "PathVersionId",
                principalTable: "PathVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PathNodes_PathStages_StageId_PathVersionId",
                table: "PathNodes",
                columns: new[] { "StageId", "PathVersionId" },
                principalTable: "PathStages",
                principalColumns: new[] { "Id", "PathVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PathStages_PathVersions_PathVersionId",
                table: "PathStages",
                column: "PathVersionId",
                principalTable: "PathVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PathTemplates_PathVersions_Id_CurrentPublishedVersionId",
                table: "PathTemplates",
                columns: new[] { "Id", "CurrentPublishedVersionId" },
                principalTable: "PathVersions",
                principalColumns: new[] { "TemplateId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_DailyTasks_SourceTaskId_EnrollmentId",
                table: "DailyTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_PathNodes_PathNodeId_ContentVersionId",
                table: "DailyTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_PathNodes_PathVersionId_PathNodeId",
                table: "DailyTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_Attempts_PlacementAttemptId",
                table: "Enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_Enrollments_PathVersions_PathVersionId",
                table: "Enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_PathTemplates_PathVersions_Id_CurrentPublishedVersionId",
                table: "PathTemplates");

            migrationBuilder.DropTable(
                name: "PathPrerequisites");

            migrationBuilder.DropTable(
                name: "PathNodes");

            migrationBuilder.DropTable(
                name: "PathStages");

            migrationBuilder.DropTable(
                name: "PathVersions");

            migrationBuilder.DropTable(
                name: "PathTemplates");

            migrationBuilder.DropIndex(
                name: "IX_TaskCompletions_AttemptId",
                table: "TaskCompletions");

            migrationBuilder.DropIndex(
                name: "IX_Enrollments_PathVersionId",
                table: "Enrollments");

            migrationBuilder.DropIndex(
                name: "IX_Enrollments_PlacementAttemptId",
                table: "Enrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DailyTasks_Id_EnrollmentId",
                table: "DailyTasks");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_EnrollmentId_PathNodeId",
                table: "DailyTasks");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_PathNodeId_ContentVersionId",
                table: "DailyTasks");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_PathVersionId_PathNodeId",
                table: "DailyTasks");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_SourceTaskId_EnrollmentId",
                table: "DailyTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DailyTask_PathPair",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "PathStartedOn",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathStudyDays",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathTimeZoneId",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathVersionId",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PlacementAttemptId",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "SelectionReason",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "PathNodeId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "PathVersionId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "SourceTaskId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "DailyTasks");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletions_AttemptId",
                table: "TaskCompletions",
                column: "AttemptId");
        }
    }
}

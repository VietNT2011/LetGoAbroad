using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordingSourceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "RecordingReflections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceContentVersionId",
                table: "RecordingReflections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSegmentId",
                table: "RecordingReflections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecordingReflectionRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReflectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Clarity = table.Column<int>(type: "int", nullable: true),
                    Rhythm = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceSegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingReflectionRevisions", x => x.Id);
                    table.CheckConstraint("CK_RecordingReflectionRevision_Rubric", "([Clarity] IS NULL OR [Clarity] BETWEEN 1 AND 5) AND ([Rhythm] IS NULL OR [Rhythm] BETWEEN 1 AND 5)");
                    table.CheckConstraint("CK_RecordingReflectionRevision_Source", "[Number] >= 0 AND ([SourceSegmentId] IS NULL OR [SourceContentVersionId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RecordingReflectionRevisions_ContentVersions_SourceContentVersionId",
                        column: x => x.SourceContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecordingReflectionRevisions_RecordingReflections_ReflectionId",
                        column: x => x.ReflectionId,
                        principalTable: "RecordingReflections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecordingReflectionRevisions_TranscriptSegments_SourceSegmentId_SourceContentVersionId",
                        columns: x => new { x.SourceSegmentId, x.SourceContentVersionId },
                        principalTable: "TranscriptSegments",
                        principalColumns: new[] { "Id", "ContentVersionId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordingReflections_SourceContentVersionId",
                table: "RecordingReflections",
                column: "SourceContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingReflections_SourceSegmentId_SourceContentVersionId",
                table: "RecordingReflections",
                columns: new[] { "SourceSegmentId", "SourceContentVersionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecordingReflection_Source",
                table: "RecordingReflections",
                sql: "[Revision] >= 0 AND ([SourceSegmentId] IS NULL OR [SourceContentVersionId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingReflectionRevisions_ReflectionId_Number",
                table: "RecordingReflectionRevisions",
                columns: new[] { "ReflectionId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordingReflectionRevisions_SourceContentVersionId",
                table: "RecordingReflectionRevisions",
                column: "SourceContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingReflectionRevisions_SourceSegmentId_SourceContentVersionId",
                table: "RecordingReflectionRevisions",
                columns: new[] { "SourceSegmentId", "SourceContentVersionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RecordingReflections_ContentVersions_SourceContentVersionId",
                table: "RecordingReflections",
                column: "SourceContentVersionId",
                principalTable: "ContentVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecordingReflections_TranscriptSegments_SourceSegmentId_SourceContentVersionId",
                table: "RecordingReflections",
                columns: new[] { "SourceSegmentId", "SourceContentVersionId" },
                principalTable: "TranscriptSegments",
                principalColumns: new[] { "Id", "ContentVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecordingReflections_ContentVersions_SourceContentVersionId",
                table: "RecordingReflections");

            migrationBuilder.DropForeignKey(
                name: "FK_RecordingReflections_TranscriptSegments_SourceSegmentId_SourceContentVersionId",
                table: "RecordingReflections");

            migrationBuilder.DropTable(
                name: "RecordingReflectionRevisions");

            migrationBuilder.DropIndex(
                name: "IX_RecordingReflections_SourceContentVersionId",
                table: "RecordingReflections");

            migrationBuilder.DropIndex(
                name: "IX_RecordingReflections_SourceSegmentId_SourceContentVersionId",
                table: "RecordingReflections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecordingReflection_Source",
                table: "RecordingReflections");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "RecordingReflections");

            migrationBuilder.DropColumn(
                name: "SourceContentVersionId",
                table: "RecordingReflections");

            migrationBuilder.DropColumn(
                name: "SourceSegmentId",
                table: "RecordingReflections");
        }
    }
}

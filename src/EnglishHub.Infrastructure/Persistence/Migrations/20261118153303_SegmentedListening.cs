using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SegmentedListening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ListeningSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningSessions", x => x.Id);
                    table.UniqueConstraint("AK_ListeningSessions_Id_UserId_ContentVersionId", x => new { x.Id, x.UserId, x.ContentVersionId });
                    table.ForeignKey(
                        name: "FK_ListeningSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningSessions_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningSessions_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TranscriptSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    StartMs = table.Column<long>(type: "bigint", nullable: false),
                    EndMs = table.Column<long>(type: "bigint", nullable: false),
                    TextEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TextVi = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AcceptedAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizationPolicyVersion = table.Column<int>(type: "int", nullable: false),
                    IgnoreTerminalPunctuation = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptSegments", x => x.Id);
                    table.UniqueConstraint("AK_TranscriptSegments_Id_ContentVersionId", x => new { x.Id, x.ContentVersionId });
                    table.CheckConstraint("CK_Segment_Timing", "[StartMs] >= 0 AND [EndMs] > [StartMs] AND [Order] >= 0 AND [NormalizationPolicyVersion] = 1");
                    table.ForeignKey(
                        name: "FK_TranscriptSegments_ContentVersions_ContentVersionId",
                        column: x => x.ContentVersionId,
                        principalTable: "ContentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ListeningResumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningResumes", x => x.Id);
                    table.CheckConstraint("CK_Listening_Position", "[PositionMs] >= 0");
                    table.ForeignKey(
                        name: "FK_ListeningResumes_ListeningSessions_SessionId_UserId_ContentVersionId",
                        columns: x => new { x.SessionId, x.UserId, x.ContentVersionId },
                        principalTable: "ListeningSessions",
                        principalColumns: new[] { "Id", "UserId", "ContentVersionId" });
                });

            migrationBuilder.CreateTable(
                name: "ListeningDisclosures",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstDisclosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningDisclosures", x => new { x.UserId, x.SegmentId });
                    table.ForeignKey(
                        name: "FK_ListeningDisclosures_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningDisclosures_TranscriptSegments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "TranscriptSegments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SegmentAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    IsAssisted = table.Column<bool>(type: "bit", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentAnswers", x => x.Id);
                    table.CheckConstraint("CK_SegmentAnswer_Number", "[Number] >= 1 AND [Number] <= 20");
                    table.ForeignKey(
                        name: "FK_SegmentAnswers_ListeningSessions_SessionId_UserId_ContentVersionId",
                        columns: x => new { x.SessionId, x.UserId, x.ContentVersionId },
                        principalTable: "ListeningSessions",
                        principalColumns: new[] { "Id", "UserId", "ContentVersionId" });
                    table.ForeignKey(
                        name: "FK_SegmentAnswers_TranscriptSegments_SegmentId_ContentVersionId",
                        columns: x => new { x.SegmentId, x.ContentVersionId },
                        principalTable: "TranscriptSegments",
                        principalColumns: new[] { "Id", "ContentVersionId" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningDisclosures_SegmentId",
                table: "ListeningDisclosures",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningResumes_SessionId_UserId_ContentVersionId",
                table: "ListeningResumes",
                columns: new[] { "SessionId", "UserId", "ContentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningResumes_UserId_ContentVersionId",
                table: "ListeningResumes",
                columns: new[] { "UserId", "ContentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningSessions_ContentVersionId",
                table: "ListeningSessions",
                column: "ContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningSessions_MediaAssetId",
                table: "ListeningSessions",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningSessions_UserId",
                table: "ListeningSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnswers_SegmentId_ContentVersionId",
                table: "SegmentAnswers",
                columns: new[] { "SegmentId", "ContentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnswers_SessionId_SegmentId_Number",
                table: "SegmentAnswers",
                columns: new[] { "SessionId", "SegmentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnswers_SessionId_UserId_ContentVersionId",
                table: "SegmentAnswers",
                columns: new[] { "SessionId", "UserId", "ContentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptSegments_ContentVersionId_Order",
                table: "TranscriptSegments",
                columns: new[] { "ContentVersionId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningDisclosures");

            migrationBuilder.DropTable(
                name: "ListeningResumes");

            migrationBuilder.DropTable(
                name: "SegmentAnswers");

            migrationBuilder.DropTable(
                name: "ListeningSessions");

            migrationBuilder.DropTable(
                name: "TranscriptSegments");
        }
    }
}

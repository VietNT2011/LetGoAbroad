using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentEvidenceDisclosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EvidencePolicyVersion",
                table: "Attempts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstDisclosedAt",
                table: "Attempts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_UserId_FirstDisclosedAt",
                table: "Attempts",
                columns: new[] { "UserId", "FirstDisclosedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attempts_UserId_FirstDisclosedAt",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "EvidencePolicyVersion",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "FirstDisclosedAt",
                table: "Attempts");
        }
    }
}

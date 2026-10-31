using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FillBlankAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // VI: Backfill câu trắc nghiệm cũ bằng JSON hợp lệ và policy v1. EN: Preserve existing choice questions/attempts with valid empty variants and the original policy.
            migrationBuilder.AddColumn<string>(
                name: "AcceptedAnswerVariantsJson",
                table: "Questions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreTerminalPunctuation",
                table: "Questions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NormalizationPolicyVersion",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrectOptionId",
                table: "GradingKey",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "AcceptedAnswerVariantsJson",
                table: "GradingKey",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreTerminalPunctuation",
                table: "GradingKey",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NormalizationPolicyVersion",
                table: "GradingKey",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "AnswerText",
                table: "AttemptItems",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "AttemptItems",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAnswerVariantsJson",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IgnoreTerminalPunctuation",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "NormalizationPolicyVersion",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "AcceptedAnswerVariantsJson",
                table: "GradingKey");

            migrationBuilder.DropColumn(
                name: "IgnoreTerminalPunctuation",
                table: "GradingKey");

            migrationBuilder.DropColumn(
                name: "NormalizationPolicyVersion",
                table: "GradingKey");

            migrationBuilder.DropColumn(
                name: "AnswerText",
                table: "AttemptItems");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "AttemptItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrectOptionId",
                table: "GradingKey",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}

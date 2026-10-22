using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitectureBase.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<float>(type: "numeric(5,2)", nullable: false, defaultValue: 0f),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestQuestions_Tests_TestId",
                        column: x => x.TestId,
                        principalTable: "Tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_QuestionId",
                table: "TestQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestId_QuestionId",
                table: "TestQuestions",
                columns: new[] { "TestId", "QuestionId" },
                unique: true);

            // Chuyển dữ liệu cũ: trước đây bản gốc là TestVersion No = 0 và điểm nằm ở Question.Score.
            // Thứ tự đánh lại 0..n-1 theo Order cũ; câu trùng trong cùng bài chỉ giữ lần đầu.
            migrationBuilder.Sql("""
                INSERT INTO "TestQuestions" ("Id", "TestId", "QuestionId", "Score", "Order")
                SELECT gen_random_uuid(), x."TestId", x."QuestionId", x."Score",
                       (ROW_NUMBER() OVER (PARTITION BY x."TestId" ORDER BY x."Order", x."VersionQuestionId") - 1)::int
                FROM (
                    SELECT DISTINCT ON (tv."TestId", tvq."QuestionId")
                           tv."TestId", tvq."QuestionId", q."Score", tvq."Order", tvq."Id" AS "VersionQuestionId"
                    FROM "TestVersionQuestions" tvq
                    JOIN "TestVersions" tv ON tv."Id" = tvq."TestVersionId"
                    JOIN "Questions" q ON q."Id" = tvq."QuestionId"
                    WHERE tv."No" = 0
                    ORDER BY tv."TestId", tvq."QuestionId", tvq."Order", tvq."Id"
                ) x;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestQuestions");
        }
    }
}

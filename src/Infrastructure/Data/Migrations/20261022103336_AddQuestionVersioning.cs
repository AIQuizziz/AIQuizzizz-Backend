using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitectureBase.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceQuestionId",
                table: "Questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceVersion",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Questions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "FolderQuestionSets",
                columns: table => new
                {
                    FolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderQuestionSets", x => new { x.FolderId, x.QuestionSetId });
                    table.ForeignKey(
                        name: "FK_FolderQuestionSets_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FolderQuestionSets_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FolderQuestionSets_QuestionSets_QuestionSetId",
                        column: x => x.QuestionSetId,
                        principalTable: "QuestionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SourceQuestionId",
                table: "Questions",
                column: "SourceQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_FolderQuestionSets_CreatedBy",
                table: "FolderQuestionSets",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FolderQuestionSets_QuestionSetId",
                table: "FolderQuestionSets",
                column: "QuestionSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Questions_SourceQuestionId",
                table: "Questions",
                column: "SourceQuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Bỏ mẫu đề thi: mỗi mẫu (chưa xoá) thành một bộ câu hỏi riêng tư cùng Id, kèm câu hỏi (chép mới),
            // quyền chia sẻ và vị trí trong thư mục. Câu cũ của mẫu giữ nguyên vì bài test có thể đang dùng chung.
            migrationBuilder.Sql("""
                INSERT INTO "QuestionSets" ("Id", "Name", "Description", "VisibilityMode", "QuestionCount", "IsDeleted",
                                            "Created", "CreatedBy", "LastModified", "LastModifiedBy", "RatingAverage", "RatingCount", "RatingSum")
                SELECT t."Id", left(t."Name", 200), left(t."Description", 500), 'Private', 0, false,
                       t."Created", t."CreatedBy", t."LastModified", t."LastModifiedBy", 0, 0, 0
                FROM "TestTemplates" t
                WHERE NOT t."IsDeleted"
                  AND NOT EXISTS (SELECT 1 FROM "QuestionSets" qs WHERE qs."Id" = t."Id");

                INSERT INTO "Questions" ("Id", "QuestionSetId", "Type", "QuestionText", "TextFormat", "ExplainText", "Score", "DataJson",
                                         "Created", "CreatedBy", "LastModified", "LastModifiedBy", "IsDeleted", "MediaId", "MediaType", "Order", "Version")
                SELECT gen_random_uuid(), ttq."TestTemplateId", q."Type", q."QuestionText", q."TextFormat", q."ExplainText", q."Score", q."DataJson",
                       q."Created", q."CreatedBy", q."LastModified", q."LastModifiedBy", false, q."MediaId", q."MediaType",
                       (ROW_NUMBER() OVER (PARTITION BY ttq."TestTemplateId" ORDER BY q."Order", ttq."Created", q."Id") - 1)::int, 1
                FROM "TestTemplateQuestions" ttq
                JOIN "Questions" q ON q."Id" = ttq."QuestionId"
                JOIN "TestTemplates" t ON t."Id" = ttq."TestTemplateId" AND NOT t."IsDeleted"
                WHERE NOT q."IsDeleted";

                UPDATE "QuestionSets" qs SET "QuestionCount" = c.n
                FROM (SELECT "QuestionSetId", count(*) AS n FROM "Questions"
                      WHERE NOT "IsDeleted" AND "QuestionSetId" IS NOT NULL GROUP BY "QuestionSetId") c
                WHERE c."QuestionSetId" = qs."Id" AND qs."Id" IN (SELECT "Id" FROM "TestTemplates");

                INSERT INTO "QuestionSetUsers" ("UserId", "QuestionSetId", "ShareMode", "Created", "CreatedBy", "LastModified", "LastModifiedBy")
                SELECT u."UserId", u."TestTemplateId", u."ShareMode", u."Created", u."CreatedBy", u."LastModified", u."LastModifiedBy"
                FROM "TestTemplateUsers" u
                JOIN "TestTemplates" t ON t."Id" = u."TestTemplateId" AND NOT t."IsDeleted"
                ON CONFLICT DO NOTHING;

                INSERT INTO "FolderQuestionSets" ("FolderId", "QuestionSetId", "Created", "CreatedBy", "LastModified", "LastModifiedBy")
                SELECT f."FolderId", f."TestTemplateId", f."Created", f."CreatedBy", f."LastModified", f."LastModifiedBy"
                FROM "FolderTestTemplates" f
                JOIN "TestTemplates" t ON t."Id" = f."TestTemplateId" AND NOT t."IsDeleted"
                ON CONFLICT DO NOTHING;
                """);

            // ContentHash của câu hỏi cũ được tính bằng C# lúc khởi động (ApplicationDbContextInitialiser),
            // vì hash dùng DataJson đã chuẩn hoá (bỏ id / thứ tự trộn), không viết lại được bằng SQL.

            migrationBuilder.DropTable(
                name: "FolderTestTemplates");

            migrationBuilder.DropTable(
                name: "TestTemplateQuestions");

            migrationBuilder.DropTable(
                name: "TestTemplateUsers");

            migrationBuilder.DropTable(
                name: "TestTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Questions_SourceQuestionId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "FolderQuestionSets");

            migrationBuilder.DropIndex(
                name: "IX_Questions_SourceQuestionId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceQuestionId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Questions");

            migrationBuilder.CreateTable(
                name: "TestTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestTemplates_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FolderTestTemplates",
                columns: table => new
                {
                    FolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderTestTemplates", x => new { x.FolderId, x.TestTemplateId });
                    table.ForeignKey(
                        name: "FK_FolderTestTemplates_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FolderTestTemplates_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FolderTestTemplates_TestTemplates_TestTemplateId",
                        column: x => x.TestTemplateId,
                        principalTable: "TestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestTemplateQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestTemplateQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestTemplateQuestions_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestTemplateQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestTemplateQuestions_TestTemplates_TestTemplateId",
                        column: x => x.TestTemplateId,
                        principalTable: "TestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestTemplateUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsViewed = table.Column<bool>(type: "boolean", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ShareMode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestTemplateUsers", x => new { x.UserId, x.TestTemplateId });
                    table.ForeignKey(
                        name: "FK_TestTemplateUsers_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestTemplateUsers_DomainUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestTemplateUsers_TestTemplates_TestTemplateId",
                        column: x => x.TestTemplateId,
                        principalTable: "TestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderTestTemplates_CreatedBy",
                table: "FolderTestTemplates",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FolderTestTemplates_TestTemplateId",
                table: "FolderTestTemplates",
                column: "TestTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplateQuestions_CreatedBy",
                table: "TestTemplateQuestions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplateQuestions_QuestionId",
                table: "TestTemplateQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplateQuestions_TestTemplateId",
                table: "TestTemplateQuestions",
                column: "TestTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplates_CreatedBy",
                table: "TestTemplates",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplateUsers_CreatedBy",
                table: "TestTemplateUsers",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TestTemplateUsers_TestTemplateId",
                table: "TestTemplateUsers",
                column: "TestTemplateId");
        }
    }
}

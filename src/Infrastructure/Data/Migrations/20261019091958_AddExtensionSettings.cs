using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitectureBase.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExtensionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetQuestionSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    TranslateEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetLang = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StoreContext = table.Column<bool>(type: "boolean", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtensionSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtensionSettings_DomainUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExtensionSettings_DomainUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "DomainUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionSettings_CreatedBy",
                table: "ExtensionSettings",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionSettings_UserId",
                table: "ExtensionSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtensionSettings");
        }
    }
}

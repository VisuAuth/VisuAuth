using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sample.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdapterConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisuAuthAdapterConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Adapter = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    IsSecret = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisuAuthAdapterConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisuAuthAdapterConfigs_Adapter_Key",
                table: "VisuAuthAdapterConfigs",
                columns: new[] { "Adapter", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisuAuthAdapterConfigs");
        }
    }
}

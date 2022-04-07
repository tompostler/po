using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    public partial class SlashCommandId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Id",
                table: "SlashCommands",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Id",
                table: "SlashCommands");
        }
    }
}

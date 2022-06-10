using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    public partial class ScheduledBlobContainerName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContainerName",
                table: "ScheduledBlobs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContainerName",
                table: "ScheduledBlobs");
        }
    }
}

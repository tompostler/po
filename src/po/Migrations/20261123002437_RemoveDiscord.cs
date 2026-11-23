using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDiscord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SlashCommands only held Discord's registration cache, and is the only thing the foreign key pointed at.
            migrationBuilder.DropForeignKey(
                name: "FK_SlashCommandChannels_SlashCommands_SlashCommandName",
                table: "SlashCommandChannels");

            migrationBuilder.DropTable(
                name: "SlashCommands");

            // Renamed in place rather than dropped and recreated, as scaffolded, so channels keep their enablement and container association.
            migrationBuilder.DropPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels");

            migrationBuilder.RenameTable(
                name: "SlashCommandChannels",
                newName: "CommandChannels");

            migrationBuilder.RenameColumn(
                name: "SlashCommandName",
                table: "CommandChannels",
                newName: "CommandName");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommandChannels",
                table: "CommandChannels",
                columns: new[] { "CommandName", "Platform", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CommandChannels",
                table: "CommandChannels");

            migrationBuilder.RenameColumn(
                name: "CommandName",
                table: "CommandChannels",
                newName: "SlashCommandName");

            migrationBuilder.RenameTable(
                name: "CommandChannels",
                newName: "SlashCommandChannels");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels",
                columns: new[] { "SlashCommandName", "Platform", "ChannelId" });

            migrationBuilder.CreateTable(
                name: "SlashCommands",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Id = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    IsGuildLevel = table.Column<bool>(type: "bit", nullable: false),
                    RequiresChannelEnablement = table.Column<bool>(type: "bit", nullable: false),
                    SuccessfullyRegistered = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlashCommands", x => x.Name);
                });

            // The foreign key needs a row per enabled command name; leaving them unregistered makes the restored Discord code register them again.
            migrationBuilder.Sql("INSERT INTO SlashCommands (Name, Id, IsGuildLevel, RequiresChannelEnablement, SuccessfullyRegistered, Version) SELECT DISTINCT SlashCommandName, 0, 1, 1, NULL, 0 FROM SlashCommandChannels");

            migrationBuilder.AddForeignKey(
                name: "FK_SlashCommandChannels_SlashCommands_SlashCommandName",
                table: "SlashCommandChannels",
                column: "SlashCommandName",
                principalTable: "SlashCommands",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

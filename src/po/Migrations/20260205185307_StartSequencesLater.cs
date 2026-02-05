using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    /// <inheritdoc />
    public partial class StartSequencesLater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RestartSequence(
                name: "ScheduledMessageIds",
                startValue: 10L);

            migrationBuilder.RestartSequence(
                name: "ScheduledBlobIds",
                startValue: 1200L);

            migrationBuilder.RestartSequence(
                name: "RandomMessageIds",
                startValue: 600L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RestartSequence(
                name: "ScheduledMessageIds",
                startValue: 1L);

            migrationBuilder.RestartSequence(
                name: "ScheduledBlobIds",
                startValue: 1L);

            migrationBuilder.RestartSequence(
                name: "RandomMessageIds",
                startValue: 1L);
        }
    }
}

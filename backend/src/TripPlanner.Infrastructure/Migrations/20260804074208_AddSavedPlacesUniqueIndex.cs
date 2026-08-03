using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedPlacesUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripId_DestinationId_SavedPlaces",
                table: "ItineraryItems",
                columns: new[] { "TripId", "DestinationId" },
                unique: true,
                filter: "\"ItineraryDayId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItineraryItems_TripId_DestinationId_SavedPlaces",
                table: "ItineraryItems");
        }
    }
}

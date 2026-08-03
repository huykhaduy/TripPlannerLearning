using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripPlanner.Infrastructure.Migrations
{
    /// <summary>
    /// Deliberately empty, and deliberately kept.
    ///
    /// Marking BaseEntity.Id as ValueGeneratedNever() is a model-metadata change:
    /// it stops EF classifying an entity that already has a key as an existing row
    /// (UPDATE) instead of a new one (INSERT). A Guid primary key on Postgres was
    /// never store-generated, so no column DDL changes and there is nothing to run.
    ///
    /// It exists so ApplicationDbContextModelSnapshot records the new metadata —
    /// the snapshot is what the next migration diffs against. Deleting this file
    /// would silently fold the change into whatever migration comes next.
    /// </summary>
    public partial class SetIdValueGeneratedNever : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

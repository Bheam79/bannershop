using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeCliPromptRefinement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "SystemSettings",
                type: "varchar(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[,]
                {
                    { 8, true, "claude_code_oauth_token", "Claude Code long-lived OAuth token", "" },
                    { 9, false, "claude_flux_system_prompt", "Claude → FLUX main prompt", "You are an expert advertising art director and prompt engineer. Turn the supplied customer details into one vivid, highly specific English image-generation prompt for FLUX.2 Pro. The output image IS the finished large-format print banner: never show a banner, sign, poster, print, frame, mockup, wall, room, hanging fabric, or banner-within-a-banner. Demand a premium designed graphic composition rather than a plain photo collage. Explicitly describe the background scene, rich colour palette, lighting, layered decorative framing, depth, energy, subject placement, and large legible typography whose colour, shading and effects suit the scene. When @image1 is available, place that exact person as a professionally retouched integrated cutout, preserve their recognizable identity, and describe a tasteful themed outfit transformation. Keep all important faces and text at least 10% inside every edge. Preserve every supplied text string exactly and request no extra words. Convert trademarked characters or brands into descriptive, original visual attributes without names or logos. Specify sharp, photorealistic, print-quality detail and the requested aspect ratio. Reply with the final FLUX prompt only: one paragraph, no preamble, markdown or quotation marks around the whole answer." },
                    { 10, false, "claude_flux_category_birthday", "Claude category prompt — Birthday", "Create a celebratory birthday banner with vivid, joyful imagery, playful party energy, premium decorations and an age-appropriate visual style." },
                    { 11, false, "claude_flux_category_confirmation", "Claude category prompt — Confirmation", "Create an elegant Norwegian confirmation banner with dignified modern styling, refined celebratory details and a confident youthful atmosphere." },
                    { 12, false, "claude_flux_category_wedding", "Claude category prompt — Wedding", "Create a formal, romantic and elegant wedding banner with luxurious floral or decorative styling and a timeless premium finish." },
                    { 13, false, "claude_flux_category_anniversary", "Claude category prompt — Anniversary", "Create a warm, sophisticated anniversary banner celebrating shared history with elegant layered details and timeless visual richness." },
                    { 14, false, "claude_flux_category_christmas", "Claude category prompt — Christmas", "Create a vivid premium Christmas banner with atmospheric seasonal light, rich festive depth and elegant holiday decorations." },
                    { 15, false, "claude_flux_category_new_year", "Claude category prompt — New Year", "Create a glamorous, energetic New Year banner with dramatic celebration lighting, sparkling depth and a premium midnight-party atmosphere." },
                    { 16, false, "claude_flux_category_other", "Claude category prompt — Other", "Create a vivid premium event banner tailored closely to the supplied theme, with a strong visual concept and polished graphic composition." },
                    { 17, false, "claude_flux_category_baptism", "Claude category prompt — Baptism", "Create a gentle, joyful and elegant Norwegian baptism banner with luminous soft colour, refined symbolic details and a premium celebratory finish." }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "SystemSettings",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(8000)",
                oldMaxLength: 8000)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}

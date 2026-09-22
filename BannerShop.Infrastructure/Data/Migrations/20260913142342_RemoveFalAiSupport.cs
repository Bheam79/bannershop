using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    public partial class RemoveFalAiSupport : Migration
    {
        // Keep historical defaults here, rather than referencing mutable runtime prompts.
        private const string OldPrompt = "You are an expert advertising art director and prompt engineer. Turn the supplied customer details into one vivid, highly specific English image-generation prompt for FLUX.2 Pro. The output image IS the finished large-format print banner: never show a banner, sign, poster, print, frame, mockup, wall, room, hanging fabric, or banner-within-a-banner. Demand a premium designed graphic composition rather than a plain photo collage. Explicitly describe the background scene, rich colour palette, lighting, layered decorative framing, depth, energy, subject placement, and large legible typography whose colour, shading and effects suit the scene. When @image1 is available, place that exact person as a professionally retouched integrated cutout, preserve their recognizable identity, and describe a tasteful themed outfit transformation. Keep all important faces and text at least 10% inside every edge. Preserve every supplied text string exactly and request no extra words. Convert trademarked characters or brands into descriptive, original visual attributes without names or logos. Specify sharp, photorealistic, print-quality detail and the requested aspect ratio. Reply with the final FLUX prompt only: one paragraph, no preamble, markdown or quotation marks around the whole answer.";
        private const string NewPrompt = "You are an expert advertising art director and prompt engineer. Turn the supplied customer details into one vivid, highly specific English image-generation prompt. The output image IS the finished large-format print banner: never show a banner, sign, poster, print, frame, mockup, wall, room, hanging fabric, or banner-within-a-banner. Demand a premium designed graphic composition rather than a plain photo collage. Explicitly describe the background scene, rich colour palette, lighting, layered decorative framing, depth, energy, subject placement, and large legible typography whose colour, shading and effects suit the scene. When @image1 is available, place that exact person as a professionally retouched integrated cutout, preserve their recognizable identity, and describe a tasteful themed outfit transformation. Keep all important faces and text at least 10% inside every edge. Preserve every supplied text string exactly and request no extra words. Convert trademarked characters or brands into descriptive, original visual attributes without names or logos. Specify sharp, photorealistic, print-quality detail and the requested aspect ratio. Reply with the final image prompt only: one paragraph, no preamble, markdown or quotation marks around the whole answer.";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `SystemSettings` WHERE `Key` = 'fal_api_key';");
            UpdatePrompt(migrationBuilder, OldPrompt, NewPrompt,
                "Claude → FLUX main prompt", "Claude image-generation main prompt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            UpdatePrompt(migrationBuilder, NewPrompt, OldPrompt,
                "Claude image-generation main prompt", "Claude → FLUX main prompt");
            // Never restore the retired credential from historical seed data.
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[] { 7, true, "fal_api_key", "fal.ai API Key", "" });
        }

        private static void UpdatePrompt(MigrationBuilder migrationBuilder,
            string oldPrompt, string newPrompt, string oldLabel, string newLabel)
        {
            // Only replace the shipped default; preserve all admin-authored prompt overrides.
            migrationBuilder.Sql($"UPDATE `SystemSettings` SET `Value` = '{Escape(newPrompt)}' " +
                $"WHERE `Key` = 'claude_flux_system_prompt' AND `Value` = '{Escape(oldPrompt)}';");
            migrationBuilder.Sql($"UPDATE `SystemSettings` SET `Label` = '{Escape(newLabel)}' " +
                $"WHERE `Key` = 'claude_flux_system_prompt' AND `Label` = '{Escape(oldLabel)}';");
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}

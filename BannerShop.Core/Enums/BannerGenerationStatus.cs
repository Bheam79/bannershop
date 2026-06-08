namespace BannerShop.Core.Enums;

/// <summary>
/// Lifecycle state of a single <see cref="Entities.BannerGeneration"/> attempt.
/// </summary>
public enum BannerGenerationStatus
{
    /// <summary>Row created (by the /regenerate endpoint); waiting for the pipeline to pick it up.</summary>
    Pending = 1,

    /// <summary>Pipeline has started processing this generation.</summary>
    Processing = 2,

    /// <summary>Pipeline completed successfully — image files are available.</summary>
    Completed = 3,

    /// <summary>Pipeline failed — <see cref="Entities.BannerGeneration.ErrorMessage"/> is set.</summary>
    Failed = 4,
}

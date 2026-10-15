using Umbraco.Cms.Core.Models;

namespace UmbracoKalaset26.Models.ViewModels
{
    public class CarouselViewModel
    {
        /// <summary>Unique element id, targeted by the Bootstrap indicators and controls.</summary>
        public required string Id { get; init; }

        public required IReadOnlyList<CarouselSlide> Slides { get; init; }
    }

    public record CarouselSlide(MediaWithCrops Image, string? Caption = null);
}

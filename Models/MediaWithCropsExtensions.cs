using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Extensions;

namespace UmbracoKalaset26.Models;

public static class MediaWithCropsExtensions
{
    /// <summary>
    /// <c>object-position</c> for an image shown with <c>object-fit: cover</c>, taken from its focal
    /// point, or null when it has none (the crop then stays centred). Use it with an image that is only
    /// resized on the server, never cropped, so the browser makes the one and only crop:
    /// <c>object-position: X% Y%</c> maps the image's X%/Y% point onto the box's X%/Y% point, so the
    /// focal point is always inside the visible area at any size. Invariant formatting: on a Swedish
    /// page "62,5%" would be invalid CSS.
    /// </summary>
    public static string? GetFocalObjectPosition(this MediaWithCrops image)
    {
        var focalPoint = image.LocalCrops?.FocalPoint
            ?? image.Content.Value<ImageCropperValue>("umbracoFile")?.FocalPoint;
        return focalPoint is null
            ? null
            : FormattableString.Invariant($"object-position: {focalPoint.Left * 100:0.##}% {focalPoint.Top * 100:0.##}%");
    }
}

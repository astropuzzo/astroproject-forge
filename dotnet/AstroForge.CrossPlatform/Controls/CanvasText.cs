using AstroForge.App.Services;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>Text drawn directly by custom controls, translated like the rest of the interface.</summary>
internal static class CanvasText
{
    public static string Language { get; set; } = UiLocalization.English;

    public static string T(string italian) => UiLocalization.Translate(italian, Language);
}

using Meziantou.Framework;

namespace SvgToAssets.Models;

/// <summary>
/// One SVG to convert.
/// </summary>
/// <param name="Path">The SVG file.</param>
/// <param name="FolderName">Slug used as the output subfolder when a run converts several SVGs.</param>
internal sealed record SvgInput(FullPath Path, string FolderName);
